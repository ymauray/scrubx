using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Scrubx.Cli;

/// <summary>Levée quand une copie annotée ne peut pas être produite.</summary>
public class DocxRevisionException : Exception
{
    public DocxRevisionException(string message) : base(message) { }
}

public sealed class RevisionOptions
{
    /// <summary>Auteur affiché par Word pour les révisions et les commentaires.</summary>
    public string Author { get; set; } = "Scrubx";

    public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;
}

public sealed record RevisionResult(int RevisionCount, int SkippedCount);

/// <summary>
/// Produit une copie d'un .docx dans laquelle les corrections proposées par
/// <see cref="DocxValidator"/> sont inscrites en révisions suivies (w:ins / w:del).
/// </summary>
public static class DocxRevisionWriter
{
    private const string DocumentPart = "word/document.xml";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static RevisionResult Write(string sourcePath, string destinationPath, ValidationReport report, RevisionOptions? options = null)
    {
        using var source = File.OpenRead(sourcePath);
        using var destination = File.Create(destinationPath);
        return Write(source, destination, report, options);
    }

    public static RevisionResult Write(Stream source, Stream destination, ValidationReport report, RevisionOptions? options = null)
    {
        options ??= new RevisionOptions();

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(DocumentPart)
            ?? throw new DocxRevisionException($"Partie '{DocumentPart}' introuvable : ce fichier n'est pas un document Word valide.");

        XDocument document;
        using (var entryStream = entry.Open())
        {
            document = XDocument.Load(entryStream, LoadOptions.PreserveWhitespace);
        }

        GuardAgainstExistingRevisions(document);

        var result = ApplyFixes(document, report, options);

        CopyArchive(archive, destination, new Dictionary<string, XDocument> { [DocumentPart] = document });

        return result;
    }

    private static void GuardAgainstExistingRevisions(XDocument document)
    {
        var revisionMarks = new[] { "ins", "del", "moveFrom", "moveTo" };
        if (document.Descendants().Any(e => e.Name.Namespace == W && revisionMarks.Contains(e.Name.LocalName)))
        {
            throw new DocxRevisionException(
                "Le document contient déjà des révisions suivies. Acceptez ou refusez-les avant de générer une copie annotée.");
        }
    }

    private static RevisionResult ApplyFixes(XDocument document, ValidationReport report, RevisionOptions options)
    {
        var paragraphs = document.Descendants(W + "p").ToList();
        var date = options.Date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'");

        int revisionId = 1;
        int applied = 0;
        int skipped = 0;

        var byParagraph = report.Errors
            .Where(e => e.Fix != null && e.PartName == DocumentPart && e.ParagraphIndex >= 0)
            .GroupBy(e => e.ParagraphIndex)
            .OrderBy(g => g.Key);

        foreach (var group in byParagraph)
        {
            if (group.Key >= paragraphs.Count)
            {
                skipped += group.Count();
                continue;
            }
            var paragraph = paragraphs[group.Key];

            // De droite à gauche : les corrections déjà appliquées ne décalent pas
            // les offsets de celles qui restent à traiter.
            int limit = int.MaxValue;
            foreach (var error in group.OrderByDescending(e => e.Fix!.Start).ThenByDescending(e => e.Fix!.Length))
            {
                var fix = error.Fix!;
                if (fix.Start + fix.Length > limit)
                {
                    // Chevauchement avec une correction déjà écrite (par exemple EFINPAR et
                    // DESPACE sur la même suite d'espaces) : on n'en garde qu'une.
                    skipped++;
                    continue;
                }

                try
                {
                    ApplyFix(paragraph, fix, options.Author, date, ref revisionId);
                    limit = fix.Start;
                    applied++;
                }
                catch (ParagraphStructureException)
                {
                    skipped++;
                }
            }
        }

        return new RevisionResult(applied, skipped);
    }

    private static void ApplyFix(XElement paragraph, TextEdit fix, string author, string date, ref int revisionId)
    {
        var map = new ParagraphTextMap(paragraph);
        if (fix.Start < 0 || fix.Start + fix.Length > map.Text.Length)
        {
            throw new ParagraphStructureException("Correction hors des limites du paragraphe.");
        }

        XElement? deletion = null;
        if (fix.Length > 0)
        {
            var runs = map.SplitRange(fix.Start, fix.Length);
            if (runs.Count == 0) throw new ParagraphStructureException("Plage de correction sans run correspondant.");

            deletion = new XElement(W + "del", RevisionAttributes(author, date, ref revisionId));
            runs[0].AddBeforeSelf(deletion);
            foreach (var run in runs)
            {
                run.Remove();
                deletion.Add(run);
                ConvertTextToDeletedText(run);
            }
        }

        if (fix.Replacement.Length == 0) return;

        var model = deletion?.Descendants(W + "r").FirstOrDefault();
        var insertedRun = new XElement(W + "r");
        var rPr = model?.Element(W + "rPr");
        if (rPr != null) insertedRun.Add(new XElement(rPr));
        insertedRun.Add(ParagraphTextMap.MakeText(fix.Replacement));

        var insertion = new XElement(W + "ins", RevisionAttributes(author, date, ref revisionId), insertedRun);

        if (deletion != null)
        {
            deletion.AddAfterSelf(insertion);
            return;
        }

        // Insertion pure : il faut d'abord ouvrir une frontière de run à l'offset visé.
        var anchor = new ParagraphTextMap(paragraph).SplitForInsertion(fix.Start);
        if (anchor != null)
        {
            anchor.AddBeforeSelf(insertion);
        }
        else
        {
            paragraph.Add(insertion);
        }
    }

    private static object[] RevisionAttributes(string author, string date, ref int revisionId) =>
    [
        new XAttribute(W + "id", revisionId++),
        new XAttribute(W + "author", author),
        new XAttribute(W + "date", date),
    ];

    private static void ConvertTextToDeletedText(XElement run)
    {
        foreach (var text in run.Descendants(W + "t").ToList())
        {
            text.ReplaceWith(new XElement(W + "delText", text.Attributes(), text.Value));
        }
    }

    /// <summary>
    /// Recopie l'archive source en remplaçant les parties données, à l'octet près pour les autres.
    /// </summary>
    internal static void CopyArchive(
        ZipArchive source,
        Stream destination,
        IReadOnlyDictionary<string, XDocument> replacements,
        IReadOnlyList<(string Name, XDocument Document)>? additions = null)
    {
        using var output = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var entry in source.Entries)
        {
            var created = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            using var target = created.Open();

            if (replacements.TryGetValue(entry.FullName, out var replacement))
            {
                Save(replacement, target);
            }
            else
            {
                using var original = entry.Open();
                original.CopyTo(target);
            }
        }

        foreach (var (name, document) in additions ?? [])
        {
            var created = output.CreateEntry(name, CompressionLevel.Optimal);
            using var target = created.Open();
            Save(document, target);
        }
    }

    private static void Save(XDocument document, Stream target)
    {
        // DisableFormatting : la moindre indentation ajoutée modifierait le texte du document.
        document.Save(target, SaveOptions.DisableFormatting);
    }
}
