using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Scrubx.Cli;
using Xunit;

namespace Scrubx.Tests;

public class DocxRevisionWriterTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>Construit un .docx minimal dont le corps est le XML donné.</summary>
    private static MemoryStream CreateDocx(string bodyXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write($"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:pPr><w:pStyle w:val="Titre1"/></w:pPr><w:r><w:t>Titre</w:t></w:r></w:p>{bodyXml}</w:body></w:document>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static string Run(string text) =>
        $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    /// <summary>Un paragraphe par erreur de l'exemple de référence, texte éclaté en plusieurs runs.</summary>
    private const string SampleBody =
        "<w:p>" + "<w:r><w:t xml:space=\"preserve\">Elle a encore plus peur qu’elle.</w:t></w:r><w:r><w:t xml:space=\"preserve\"> </w:t></w:r>" + "</w:p>" +
        "<w:p>" + "<w:r><w:t xml:space=\"preserve\">- </w:t></w:r><w:r><w:t>J’ai pourtant essayé.</w:t></w:r>" + "</w:p>" +
        "<w:p>" + "<w:r><w:t xml:space=\"preserve\">les dessins que la </w:t></w:r><w:r><w:t>\"</w:t></w:r><w:r><w:t>marée</w:t></w:r><w:r><w:t>\"</w:t></w:r><w:r><w:t xml:space=\"preserve\"> emportait.</w:t></w:r>" + "</w:p>";

    private static MemoryStream WriteRevisions(Stream source, out RevisionResult result, RevisionOptions? options = null)
    {
        var report = DocxValidator.Validate(source, null);
        source.Position = 0;
        var destination = new MemoryStream();
        result = DocxRevisionWriter.Write(source, destination, report, options);
        destination.Position = 0;
        return destination;
    }

    private static XDocument ReadDocumentPart(Stream docx)
    {
        docx.Position = 0;
        using var archive = new ZipArchive(docx, ZipArchiveMode.Read, leaveOpen: true);
        using var entry = archive.GetEntry("word/document.xml")!.Open();
        var document = XDocument.Load(entry, LoadOptions.PreserveWhitespace);
        docx.Position = 0;
        return document;
    }

    /// <summary>Rejoue « Accepter toutes les révisions » (ou « Refuser toutes ») sur une copie annotée.</summary>
    private static MemoryStream Resolve(Stream docx, bool accept)
    {
        var document = ReadDocumentPart(docx);

        foreach (var insertion in document.Descendants(W + "ins").ToList())
        {
            if (accept) insertion.ReplaceWith(insertion.Elements());
            else insertion.Remove();
        }

        foreach (var deletion in document.Descendants(W + "del").ToList())
        {
            if (accept)
            {
                deletion.Remove();
                continue;
            }

            foreach (var deletedText in deletion.Descendants(W + "delText").ToList())
            {
                deletedText.ReplaceWith(new XElement(W + "t", deletedText.Attributes(), deletedText.Value));
            }
            deletion.ReplaceWith(deletion.Elements());
        }

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            document.Save(entryStream, SaveOptions.DisableFormatting);
        }
        stream.Position = 0;
        return stream;
    }

    private static string[] ParagraphTexts(Stream docx) =>
        ReadDocumentPart(docx)
            .Descendants(W + "p")
            .Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)))
            .ToArray();

    [Fact]
    public void Write_ProducesOneRevisionPerFixableError()
    {
        using var source = CreateDocx(SampleBody);

        using var revised = WriteRevisions(source, out var result);

        // EFINPAR, TIRET et deux GDROIT.
        Assert.Equal(4, result.RevisionCount);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public void Write_AcceptingAllRevisions_YieldsAValidDocument()
    {
        using var source = CreateDocx(SampleBody);
        using var revised = WriteRevisions(source, out _);

        using var accepted = Resolve(revised, accept: true);
        var report = DocxValidator.Validate(accepted, null);

        Assert.DoesNotContain(report.Errors, e => !e.IsWarning);
    }

    [Fact]
    public void Write_AcceptingAllRevisions_AppliesTheExpectedCorrections()
    {
        using var source = CreateDocx(SampleBody);
        using var revised = WriteRevisions(source, out _);

        using var accepted = Resolve(revised, accept: true);
        var texts = ParagraphTexts(accepted);

        Assert.Equal("Elle a encore plus peur qu’elle.", texts[1]);
        Assert.Equal("— J’ai pourtant essayé.", texts[2]);
        Assert.Equal("les dessins que la « marée » emportait.", texts[3]);
    }

    [Fact]
    public void Write_RejectingAllRevisions_RestoresTheOriginalText()
    {
        using var source = CreateDocx(SampleBody);
        var original = ParagraphTexts(source);
        using var revised = WriteRevisions(source, out _);

        using var rejected = Resolve(revised, accept: false);

        Assert.Equal(original, ParagraphTexts(rejected));
    }

    [Fact]
    public void Write_MarksRevisionsWithTheGivenAuthorAndDate()
    {
        using var source = CreateDocx(SampleBody);
        var options = new RevisionOptions
        {
            Author = "Relecteur",
            Date = new DateTimeOffset(2026, 5, 21, 12, 0, 0, TimeSpan.FromHours(2)),
        };

        using var revised = WriteRevisions(source, out _, options);
        var document = ReadDocumentPart(revised);

        var revisions = document.Descendants().Where(e => e.Name == W + "ins" || e.Name == W + "del").ToList();
        Assert.NotEmpty(revisions);
        Assert.All(revisions, r => Assert.Equal("Relecteur", r.Attribute(W + "author")!.Value));
        Assert.All(revisions, r => Assert.Equal("2026-05-21T10:00:00Z", r.Attribute(W + "date")!.Value));
        Assert.Equal(revisions.Count, revisions.Select(r => r.Attribute(W + "id")!.Value).Distinct().Count());
    }

    [Fact]
    public void Write_UsesDeletedTextMarkupInsideDeletions()
    {
        using var source = CreateDocx(SampleBody);

        using var revised = WriteRevisions(source, out _);
        var document = ReadDocumentPart(revised);

        Assert.All(
            document.Descendants(W + "del"),
            del =>
            {
                Assert.Empty(del.Descendants(W + "t"));
                Assert.NotEmpty(del.Descendants(W + "delText"));
            });
    }

    [Fact]
    public void Write_KeepsUntouchedPartsByteForByte()
    {
        using var source = CreateDocx(SampleBody);
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/fontTable.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("<fonts/>");
        }
        source.Position = 0;

        using var revised = WriteRevisions(source, out _);

        using var archive2 = new ZipArchive(revised, ZipArchiveMode.Read, leaveOpen: true);
        using var fonts = new StreamReader(archive2.GetEntry("word/fontTable.xml")!.Open());
        Assert.Equal("<fonts/>", fonts.ReadToEnd());
    }

    [Fact]
    public void Write_OnADocumentThatAlreadyHasRevisions_Throws()
    {
        using var source = CreateDocx(
            "<w:p><w:ins w:id=\"1\" w:author=\"X\" w:date=\"2026-01-01T00:00:00Z\"><w:r><w:t>Bonjour  </w:t></w:r></w:ins></w:p>");
        var report = DocxValidator.Validate(source, null);
        source.Position = 0;

        using var destination = new MemoryStream();
        var exception = Assert.Throws<DocxRevisionException>(
            () => DocxRevisionWriter.Write(source, destination, report));
        Assert.Contains("révisions suivies", exception.Message);
    }

    [Fact]
    public void Write_SkipsOverlappingFixes()
    {
        // Deux espaces en fin de paragraphe : DESPACE et EFINPAR visent la même plage.
        using var source = CreateDocx("<w:p>" + Run("Bonjour  ") + "</w:p>");

        using var revised = WriteRevisions(source, out var result);

        Assert.Equal(1, result.RevisionCount);
        Assert.Equal(1, result.SkippedCount);

        using var accepted = Resolve(revised, accept: true);
        Assert.Equal("Bonjour", ParagraphTexts(accepted)[1]);
    }

    [Fact]
    public void Write_ForAPureInsertion_AddsAnInsertionWithoutDeletion()
    {
        // EIPONC : espace insécable manquante avant « ! ».
        using var source = CreateDocx("<w:p>" + Run("Quelle chance!") + "</w:p>");

        using var revised = WriteRevisions(source, out var result);
        var document = ReadDocumentPart(revised);

        Assert.Equal(1, result.RevisionCount);
        Assert.Single(document.Descendants(W + "ins"));
        Assert.Empty(document.Descendants(W + "del"));

        using var accepted = Resolve(revised, accept: true);
        Assert.Equal("Quelle chance\u00A0!", ParagraphTexts(accepted)[1]);
    }

    [Fact]
    public void Write_KeepsTheFormattingOfTheCorrectedRun()
    {
        using var source = CreateDocx(
            "<w:p><w:r><w:rPr><w:i/></w:rPr><w:t xml:space=\"preserve\">Quelle chance!</w:t></w:r></w:p>");

        using var revised = WriteRevisions(source, out _);
        var insertion = ReadDocumentPart(revised).Descendants(W + "ins").Single();

        Assert.NotNull(insertion.Element(W + "r")!.Element(W + "rPr")!.Element(W + "i"));
    }

    [Fact]
    public void Write_CorrectsSeveralErrorsInTheSameParagraph()
    {
        using var source = CreateDocx("<w:p>" + Run("- L'été \"chaud\"  arrive!") + "</w:p>");

        using var revised = WriteRevisions(source, out var result);

        Assert.True(result.RevisionCount >= 5);

        using var accepted = Resolve(revised, accept: true);
        var report = DocxValidator.Validate(accepted, null);
        Assert.DoesNotContain(report.Errors, e => !e.IsWarning);

        using var rejected = Resolve(revised, accept: false);
        Assert.Equal(ParagraphTexts(source), ParagraphTexts(rejected));
    }

    [Fact]
    public void Write_CorrectsBothSidesOfADoublePunctuationMark()
    {
        using var source = CreateDocx("<w:p>" + Run("Bonjour!Comment vas-tu?") + "</w:p>");

        using var revised = WriteRevisions(source, out _);

        using var accepted = Resolve(revised, accept: true);
        Assert.Equal("Bonjour\u00A0! Comment vas-tu\u00A0?", ParagraphTexts(accepted)[1]);
        Assert.DoesNotContain(DocxValidator.Validate(accepted, null).Errors, e => !e.IsWarning);

        using var rejected = Resolve(revised, accept: false);
        Assert.Equal(ParagraphTexts(source), ParagraphTexts(rejected));
    }
}
