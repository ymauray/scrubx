using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Xunit;
using Scrubx.Cli;

namespace Scrubx.Tests;

public class DocxReviewerTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string CommentsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";

    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    private const string PackageRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    private const string DocumentRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    /// <summary>Construit un .docx minimal mais complet (content-types + relations).</summary>
    private static MemoryStream CreatePackage(
        IEnumerable<string> paragraphs,
        IEnumerable<(string Name, string Xml)>? extraParts = null)
    {
        var parts = new List<(string Name, string Xml)>
        {
            ("[Content_Types].xml", ContentTypesXml),
            ("_rels/.rels", PackageRelsXml),
            ("word/_rels/document.xml.rels", DocumentRelsXml),
            ("word/document.xml", DocumentXml(paragraphs)),
        };
        // Une partie fournie explicitement remplace celle par défaut du même nom
        foreach (var extra in extraParts ?? Enumerable.Empty<(string, string)>())
        {
            int existing = parts.FindIndex(p => p.Name == extra.Name);
            if (existing >= 0) parts[existing] = extra;
            else parts.Add(extra);
        }

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, xml) in parts)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(xml);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string DocumentXml(IEnumerable<string> paragraphs)
    {
        var body = string.Concat(paragraphs.Select(text =>
            $"<w:p><w:r><w:t>{SecurityElement.Escape(text)}</w:t></w:r></w:p>"));
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{body}</w:body></w:document>
            """;
    }

    private static ValidationError Error(
        string ruleName,
        int? paragraphIndex,
        string entryName = "word/document.xml",
        bool isWarning = false,
        int? offset = null,
        int length = 0) => new()
        {
            RuleName = ruleName,
            Message = "Message de test.",
            Context = "... avant >>>x<<< après ...",
            EntryName = entryName,
            ParagraphIndex = paragraphIndex,
            IsWarning = isWarning,
            Offset = offset,
            Length = length,
        };

    /// <summary>Construit un paquet dont le corps de document est fourni tel quel.</summary>
    private static MemoryStream CreatePackageWithBody(string bodyXml)
    {
        var documentXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{bodyXml}</w:body></w:document>
            """;
        return CreatePackage([], [("word/document.xml", documentXml)]);
    }

    /// <summary>Texte réellement encadré par les marques du commentaire donné.</summary>
    private static string AnchoredText(XElement paragraph, int commentId)
    {
        var id = commentId.ToString();
        var anchored = new StringBuilder();
        bool inside = false;

        foreach (var element in paragraph.Descendants())
        {
            if (element.Name == W + "commentRangeStart" && (string?)element.Attribute(W + "id") == id) inside = true;
            else if (element.Name == W + "commentRangeEnd" && (string?)element.Attribute(W + "id") == id) inside = false;
            else if (inside && element.Name == W + "t") anchored.Append(element.Value);
        }

        return anchored.ToString();
    }

    private static string ParagraphText(XElement paragraph) =>
        string.Concat(paragraph.Descendants(W + "t").Select(t => t.Value));

    private static ValidationReport ReportWith(params ValidationError[] errors) =>
        new() { Errors = errors.ToList() };

    private static MemoryStream Review(MemoryStream source, ValidationReport report, out int commentsAdded)
    {
        var output = new MemoryStream();
        commentsAdded = DocxReviewer.Review(source, output, report, new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero));
        output.Position = 0;
        return output;
    }

    private static XDocument ReadPart(MemoryStream package, string partName)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(partName);
        Assert.NotNull(entry);
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private static bool HasPart(MemoryStream package, string partName)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        return archive.GetEntry(partName) != null;
    }

    [Fact]
    public void Review_AddsOneCommentPerError()
    {
        // Arrange
        using var source = CreatePackage(["Premier paragraphe.", "Second paragraphe."]);
        var report = ReportWith(
            Error("ApostropheDroite", 0),
            Error("DoubleEspace", 1),
            Error("VirguleAvantEt", 1, isWarning: true));

        // Act
        using var output = Review(source, report, out int commentsAdded);

        // Assert
        Assert.Equal(3, commentsAdded);
        var comments = ReadPart(output, "word/comments.xml").Root!.Elements(W + "comment").ToList();
        Assert.Equal(3, comments.Count);
        Assert.All(comments, c => Assert.Equal("Scrubx", (string?)c.Attribute(W + "author")));
        Assert.Equal(["1", "2", "3"], comments.Select(c => (string?)c.Attribute(W + "id")));
    }

    [Fact]
    public void Review_CommentBodyCarriesRuleCodeAndContext()
    {
        // Arrange
        using var source = CreatePackage(["Un paragraphe."]);
        var report = ReportWith(Error("ApostropheDroite", 0));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var comment = ReadPart(output, "word/comments.xml").Root!.Element(W + "comment")!;
        var lines = comment.Elements(W + "p").Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value))).ToList();
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("[APOS] ", lines[0]);
        Assert.Contains("Message de test.", lines[0]);
        Assert.StartsWith("Contexte : ", lines[1]);
    }

    [Fact]
    public void Review_WarningCommentIsLabelledAsSuch()
    {
        // Arrange
        using var source = CreatePackage(["Un paragraphe."]);
        var report = ReportWith(Error("VirguleAvantEt", 0, isWarning: true));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var comment = ReadPart(output, "word/comments.xml").Root!.Element(W + "comment")!;
        var headline = string.Concat(comment.Elements(W + "p").First().Descendants(W + "t").Select(t => t.Value));
        Assert.Equal("[VIRGET] Avertissement : Message de test.", headline);
    }

    [Fact]
    public void Review_AnchorsCommentOnTheReportedParagraph()
    {
        // Arrange
        using var source = CreatePackage(["Premier paragraphe.", "Second paragraphe."]);
        var report = ReportWith(Error("ApostropheDroite", 1));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var paragraphs = ReadPart(output, "word/document.xml").Descendants(W + "p").ToList();
        Assert.Empty(paragraphs[0].Elements(W + "commentRangeStart"));
        Assert.Single(paragraphs[1].Elements(W + "commentRangeStart"));
        Assert.Single(paragraphs[1].Elements(W + "commentRangeEnd"));
        Assert.Single(paragraphs[1].Descendants(W + "commentReference"));
    }

    [Fact]
    public void Review_AnchorsAllErrorsOfTheSameParagraph()
    {
        // Arrange
        using var source = CreatePackage(["Un seul paragraphe."]);
        var report = ReportWith(Error("ApostropheDroite", 0), Error("DoubleEspace", 0));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Single();
        Assert.Equal(2, paragraph.Elements(W + "commentRangeStart").Count());
        Assert.Equal(2, paragraph.Elements(W + "commentRangeEnd").Count());
    }

    [Fact]
    public void Review_DocumentWideError_IsAnchoredOnFirstParagraph()
    {
        // Arrange
        using var source = CreatePackage(["Premier paragraphe.", "Second paragraphe."]);
        var report = ReportWith(Error("StyleTitre1Manquant", null));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var paragraphs = ReadPart(output, "word/document.xml").Descendants(W + "p").ToList();
        Assert.Single(paragraphs[0].Elements(W + "commentRangeStart"));
        Assert.Empty(paragraphs[1].Elements(W + "commentRangeStart"));
    }

    [Fact]
    public void Review_KeepsParagraphPropertiesFirst()
    {
        // Arrange — a paragraph whose w:pPr must remain the first child
        var documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:pPr><w:pStyle w:val="Titre1"/></w:pPr><w:r><w:t>Titre</w:t></w:r></w:p></w:body></w:document>
            """;
        using var source = CreatePackage([], [("word/document.xml", documentXml)]);
        var report = ReportWith(Error("ApostropheDroite", 0));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Single();
        Assert.Equal(W + "pPr", paragraph.Elements().First().Name);
        Assert.Equal(W + "commentRangeStart", paragraph.Elements().Skip(1).First().Name);
    }

    [Fact]
    public void Review_DoesNotAlterDocumentText()
    {
        // Arrange
        var paragraphs = new[] { "C'est un bel été.", "Deux  espaces et « une citation »." };
        using var source = CreatePackage(paragraphs);
        var report = ReportWith(Error("ApostropheDroite", 0), Error("DoubleEspace", 1));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var text = ReadPart(output, "word/document.xml")
            .Descendants(W + "p")
            .Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)));
        Assert.Equal(paragraphs, text);
    }

    [Fact]
    public void Review_DeclaresCommentsPartAndRelationship()
    {
        // Arrange
        using var source = CreatePackage(["Un paragraphe."]);
        var report = ReportWith(Error("ApostropheDroite", 0));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var overrides = ReadPart(output, "[Content_Types].xml").Root!.Elements(ContentTypesNs + "Override");
        Assert.Contains(overrides, o => (string?)o.Attribute("PartName") == "/word/comments.xml");

        var relationships = ReadPart(output, "word/_rels/document.xml.rels").Root!.Elements(RelationshipsNs + "Relationship").ToList();
        var commentsRel = Assert.Single(relationships, r => (string?)r.Attribute("Type") == CommentsRelationshipType);
        Assert.Equal("comments.xml", (string?)commentsRel.Attribute("Target"));
        Assert.Equal("rId2", (string?)commentsRel.Attribute("Id"));
    }

    [Fact]
    public void Review_WithoutErrors_LeavesPackageUntouched()
    {
        // Arrange
        using var source = CreatePackage(["Un paragraphe."]);

        // Act
        using var output = Review(source, new ValidationReport(), out int commentsAdded);

        // Assert
        Assert.Equal(0, commentsAdded);
        Assert.False(HasPart(output, "word/comments.xml"));
        var relationships = ReadPart(output, "word/_rels/document.xml.rels").Root!.Elements(RelationshipsNs + "Relationship");
        Assert.DoesNotContain(relationships, r => (string?)r.Attribute("Type") == CommentsRelationshipType);
    }

    [Fact]
    public void Review_IgnoresErrorsWithoutLocation()
    {
        // Arrange — read errors carry no location and cannot be anchored
        using var source = CreatePackage(["Un paragraphe."]);
        var report = ReportWith(new ValidationError { RuleName = "LectureDocument", Message = "Erreur." });

        // Act
        using var output = Review(source, report, out int commentsAdded);

        // Assert
        Assert.Equal(0, commentsAdded);
        Assert.False(HasPart(output, "word/comments.xml"));
    }

    [Fact]
    public void Review_PreservesExistingComments()
    {
        // Arrange — a document already reviewed by a human
        const string existingComments = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:comments xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:comment w:id="7" w:author="Relecteur" w:date="2026-08-01T09:00:00Z"><w:p><w:r><w:t>À revoir</w:t></w:r></w:p></w:comment></w:comments>
            """;
        using var source = CreatePackage(["Un paragraphe."], [("word/comments.xml", existingComments)]);
        var report = ReportWith(Error("ApostropheDroite", 0));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var comments = ReadPart(output, "word/comments.xml").Root!.Elements(W + "comment").ToList();
        Assert.Equal(2, comments.Count);
        Assert.Contains(comments, c => (string?)c.Attribute(W + "author") == "Relecteur");
        // Les identifiants repartent au-delà de ceux déjà utilisés
        var added = Assert.Single(comments, c => (string?)c.Attribute(W + "author") == "Scrubx");
        Assert.Equal("8", (string?)added.Attribute(W + "id"));
    }

    [Fact]
    public void Review_AnchorsErrorsInFootnotesPart()
    {
        // Arrange
        const string footnotesXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:footnote w:id="1"><w:p><w:r><w:t>Texte de note.</w:t></w:r></w:p></w:footnote></w:footnotes>
            """;
        using var source = CreatePackage(["Un paragraphe."], [("word/footnotes.xml", footnotesXml)]);
        var report = ReportWith(Error("ApostropheDroite", 0, entryName: "word/footnotes.xml"));

        // Act
        using var output = Review(source, report, out int commentsAdded);

        // Assert
        Assert.Equal(1, commentsAdded);
        var paragraph = ReadPart(output, "word/footnotes.xml").Descendants(W + "p").Single();
        Assert.Single(paragraph.Elements(W + "commentRangeStart"));
        // La partie notes doit elle aussi pouvoir atteindre word/comments.xml
        var relationships = ReadPart(output, "word/_rels/footnotes.xml.rels").Root!.Elements(RelationshipsNs + "Relationship");
        Assert.Contains(relationships, r => (string?)r.Attribute("Type") == CommentsRelationshipType);
        // Le corps du document, lui, n'est pas touché
        Assert.Empty(ReadPart(output, "word/document.xml").Descendants(W + "commentRangeStart"));
    }

    [Fact]
    public void Review_CopiesUnrelatedPartsVerbatim()
    {
        // Arrange
        const string stylesXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:styleId="Titre1"/></w:styles>
            """;
        using var source = CreatePackage(["Un paragraphe."], [("word/styles.xml", stylesXml)]);
        var report = ReportWith(Error("ApostropheDroite", 0));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        Assert.True(HasPart(output, "word/styles.xml"));
        Assert.True(HasPart(output, "_rels/.rels"));
        Assert.Single(ReadPart(output, "word/styles.xml").Descendants(W + "style"));
    }

    [Fact]
    public void Review_WithoutContentTypes_Throws()
    {
        // Arrange — not a real .docx
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write(DocumentXml(["Un paragraphe."]));
        }
        stream.Position = 0;
        var report = ReportWith(Error("ApostropheDroite", 0));

        // Act & Assert
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => DocxReviewer.Review(stream, output, report));
    }

    [Fact]
    public void Review_RefusesToOverwriteTheSourceFile()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"scrubx-{Guid.NewGuid():N}.docx");
        using (var source = CreatePackage(["Un paragraphe."]))
        using (var file = File.Create(path))
        {
            source.CopyTo(file);
        }

        try
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => DocxReviewer.Review(path, path, new ValidationReport()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Review_AnchorsExactlyTheReportedCharacters()
    {
        // Arrange — l'apostrophe droite est au milieu d'un run
        using var source = CreatePackageWithBody("<w:p><w:r><w:t>C'est un bel été.</w:t></w:r></w:p>");
        var report = ReportWith(Error("ApostropheDroite", 0, offset: 1, length: 1));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Single();
        Assert.Equal("'", AnchoredText(paragraph, 1));
    }

    [Fact]
    public void Review_SplittingRunsLeavesTheTextUnchanged()
    {
        // Arrange
        using var source = CreatePackageWithBody("<w:p><w:r><w:t>C'est un bel été.</w:t></w:r></w:p>");
        var report = ReportWith(Error("ApostropheDroite", 0, offset: 1, length: 1));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Single();
        Assert.Equal("C'est un bel été.", ParagraphText(paragraph));
        Assert.Equal(3, paragraph.Elements(W + "r").Count(r => r.Element(W + "t") != null));
    }

    [Fact]
    public void Review_SplittingRunsCopiesTheirFormatting()
    {
        // Arrange — tout le paragraphe est en gras
        using var source = CreatePackageWithBody(
            "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>C'est un bel été.</w:t></w:r></w:p>");
        var report = ReportWith(Error("ApostropheDroite", 0, offset: 1, length: 1));

        // Act
        using var output = Review(source, report, out _);

        // Assert — les trois morceaux restent en gras, le rendu est inchangé
        var runs = ReadPart(output, "word/document.xml").Descendants(W + "p").Single()
            .Elements(W + "r").Where(r => r.Element(W + "t") != null).ToList();
        Assert.Equal(3, runs.Count);
        Assert.All(runs, r => Assert.NotNull(r.Element(W + "rPr")?.Element(W + "b")));
    }

    [Fact]
    public void Review_SplittingRunsMarksSpacesAsPreserved()
    {
        // Arrange — la moitié des règles portent sur des espaces
        using var source = CreatePackageWithBody("<w:p><w:r><w:t xml:space=\"preserve\">Deux  espaces</w:t></w:r></w:p>");
        var report = ReportWith(Error("DoubleEspace", 0, offset: 4, length: 2));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Single();
        Assert.Equal("  ", AnchoredText(paragraph, 1));
        Assert.Equal("Deux  espaces", ParagraphText(paragraph));
        Assert.All(paragraph.Descendants(W + "t"),
            t => Assert.Equal("preserve", (string?)t.Attribute(XNamespace.Xml + "space")));
    }

    [Fact]
    public void Review_AnchorsRangeSpanningSeveralRuns()
    {
        // Arrange — Word a coupé le paragraphe entre les deux espaces
        using var source = CreatePackageWithBody(
            "<w:p><w:r><w:t xml:space=\"preserve\">Deux </w:t></w:r><w:r><w:t xml:space=\"preserve\"> espaces</w:t></w:r></w:p>");
        var report = ReportWith(Error("DoubleEspace", 0, offset: 4, length: 2));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Single();
        Assert.Equal("  ", AnchoredText(paragraph, 1));
        Assert.Equal("Deux  espaces", ParagraphText(paragraph));
    }

    [Fact]
    public void Review_AnchorsSeveralRangesInTheSameParagraph()
    {
        // Arrange
        using var source = CreatePackageWithBody("<w:p><w:r><w:t>C'est l'été.</w:t></w:r></w:p>");
        var report = ReportWith(
            Error("ApostropheDroite", 0, offset: 1, length: 1),
            Error("ApostropheDroite", 0, offset: 7, length: 1));

        // Act
        using var output = Review(source, report, out _);

        // Assert — le découpage du premier ne fausse pas les offsets du second
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Single();
        Assert.Equal("'", AnchoredText(paragraph, 1));
        Assert.Equal("'", AnchoredText(paragraph, 2));
        Assert.Equal("C'est l'été.", ParagraphText(paragraph));
    }

    [Fact]
    public void Review_SplittingKeepsRunContentThatFollowsTheText()
    {
        // Arrange — un run peut porter autre chose qu'un w:t
        using var source = CreatePackageWithBody(
            "<w:p><w:r><w:t>C'est</w:t><w:tab/></w:r><w:r><w:t> la fin.</w:t></w:r></w:p>");
        var report = ReportWith(Error("ApostropheDroite", 0, offset: 1, length: 1));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Single();
        Assert.Equal("'", AnchoredText(paragraph, 1));
        Assert.Single(paragraph.Descendants(W + "tab"));
        Assert.Equal("C'est la fin.", ParagraphText(paragraph));
    }

    [Fact]
    public void Review_ErrorWithoutOffset_AnchorsWholeParagraph()
    {
        // Arrange — une règle qui porte sur le paragraphe, pas sur des caractères
        using var source = CreatePackageWithBody("<w:p><w:r><w:t>Un paragraphe entier.</w:t></w:r></w:p>");
        var report = ReportWith(Error("StyleParagrapheInvalide", 0));

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Single();
        Assert.Equal("Un paragraphe entier.", AnchoredText(paragraph, 1));
        Assert.Single(paragraph.Elements(W + "r"), r => r.Element(W + "t") != null);
    }

    [Theory]
    [InlineData(100, 1)]  // au-delà de la fin du texte
    [InlineData(0, 500)]  // plage plus longue que le texte
    public void Review_UnresolvableRange_FallsBackToWholeParagraph(int offset, int length)
    {
        // Arrange
        using var source = CreatePackageWithBody("<w:p><w:r><w:t>Un paragraphe entier.</w:t></w:r></w:p>");
        var report = ReportWith(Error("ApostropheDroite", 0, offset: offset, length: length));

        // Act
        using var output = Review(source, report, out int commentsAdded);

        // Assert — on annote quand même, sur le paragraphe entier
        Assert.Equal(1, commentsAdded);
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Single();
        Assert.Equal("Un paragraphe entier.", AnchoredText(paragraph, 1));
    }

    [Fact]
    public void Review_EndToEnd_AnchorsOnTheCharactersTheValidatorFound()
    {
        // Arrange
        using var source = CreatePackageWithBody(
            "<w:p><w:pPr><w:pStyle w:val=\"Titre1\"/></w:pPr><w:r><w:t>Titre</w:t></w:r></w:p>"
            + "<w:p><w:r><w:t>C'est un bel été.</w:t></w:r></w:p>");
        var report = DocxValidator.Validate(source);
        source.Position = 0;

        // Act
        using var output = Review(source, report, out _);

        // Assert
        var apostrophe = Assert.Single(report.Errors);
        Assert.Equal("ApostropheDroite", apostrophe.RuleName);
        var paragraph = ReadPart(output, "word/document.xml").Descendants(W + "p").Last();
        Assert.Equal("'", AnchoredText(paragraph, 1));
    }

    [Fact]
    public void Review_EndToEnd_AnnotatesWhatTheValidatorReported()
    {
        // Arrange
        using var source = CreatePackage(["Titre du document", "C'est un bel été."]);
        var report = DocxValidator.Validate(source);
        source.Position = 0;

        // Act
        using var output = Review(source, report, out int commentsAdded);

        // Assert — apostrophe droite + Titre1 manquant
        Assert.Equal(report.Errors.Count, commentsAdded);
        var paragraphs = ReadPart(output, "word/document.xml").Descendants(W + "p").ToList();
        Assert.Single(paragraphs[1].Elements(W + "commentRangeStart"));   // l'apostrophe
        Assert.Single(paragraphs[0].Elements(W + "commentRangeStart"));   // Titre1 manquant, en tête
    }
}
