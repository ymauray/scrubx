using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Scrubx.Cli;

/// <summary>
/// Ajoute des commentaires Word à un package : partie <c>comments.xml</c> (et ses
/// compagnons modernes), déclarations dans <c>[Content_Types].xml</c> et dans les
/// relations du document, styles de commentaire.
/// </summary>
/// <remarks>
/// Les commentaires déjà présents dans le document sont conservés et les nouveaux
/// leur sont ajoutés à la suite.
/// </remarks>
internal sealed class DocxComments
{
    public const string CommentsPart = "word/comments.xml";
    public const string CommentsExtendedPart = "word/commentsExtended.xml";
    public const string CommentsIdsPart = "word/commentsIds.xml";
    private const string ContentTypesPart = "[Content_Types].xml";
    private const string DocumentRelsPart = "word/_rels/document.xml.rels";
    private const string StylesPart = "word/styles.xml";

    public const string ReferenceStyleId = "Marquedecommentaire";
    private const string TextStyleId = "Commentaire";
    private const string TextCharStyleId = "CommentaireCar";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private static readonly XNamespace W16CID = "http://schemas.microsoft.com/office/word/2016/wordml/cid";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly DocxPackage _package;
    private readonly string _author;
    private readonly string _initials;
    private readonly string _date;
    private readonly HashSet<string> _usedParaIds;
    private readonly HashSet<string> _usedDurableIds = new();

    private int _nextCommentId;
    private int _paraIdSeed;
    private int _durableIdSeed;
    private int _added;

    public DocxComments(DocxPackage package, XDocument document, string author, DateTimeOffset date)
    {
        _package = package;
        _author = author;
        _initials = Initials(author);
        _date = date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'");

        var comments = _package.TryLoad(CommentsPart);
        _nextCommentId = comments == null
            ? 0
            : comments.Root!.Elements(W + "comment")
                .Select(c => int.TryParse(c.Attribute(W + "id")?.Value, out var id) ? id : -1)
                .DefaultIfEmpty(-1)
                .Max() + 1;

        _usedParaIds = document.Descendants()
            .Select(e => e.Attribute(W14 + "paraId")?.Value)
            .Concat(comments?.Descendants().Select(e => e.Attribute(W14 + "paraId")?.Value) ?? [])
            .Where(value => value != null)
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Ajoute un commentaire et renvoie son identifiant.</summary>
    public int Add(string text)
    {
        var comments = _package.GetOrCreate(CommentsPart, () => NewPart(W + "comments", ("w14", W14)));
        var extended = _package.GetOrCreate(CommentsExtendedPart, () => NewPart(W15 + "commentsEx"));
        var ids = _package.GetOrCreate(CommentsIdsPart, () => NewPart(W16CID + "commentsIds"));

        var commentId = _nextCommentId++;
        var paraId = NextId(ref _paraIdSeed, 0x40000000, _usedParaIds);
        var durableId = NextId(ref _durableIdSeed, 0x50000000, _usedDurableIds);

        comments.Root!.Add(new XElement(
            W + "comment",
            new XAttribute(W + "id", commentId),
            new XAttribute(W + "author", _author),
            new XAttribute(W + "date", _date),
            new XAttribute(W + "initials", _initials),
            new XElement(
                W + "p",
                new XAttribute(W14 + "paraId", paraId),
                new XAttribute(W14 + "textId", "77777777"),
                new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", TextStyleId))),
                new XElement(
                    W + "r",
                    new XElement(W + "rPr", new XElement(W + "rStyle", new XAttribute(W + "val", ReferenceStyleId))),
                    new XElement(W + "annotationRef")),
                new XElement(W + "r", ParagraphTextMap.MakeText(text)))));

        extended.Root!.Add(new XElement(
            W15 + "commentEx",
            new XAttribute(W15 + "paraId", paraId),
            new XAttribute(W15 + "done", "0")));

        ids.Root!.Add(new XElement(
            W16CID + "commentId",
            new XAttribute(W16CID + "paraId", paraId),
            new XAttribute(W16CID + "durableId", durableId)));

        _package.Touch(CommentsPart);
        _package.Touch(CommentsExtendedPart);
        _package.Touch(CommentsIdsPart);
        _added++;

        return commentId;
    }

    /// <summary>Déclare les parties ajoutées dans le package (types, relations, styles).</summary>
    public void Finish()
    {
        if (_added == 0) return;

        DeclareContentType(CommentsPart, "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml");
        DeclareContentType(CommentsExtendedPart, "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml");
        DeclareContentType(CommentsIdsPart, "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsIds+xml");

        DeclareRelationship("http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments", "comments.xml");
        DeclareRelationship("http://schemas.microsoft.com/office/2011/relationships/commentsExtended", "commentsExtended.xml");
        DeclareRelationship("http://schemas.microsoft.com/office/2016/09/relationships/commentsIds", "commentsIds.xml");

        DeclareStyles();
    }

    /// <summary>Marque le début de la plage commentée, à insérer avant le passage visé.</summary>
    public static XElement RangeStart(int commentId) =>
        new(W + "commentRangeStart", new XAttribute(W + "id", commentId));

    /// <summary>Marque la fin de la plage commentée, suivie de l'appel de note.</summary>
    public static IEnumerable<XElement> RangeEnd(int commentId) =>
    [
        new XElement(W + "commentRangeEnd", new XAttribute(W + "id", commentId)),
        new XElement(
            W + "r",
            new XElement(W + "rPr", new XElement(W + "rStyle", new XAttribute(W + "val", ReferenceStyleId))),
            new XElement(W + "commentReference", new XAttribute(W + "id", commentId))),
    ];

    private static XDocument NewPart(XName root, params (string Prefix, XNamespace Namespace)[] extraNamespaces)
    {
        var element = new XElement(
            root,
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "mc", Mc.NamespaceName));

        if (root.Namespace == W15) element.Add(new XAttribute(XNamespace.Xmlns + "w15", W15.NamespaceName));
        if (root.Namespace == W16CID) element.Add(new XAttribute(XNamespace.Xmlns + "w16cid", W16CID.NamespaceName));

        foreach (var (prefix, ns) in extraNamespaces)
        {
            element.Add(new XAttribute(XNamespace.Xmlns + prefix, ns.NamespaceName));
        }

        // mc:Ignorable ne doit citer que des préfixes déclarés ici, et jamais celui de la
        // racine : la partie entière serait ignorée.
        if (extraNamespaces.Length > 0)
        {
            element.Add(new XAttribute(Mc + "Ignorable", string.Join(' ', extraNamespaces.Select(n => n.Prefix))));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), element);
    }

    private void DeclareContentType(string partName, string contentType)
    {
        var types = _package.TryLoad(ContentTypesPart);
        if (types == null) return;

        var path = "/" + partName;
        if (types.Root!.Elements(ContentTypes + "Override").Any(o => o.Attribute("PartName")?.Value == path)) return;

        types.Root.Add(new XElement(
            ContentTypes + "Override",
            new XAttribute("PartName", path),
            new XAttribute("ContentType", contentType)));
        _package.Touch(ContentTypesPart);
    }

    private void DeclareRelationship(string type, string target)
    {
        var rels = _package.TryLoad(DocumentRelsPart);
        if (rels == null) return;

        if (rels.Root!.Elements(PackageRelationships + "Relationship").Any(r => r.Attribute("Type")?.Value == type)) return;

        var nextId = rels.Root.Elements(PackageRelationships + "Relationship")
            .Select(r => Regex.Match(r.Attribute("Id")?.Value ?? "", @"^rId(\d+)$"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max() + 1;

        rels.Root.Add(new XElement(
            PackageRelationships + "Relationship",
            new XAttribute("Id", $"rId{nextId}"),
            new XAttribute("Type", type),
            new XAttribute("Target", target)));
        _package.Touch(DocumentRelsPart);
    }

    private void DeclareStyles()
    {
        var styles = _package.TryLoad(StylesPart);
        if (styles == null) return;

        var known = styles.Root!.Elements(W + "style")
            .Select(s => s.Attribute(W + "styleId")?.Value)
            .Where(id => id != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase!);

        bool added = false;

        if (known.Add(ReferenceStyleId))
        {
            styles.Root.Add(new XElement(
                W + "style",
                new XAttribute(W + "type", "character"),
                new XAttribute(W + "styleId", ReferenceStyleId),
                new XElement(W + "name", new XAttribute(W + "val", "annotation reference")),
                new XElement(W + "uiPriority", new XAttribute(W + "val", "99")),
                new XElement(W + "semiHidden"),
                new XElement(W + "unhideWhenUsed"),
                new XElement(
                    W + "rPr",
                    new XElement(W + "sz", new XAttribute(W + "val", "16")),
                    new XElement(W + "szCs", new XAttribute(W + "val", "16")))));
            added = true;
        }

        if (known.Add(TextStyleId))
        {
            styles.Root.Add(new XElement(
                W + "style",
                new XAttribute(W + "type", "paragraph"),
                new XAttribute(W + "styleId", TextStyleId),
                new XElement(W + "name", new XAttribute(W + "val", "annotation text")),
                new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")),
                new XElement(W + "link", new XAttribute(W + "val", TextCharStyleId)),
                new XElement(W + "uiPriority", new XAttribute(W + "val", "99")),
                new XElement(W + "semiHidden"),
                new XElement(W + "unhideWhenUsed"),
                new XElement(
                    W + "pPr",
                    new XElement(
                        W + "spacing",
                        new XAttribute(W + "line", "240"),
                        new XAttribute(W + "lineRule", "auto"))),
                new XElement(
                    W + "rPr",
                    new XElement(W + "sz", new XAttribute(W + "val", "20")),
                    new XElement(W + "szCs", new XAttribute(W + "val", "20")))));
            added = true;
        }

        if (known.Add(TextCharStyleId))
        {
            styles.Root.Add(new XElement(
                W + "style",
                new XAttribute(W + "type", "character"),
                new XAttribute(W + "customStyle", "1"),
                new XAttribute(W + "styleId", TextCharStyleId),
                new XElement(W + "name", new XAttribute(W + "val", "Commentaire Car")),
                new XElement(W + "link", new XAttribute(W + "val", TextStyleId)),
                new XElement(W + "uiPriority", new XAttribute(W + "val", "99")),
                new XElement(W + "semiHidden"),
                new XElement(
                    W + "rPr",
                    new XElement(W + "sz", new XAttribute(W + "val", "20")),
                    new XElement(W + "szCs", new XAttribute(W + "val", "20")))));
            added = true;
        }

        if (added) _package.Touch(StylesPart);
    }

    /// <summary>Identifiants sur 8 chiffres hexadécimaux, déterministes et sans collision.</summary>
    private static string NextId(ref int seed, int prefix, HashSet<string> used)
    {
        while (true)
        {
            seed++;
            var candidate = (prefix | (seed & 0x0FFFFFFF)).ToString("X8");
            if (used.Add(candidate)) return candidate;
        }
    }

    private static string Initials(string author)
    {
        var letters = author
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => char.IsLetter(word[0]))
            .Select(word => char.ToUpperInvariant(word[0]))
            .Take(3)
            .ToArray();
        return letters.Length > 0 ? new string(letters) : "S";
    }
}
