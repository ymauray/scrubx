using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Scrubx.Cli;

/// <summary>
/// Produit une copie annotée d'un .docx : chaque anomalie d'un
/// <see cref="ValidationReport"/> devient un commentaire Word ancré sur les
/// caractères fautifs, comme si un relecteur était passé sur le document.
///
/// Le texte du document n'est jamais modifié. Les runs sont en revanche
/// découpés là où il le faut pour que les marques de commentaire encadrent
/// exactement la plage signalée : la mise en forme de chaque moitié est
/// recopiée, si bien que le rendu est inchangé. L'utilisateur reste libre de
/// traiter ou d'ignorer chaque commentaire dans Word.
/// </summary>
public static class DocxReviewer
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string ContentTypesPart = "[Content_Types].xml";
    private const string CommentsPart = "word/comments.xml";
    private const string CommentsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml";
    private const string CommentsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";

    /// <summary>Auteur porté par les commentaires, pour pouvoir les filtrer dans Word.</summary>
    public const string Author = "Scrubx";
    public const string Initials = "SX";

    /// <summary>
    /// Écrit dans <paramref name="outputPath"/> une copie annotée de
    /// <paramref name="sourcePath"/>. Le fichier source n'est jamais modifié.
    /// </summary>
    /// <returns>Ce qui a été ajouté au document.</returns>
    public static ReviewResult Review(string sourcePath, string outputPath, ValidationReport report, DateTimeOffset? date = null)
    {
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Le fichier annoté doit être écrit ailleurs que sur le document source.", nameof(outputPath));
        }

        using var source = File.OpenRead(sourcePath);
        using var output = File.Create(outputPath);
        return Review(source, output, report, date);
    }

    /// <summary>
    /// Écrit dans <paramref name="output"/> une copie annotée de
    /// <paramref name="source"/>.
    /// </summary>
    /// <returns>Ce qui a été ajouté au document.</returns>
    public static ReviewResult Review(Stream source, Stream output, ValidationReport report, DateTimeOffset? date = null)
    {
        var stamp = (date ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

        var contentTypesEntry = archive.GetEntry(ContentTypesPart)
            ?? throw new InvalidDataException($"Archive invalide : '{ContentTypesPart}' est absent, ce n'est pas un fichier .docx.");

        // Un document peut déjà porter les commentaires d'un relecteur humain :
        // on s'ajoute à la suite plutôt que de les remplacer.
        var commentsDoc = LoadOrCreateComments(archive, out int nextCommentId);

        // Parties modifiées (document, notes, rels, content-types, commentaires),
        // écrites à la place de l'original au moment de la recopie de l'archive.
        var modifiedParts = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        var commentedParts = new List<string>();
        int commentsAdded = 0;
        var revisions = new RevisionWriter(stamp);

        var locatedErrors = report.Errors.Where(e => e.EntryName != null);

        foreach (var partGroup in locatedErrors.GroupBy(e => e.EntryName!))
        {
            var entry = archive.GetEntry(partGroup.Key);
            if (entry == null) continue;

            var partDoc = LoadXml(entry);
            // Même énumération que DocxValidator : l'index de paragraphe en dépend.
            var paragraphs = partDoc.Descendants(W + "p").ToList();
            if (paragraphs.Count == 0) continue;

            revisions.ReserveIdsUsedBy(partDoc);

            foreach (var paragraphGroup in partGroup.GroupBy(e => ResolveParagraphIndex(e.ParagraphIndex, paragraphs.Count)))
            {
                var paragraph = paragraphs[paragraphGroup.Key];

                // Les commentaires d'abord : ils n'altèrent pas le texte, donc
                // les offsets restent ceux relevés par le validateur.
                foreach (var error in paragraphGroup)
                {
                    int commentId = nextCommentId++;
                    commentsDoc.Root!.Add(BuildComment(commentId, error, stamp));
                    commentsAdded++;

                    AnchorComment(paragraph, commentId, error);
                }

                ApplyRevisions(paragraph, paragraphGroup, revisions);
            }

            modifiedParts[partGroup.Key] = partDoc;
            commentedParts.Add(partGroup.Key);
        }

        // La partie commentaires doit être atteignable depuis chaque partie qui
        // la référence (le corps, mais aussi les notes le cas échéant).
        foreach (var part in commentedParts)
        {
            var relsName = RelationshipsPartFor(part);
            var relsDoc = modifiedParts.TryGetValue(relsName, out var alreadyLoaded)
                ? alreadyLoaded
                : LoadOrCreateRelationships(archive, relsName);

            bool alreadyDeclared = relsDoc.Root!
                .Elements(RelationshipsNs + "Relationship")
                .Any(r => (string?)r.Attribute("Type") == CommentsRelationshipType);

            if (!alreadyDeclared)
            {
                relsDoc.Root.Add(new XElement(RelationshipsNs + "Relationship",
                    new XAttribute("Id", NextRelationshipId(relsDoc)),
                    new XAttribute("Type", CommentsRelationshipType),
                    new XAttribute("Target", "comments.xml")));
            }

            modifiedParts[relsName] = relsDoc;
        }

        if (commentsAdded > 0)
        {
            modifiedParts[ContentTypesPart] = DeclareCommentsContentType(LoadXml(contentTypesEntry));
            modifiedParts[CommentsPart] = commentsDoc;
        }

        WriteArchive(archive, output, modifiedParts);
        return new ReviewResult(commentsAdded, revisions.Count);
    }

    private static void WriteArchive(ZipArchive source, Stream output, Dictionary<string, XDocument> modifiedParts)
    {
        using var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var copied = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in source.Entries)
        {
            copied.Add(entry.FullName);

            if (modifiedParts.TryGetValue(entry.FullName, out var replacement))
            {
                WriteXml(destination, entry.FullName, replacement);
                continue;
            }

            var copy = destination.CreateEntry(entry.FullName);
            copy.LastWriteTime = entry.LastWriteTime;
            using var from = entry.Open();
            using var to = copy.Open();
            from.CopyTo(to);
        }

        foreach (var part in modifiedParts.Where(p => !copied.Contains(p.Key)))
        {
            WriteXml(destination, part.Key, part.Value);
        }
    }

    /// <summary>
    /// Pose les marques d'un commentaire autour des caractères fautifs. Si
    /// l'anomalie ne désigne pas de plage (règle qui porte sur le paragraphe
    /// entier), ou si la plage ne peut pas être résolue, le paragraphe entier
    /// est encadré.
    /// </summary>
    private static void AnchorComment(XElement paragraph, int commentId, ValidationError error)
    {
        var rangeStart = new XElement(W + "commentRangeStart", new XAttribute(W + "id", commentId));
        var rangeEnd = new XElement(W + "commentRangeEnd", new XAttribute(W + "id", commentId));
        var reference = new XElement(W + "r",
            new XElement(W + "commentReference", new XAttribute(W + "id", commentId)));

        var range = error.Offset is int offset
            ? IsolateRange(paragraph, offset, offset + error.Length)
            : null;

        if (range.HasValue)
        {
            var (first, last) = range.Value;
            // Les marques sont posées au niveau du paragraphe : à l'intérieur
            // d'un `w:ins` ou d'un `w:hyperlink`, le schéma ne les accepte pas.
            TopLevelAncestor(first, paragraph).AddBeforeSelf(rangeStart);
            var closing = TopLevelAncestor(last, paragraph);
            closing.AddAfterSelf(rangeEnd);
            rangeEnd.AddAfterSelf(reference);
            return;
        }

        // `w:pPr` doit rester le premier enfant du paragraphe.
        var properties = paragraph.Element(W + "pPr");
        if (properties != null)
        {
            properties.AddAfterSelf(rangeStart);
        }
        else
        {
            paragraph.AddFirst(rangeStart);
        }
        paragraph.Add(rangeEnd);
        paragraph.Add(reference);
    }

    /// <summary>
    /// Traduit en marques de révision les corrections proposées par les règles
    /// de ce paragraphe.
    /// </summary>
    private static void ApplyRevisions(XElement paragraph, IEnumerable<ValidationError> errors, RevisionWriter revisions)
    {
        var applied = new List<(int Start, int End)>();

        // De droite à gauche : une modification ne décale alors que du texte
        // déjà traité, donc les offsets restants restent valides.
        var suggestions = errors
            .Select(e => e.Suggestion)
            .Where(s => s != null && IsApplicable(s))
            .Select(s => s!)
            .OrderByDescending(s => s.Offset)
            .ToList();

        foreach (var suggestion in suggestions)
        {
            // Deux règles visent parfois les mêmes caractères (une espace en
            // trop qui est aussi l'espace de fin de paragraphe) : la seconde
            // marque ferait double emploi, seul son commentaire subsiste.
            if (OverlapsApplied(applied, suggestion)) continue;

            bool done = suggestion.Kind switch
            {
                SuggestedEditKind.Delete => TryDelete(paragraph, suggestion, revisions),
                SuggestedEditKind.Insert => TryInsert(paragraph, suggestion, revisions),
                SuggestedEditKind.ParagraphStyle => TryChangeStyle(paragraph, suggestion, revisions),
                SuggestedEditKind.RemovePageBreak => TryRemovePageBreak(paragraph, revisions),
                _ => false,
            };

            if (done)
            {
                applied.Add((suggestion.Offset, suggestion.Offset + suggestion.Length));
                revisions.Applied();
            }
        }
    }

    /// <summary>
    /// Les substitutions (`w:del` suivi de `w:ins`) sont deux révisions
    /// distinctes dans le fichier : tant qu'on n'a pas vérifié que Word les
    /// traite comme un tout — accepter l'une sans l'autre produirait un texte
    /// incohérent — elles ne sont pas posées, et l'anomalie reste signalée par
    /// son seul commentaire. Cf. ROADMAP.md §3.
    /// </summary>
    private static bool IsApplicable(SuggestedEdit? suggestion) =>
        suggestion != null && suggestion.Kind != SuggestedEditKind.Replace;

    private static bool OverlapsApplied(List<(int Start, int End)> applied, SuggestedEdit suggestion)
    {
        int start = suggestion.Offset;
        int end = start + suggestion.Length;

        foreach (var (appliedStart, appliedEnd) in applied)
        {
            bool overlaps = suggestion.Length == 0
                ? start > appliedStart && start < appliedEnd
                : start < appliedEnd && end > appliedStart;
            if (overlaps) return true;
        }

        return false;
    }

    /// <summary>Marque `[Offset, Offset + Length)` comme supprimé.</summary>
    private static bool TryDelete(XElement paragraph, SuggestedEdit suggestion, RevisionWriter revisions)
    {
        if (suggestion.Length <= 0) return false;

        var range = IsolateRange(paragraph, suggestion.Offset, suggestion.Offset + suggestion.Length);
        if (range == null) return false;

        var (first, last) = range.Value;
        var nodes = NodesBetween(TopLevelAncestor(first, paragraph), TopLevelAncestor(last, paragraph));
        if (nodes == null) return false;

        var deletion = revisions.Mark("del");
        nodes[0].AddBeforeSelf(deletion);

        // Une marque de commentaire ne peut pas vivre dans un `w:del` : elle est
        // repoussée de part et d'autre, ce qui préserve le surlignage.
        var trailing = new List<XElement>();
        foreach (var node in nodes)
        {
            node.Remove();

            if (node.Name == W + "commentRangeStart") deletion.AddBeforeSelf(node);
            else if (node.Name == W + "commentRangeEnd" || node.Descendants(W + "commentReference").Any()) trailing.Add(node);
            else deletion.Add(node);
        }
        deletion.AddAfterSelf(trailing);

        if (!deletion.Elements().Any())
        {
            deletion.Remove();
            return false;
        }

        // Dans une suppression, le texte se porte par `w:delText`.
        foreach (var textNode in deletion.Descendants(W + "t").ToList())
        {
            textNode.ReplaceWith(new XElement(W + "delText", textNode.Attributes(), textNode.Value));
        }

        return true;
    }

    /// <summary>Insère <see cref="SuggestedEdit.Text"/> comme texte ajouté.</summary>
    private static bool TryInsert(XElement paragraph, SuggestedEdit suggestion, RevisionWriter revisions)
    {
        if (suggestion.Text.Length == 0) return false;

        var map = ParagraphTextMap.Build(paragraph);
        if (suggestion.Offset < 0 || suggestion.Offset > map.Text.Length || map.Segments.Count == 0) return false;

        var insertion = revisions.Mark("ins");
        var run = new XElement(W + "r");

        if (suggestion.Offset == map.Text.Length)
        {
            // En toute fin de paragraphe : rien à découper.
            var lastSegment = map.Segments[^1];
            CopyFormatting(lastSegment.Text, run);
            run.Add(TextElement(suggestion.Text));
            insertion.Add(run);
            TopLevelAncestor(lastSegment.Text, paragraph).AddAfterSelf(insertion);
            return true;
        }

        var segment = map.SegmentContaining(suggestion.Offset);
        if (segment == null) return false;
        if (suggestion.Offset > segment.Value.Start
            && !TrySplitAt(segment.Value.Text, suggestion.Offset - segment.Value.Start)) return false;

        map = ParagraphTextMap.Build(paragraph);
        var target = map.Segments.FirstOrDefault(s => s.Start == suggestion.Offset);
        if (target.Text == null) return false;

        CopyFormatting(target.Text, run);
        run.Add(TextElement(suggestion.Text));
        insertion.Add(run);
        TopLevelAncestor(target.Text, paragraph).AddBeforeSelf(insertion);
        return true;
    }

    /// <summary>Propose un autre style de paragraphe, l'ancien étant conservé dans `w:pPrChange`.</summary>
    private static bool TryChangeStyle(XElement paragraph, SuggestedEdit suggestion, RevisionWriter revisions)
    {
        var properties = paragraph.Element(W + "pPr");
        var style = properties?.Element(W + "pStyle");
        if (properties == null || style == null) return false;

        var previous = SnapshotProperties(properties);
        style.SetAttributeValue(W + "val", suggestion.Text);
        RecordPropertiesChange(properties, previous, revisions);
        return true;
    }

    /// <summary>Propose de retirer la propriété « saut de page avant ».</summary>
    private static bool TryRemovePageBreak(XElement paragraph, RevisionWriter revisions)
    {
        var properties = paragraph.Element(W + "pPr");
        var pageBreak = properties?.Element(W + "pageBreakBefore");
        if (properties == null || pageBreak == null) return false;

        var previous = SnapshotProperties(properties);
        pageBreak.Remove();
        RecordPropertiesChange(properties, previous, revisions);
        return true;
    }

    private static XElement SnapshotProperties(XElement properties)
    {
        var copy = new XElement(properties);
        copy.Element(W + "pPrChange")?.Remove();
        return copy;
    }

    private static void RecordPropertiesChange(XElement properties, XElement previous, RevisionWriter revisions)
    {
        properties.Element(W + "pPrChange")?.Remove();

        var change = revisions.Mark("pPrChange");
        change.Add(previous);
        // `w:pPrChange` ferme la séquence des propriétés de paragraphe.
        properties.Add(change);
    }

    private static void CopyFormatting(XElement textNode, XElement run)
    {
        var properties = textNode.Parent?.Element(W + "rPr");
        if (properties != null) run.Add(new XElement(properties));
    }

    /// <summary>Éléments allant de <paramref name="first"/> à <paramref name="last"/> inclus.</summary>
    private static List<XElement>? NodesBetween(XElement first, XElement last)
    {
        var nodes = new List<XElement>();

        for (XElement? node = first; node != null; node = node.ElementsAfterSelf().FirstOrDefault())
        {
            nodes.Add(node);
            if (node == last) return nodes;
        }

        return null;
    }

    /// <summary>Distribue les identifiants de révision et retient ce qui a été posé.</summary>
    private sealed class RevisionWriter(string stamp)
    {
        private int _nextId = 1;

        public int Count { get; private set; }

        public XElement Mark(string name) => new(W + name,
            new XAttribute(W + "id", _nextId++),
            new XAttribute(W + "author", Author),
            new XAttribute(W + "date", stamp));

        public void Applied() => Count++;

        /// <summary>Évite de réutiliser un identifiant déjà porté par une révision existante.</summary>
        public void ReserveIdsUsedBy(XDocument part)
        {
            var revisionNames = new[] { W + "ins", W + "del", W + "pPrChange", W + "rPrChange" };
            int highest = part.Descendants()
                .Where(e => revisionNames.Contains(e.Name))
                .Select(e => int.TryParse((string?)e.Attribute(W + "id"), out int id) ? id : 0)
                .DefaultIfEmpty(0)
                .Max();

            if (_nextId <= highest) _nextId = highest + 1;
        }
    }

    /// <summary>
    /// Découpe les runs pour que les caractères `[start, end)` du texte du
    /// paragraphe occupent des `w:t` entiers, et renvoie le premier et le
    /// dernier d'entre eux.
    /// </summary>
    private static (XElement First, XElement Last)? IsolateRange(XElement paragraph, int start, int end)
    {
        if (start < 0 || end <= start) return null;

        // On coupe la fin puis le début : un découpage de run ne change pas le
        // texte du paragraphe, donc les offsets restent valides d'un bout à
        // l'autre. La table est reconstruite après chaque coupe.
        var map = ParagraphTextMap.Build(paragraph);
        if (end > map.Text.Length) return null;

        var last = map.SegmentContaining(end - 1);
        if (last == null) return null;
        if (end < last.Value.End && !TrySplitAt(last.Value.Text, end - last.Value.Start)) return null;

        map = ParagraphTextMap.Build(paragraph);
        var first = map.SegmentContaining(start);
        if (first == null) return null;
        if (start > first.Value.Start && !TrySplitAt(first.Value.Text, start - first.Value.Start)) return null;

        map = ParagraphTextMap.Build(paragraph);
        var covered = map.Segments.Where(s => s.Start >= start && s.End <= end).ToList();
        if (covered.Count == 0) return null;

        return (covered[0].Text, covered[^1].Text);
    }

    /// <summary>
    /// Scinde le run portant <paramref name="textNode"/> en deux, à
    /// <paramref name="localOffset"/> caractères du début de ce `w:t`. La mise
    /// en forme (`w:rPr`) est recopiée sur les deux moitiés.
    /// </summary>
    private static bool TrySplitAt(XElement textNode, int localOffset)
    {
        var run = textNode.Parent;
        if (run == null || run.Name != W + "r") return false;

        var value = textNode.Value;
        if (localOffset <= 0 || localOffset >= value.Length) return true;

        var tail = new XElement(run.Name, run.Attributes());
        var properties = run.Element(W + "rPr");
        if (properties != null) tail.Add(new XElement(properties));
        tail.Add(TextElement(value[localOffset..]));

        // Ce qui suivait le `w:t` dans le run d'origine part avec la queue.
        foreach (var following in textNode.ElementsAfterSelf().ToList())
        {
            following.Remove();
            tail.Add(following);
        }

        textNode.Value = value[..localOffset];
        PreserveSpace(textNode);
        run.AddAfterSelf(tail);
        return true;
    }

    /// <summary>Ancêtre de <paramref name="node"/> qui est enfant direct du paragraphe.</summary>
    private static XElement TopLevelAncestor(XElement node, XElement paragraph)
    {
        var current = node;
        while (current.Parent != null && current.Parent != paragraph)
        {
            current = current.Parent;
        }
        return current;
    }

    private static XElement TextElement(string value)
    {
        var element = new XElement(W + "t", value);
        PreserveSpace(element);
        return element;
    }

    /// <summary>
    /// Une moitié de run peut commencer ou finir par une espace — la moitié des
    /// règles portent justement sur des espaces — donc `xml:space` est
    /// systématiquement posé.
    /// </summary>
    private static void PreserveSpace(XElement textElement) =>
        textElement.SetAttributeValue(XNamespace.Xml + "space", "preserve");

    private static XElement BuildComment(int commentId, ValidationError error, string stamp)
    {
        var code = RuleCatalog.GetCode(error.RuleName);
        var prefix = code == null ? string.Empty : $"[{code}] ";
        var headline = error.IsWarning
            ? $"{prefix}Avertissement : {error.Message}"
            : $"{prefix}{error.Message}";

        var comment = new XElement(W + "comment",
            new XAttribute(W + "id", commentId),
            new XAttribute(W + "author", Author),
            new XAttribute(W + "initials", Initials),
            new XAttribute(W + "date", stamp),
            CommentParagraph(headline));

        if (!string.IsNullOrWhiteSpace(error.Context))
        {
            comment.Add(CommentParagraph($"Contexte : {error.Context}"));
        }

        return comment;
    }

    private static XElement CommentParagraph(string text) =>
        new XElement(W + "p", new XElement(W + "r", TextElement(text)));

    /// <summary>
    /// Une anomalie qui porte sur le document entier (index null), ou dont
    /// l'index ne retombe pas sur un paragraphe existant, est ancrée en tête
    /// de document.
    /// </summary>
    private static int ResolveParagraphIndex(int? index, int paragraphCount) =>
        index is int value && value >= 0 && value < paragraphCount ? value : 0;

    private static XDocument LoadOrCreateComments(ZipArchive archive, out int nextCommentId)
    {
        var entry = archive.GetEntry(CommentsPart);
        if (entry == null)
        {
            nextCommentId = 1;
            return new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(W + "comments", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName)));
        }

        var doc = LoadXml(entry);
        nextCommentId = doc.Root!
            .Elements(W + "comment")
            .Select(c => int.TryParse((string?)c.Attribute(W + "id"), out int id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return doc;
    }

    private static XDocument LoadOrCreateRelationships(ZipArchive archive, string relsName)
    {
        var entry = archive.GetEntry(relsName);
        if (entry != null) return LoadXml(entry);

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(RelationshipsNs + "Relationships",
                new XAttribute("xmlns", RelationshipsNs.NamespaceName)));
    }

    private static XDocument DeclareCommentsContentType(XDocument contentTypes)
    {
        bool alreadyDeclared = contentTypes.Root!
            .Elements(ContentTypesNs + "Override")
            .Any(o => (string?)o.Attribute("PartName") == "/" + CommentsPart);

        if (!alreadyDeclared)
        {
            contentTypes.Root.Add(new XElement(ContentTypesNs + "Override",
                new XAttribute("PartName", "/" + CommentsPart),
                new XAttribute("ContentType", CommentsContentType)));
        }

        return contentTypes;
    }

    private static string RelationshipsPartFor(string partName)
    {
        int separator = partName.LastIndexOf('/');
        return separator < 0
            ? $"_rels/{partName}.rels"
            : $"{partName[..separator]}/_rels/{partName[(separator + 1)..]}.rels";
    }

    private static string NextRelationshipId(XDocument relationships)
    {
        int max = relationships.Root!
            .Elements(RelationshipsNs + "Relationship")
            .Select(r => Regex.Match((string?)r.Attribute("Id") ?? string.Empty, @"^rId(\d+)$"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();
        return $"rId{max + 1}";
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static void WriteXml(ZipArchive destination, string partName, XDocument doc)
    {
        var entry = destination.CreateEntry(partName);
        using var stream = entry.Open();
        // DisableFormatting : aucune indentation ajoutée, sans quoi les espaces
        // parasites modifieraient le texte rendu par Word.
        doc.Save(stream, SaveOptions.DisableFormatting);
    }
}
