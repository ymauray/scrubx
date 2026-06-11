using System.IO;
using System.IO.Compression;
using System.Security;
using System.Xml.Linq;
using System.Linq;
using Xunit;
using Scrubx.Cli;

namespace Scrubx.Tests;

public class DocxValidatorTests
{
    private static Stream CreateMockDocx(string textContent, string entryName = "word/document.xml")
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            
            // Add a Titre1 heading to satisfy the StyleTitre1Manquant requirement for general checks
            string titreXml = "";
            if (entryName == "word/document.xml")
            {
                titreXml = """
                    <w:p>
                      <w:pPr>
                        <w:pStyle w:val="Titre1"/>
                      </w:pPr>
                      <w:r>
                        <w:t>Titre du Document</w:t>
                      </w:r>
                    </w:p>
                    """;
            }

            writer.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    {titreXml}
                    <w:p>
                      <w:r>
                        <w:t>{SecurityElement.Escape(textContent)}</w:t>
                      </w:r>
                    </w:p>
                  </w:body>
                </w:document>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Validate_WithOnlyCurvedApostrophes_ReturnsValid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("C’est un bel été. J’aime coder.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_WithStraightApostrophe_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("C'est un bel été. J'aime coder.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Equal(2, report.Errors.Count);
        Assert.Contains("Apostrophe droite", report.Errors[0].Message);
        Assert.Contains("Apostrophe droite", report.Errors[1].Message);
    }

    [Fact]
    public void Validate_WithNoApostrophes_ReturnsValid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("Bonjour tout le monde\u00A0!");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_WithStraightApostropheInFootnotes_ReturnsInvalid()
    {
        // Arrange
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var docEntry = archive.CreateEntry("word/document.xml");
            using (var s = docEntry.Open())
            using (var writer = new StreamWriter(s))
            {
                writer.Write("""
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:body>
                        <w:p><w:pPr><w:pStyle w:val="Titre1"/></w:pPr><w:t>Titre</w:t></w:p>
                        <w:p><w:t>C’est bon.</w:t></w:p>
                      </w:body>
                    </w:document>
                    """);
            }

            var fnEntry = archive.CreateEntry("word/footnotes.xml");
            using (var s = fnEntry.Open())
            using (var writer = new StreamWriter(s))
            {
                writer.Write("""
                    <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:footnote><w:p><w:t>Note : c'est ici.</w:t></w:p></w:footnote>
                    </w:footnotes>
                    """);
            }
        }
        stream.Position = 0;

        // Act
        var report = DocxValidator.Validate(stream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Contains("Apostrophe droite", report.Errors[0].Message);
    }

    [Fact]
    public void Validate_WithValidEmDash_ReturnsValid()
    {
        // Arrange
        // — (em-dash) followed by \u00A0 (non-breaking space)
        using var docxStream = CreateMockDocx("—\u00A0Bonjour, dit-il.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_WithInvalidHyphen_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("- Bonjour, dit-il.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Contains("Tiret de début de ligne invalide", report.Errors[0].Message);
    }

    [Fact]
    public void Validate_WithInvalidEnDash_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("– Bonjour, dit-il.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Contains("Tiret de début de ligne invalide", report.Errors[0].Message);
    }

    [Fact]
    public void Validate_WithLeadingSpacesAndHyphen_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("   - Bonjour, dit-il.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.RuleName == "TiretDebutInvalide");
    }

    [Fact]
    public void Validate_WithDashNotAtStart_ReturnsValid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("Bonjour - dit-il.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_WithEmDashAndNnbs_ReturnsValid()
    {
        // Arrange
        // — (em-dash) followed by \u202F (narrow non-breaking space)
        using var docxStream = CreateMockDocx("—\u202FBonjour, dit-il.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_WithEmDashAndNormalSpace_ReturnsInvalid()
    {
        // Arrange
        // — followed by standard space (\u0020)
        using var docxStream = CreateMockDocx("— Bonjour, dit-il.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Contains("non suivi d'une espace insécable", report.Errors[0].Message);
    }

    [Fact]
    public void Validate_WithEmDashAndNoSpace_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("—Bonjour, dit-il.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Contains("non suivi d'une espace insécable", report.Errors[0].Message);
    }

    [Fact]
    public void Validate_WithEmDashAlone_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("—");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Contains("non suivi d'une espace insécable", report.Errors[0].Message);
    }

    [Fact]
    public void Validate_ContextExtraction_ReturnsCorrectFiveWords()
    {
        // Arrange
        // 6 words before, 6 words after
        using var docxStream = CreateMockDocx("un deux trois quatre cinq six C'est sept huit neuf dix onze douze");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        
        var context = report.Errors[0].Context;
        Assert.Contains("trois quatre cinq six C >>>'<<< est sept huit neuf dix", context);
    }

    [Fact]
    public void Validate_PunctuationWithNbs_ReturnsValid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("Bonjour\u00A0! Quoi\u00A0?");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_PunctuationWithNnbs_ReturnsValid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("Bonjour\u202F! Quoi\u202F?");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_PunctuationWithNormalSpace_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("Bonjour ! Quoi ?");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Equal(2, report.Errors.Count);
        Assert.Contains("espace ordinaire détectée", report.Errors[0].Message);
        Assert.Contains("espace ordinaire détectée", report.Errors[1].Message);
    }

    [Fact]
    public void Validate_PunctuationWithNoSpace_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("Bonjour! Quoi?");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Equal(2, report.Errors.Count);
        Assert.Contains("Espace insécable manquante", report.Errors[0].Message);
        Assert.Contains("Espace insécable manquante", report.Errors[1].Message);
    }

    [Fact]
    public void Validate_MultiplePunctuation_DoesNotFlagConsecutive()
    {
        // Arrange
        using var docxStream = CreateMockDocx("Ah\u00A0!! Quoi\u00A0?!");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_FrenchQuotes_ReturnsValid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("«\u00A0Bonjour\u00A0»");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_StraightDoubleQuotes_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("Il a dit: \"Bonjour\".");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Equal(2, report.Errors.Count);
        Assert.Contains("Guillemet droit", report.Errors[0].Message);
        Assert.Contains("Guillemet droit", report.Errors[1].Message);
    }

    [Fact]
    public void Validate_FrenchQuotesWithNnbs_ReturnsValid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("«\u202FBonjour\u202F»");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_FrenchQuotesWithNormalSpace_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("« Bonjour »");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Equal(2, report.Errors.Count);
        Assert.Contains("espace ordinaire détectée", report.Errors[0].Message);
        Assert.Contains("espace ordinaire détectée", report.Errors[1].Message);
    }

    [Fact]
    public void Validate_FrenchQuotesWithNoSpace_ReturnsInvalid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("«Bonjour»");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Equal(2, report.Errors.Count);
        Assert.Contains("non suivi", report.Errors[0].Message);
        Assert.Contains("non précédé", report.Errors[1].Message);
    }

    [Fact]
    public void Validate_WithValidParagraphStyles_ReturnsValid()
    {
        // Arrange: Normal, Titre1, and Ellipse styles
        var paragraphs = new List<(string text, string? style)>
        {
            ("Titre Principal", "Titre1"),
            ("Un paragraphe normal.", "Normal"),
            ("Un autre paragraphe sans style explicite.", null),
            ("Une ellipse...", "Ellipse")
        };
        using var docxStream = CreateMockDocxWithParagraphs(paragraphs);

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Validate_WithInvalidParagraphStyle_ReturnsInvalid()
    {
        // Arrange: contains "Titre2" which is not allowed
        var paragraphs = new List<(string text, string? style)>
        {
            ("Titre Principal", "Titre1"),
            ("Un sous-titre", "Titre2"),
            ("Un paragraphe normal.", "Normal")
        };
        using var docxStream = CreateMockDocxWithParagraphs(paragraphs);

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Equal("StyleParagrapheInvalide", report.Errors[0].RuleName);
        Assert.Contains("Style de paragraphe non autorisé : 'Titre2'", report.Errors[0].Message);
    }

    [Fact]
    public void Validate_WithoutTitre1Style_ReturnsInvalid()
    {
        // Arrange: only "Normal" style, no "Titre1"
        var paragraphs = new List<(string text, string? style)>
        {
            ("Un paragraphe normal.", "Normal"),
            ("Une ellipse...", "Ellipse")
        };
        using var docxStream = CreateMockDocxWithParagraphs(paragraphs);

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Equal("StyleTitre1Manquant", report.Errors[0].RuleName);
    }

    [Fact]
    public void Validate_StyleChecksDoNotApplyToFootnotesOrEndnotes_ReturnsValid()
    {
        // Arrange: document has Titre1 and Normal, footnotes have CustomStyle
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var docEntry = archive.CreateEntry("word/document.xml");
            using (var s = docEntry.Open())
            using (var writer = new StreamWriter(s))
            {
                writer.Write("""
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:body>
                        <w:p><w:pPr><w:pStyle w:val="Titre1"/></w:pPr><w:t>Titre</w:t></w:p>
                        <w:p><w:t>Bonjour</w:t></w:p>
                      </w:body>
                    </w:document>
                    """);
            }

            var fnEntry = archive.CreateEntry("word/footnotes.xml");
            using (var s = fnEntry.Open())
            using (var writer = new StreamWriter(s))
            {
                writer.Write("""
                    <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:footnote>
                        <w:p>
                          <w:pPr><w:pStyle w:val="Chapeau"/></w:pPr>
                          <w:t>Note : c’est valide car c'est une note.</w:t>
                        </w:p>
                      </w:footnote>
                    </w:footnotes>
                    """);
            }
        }
        stream.Position = 0;

        // Act
        var report = DocxValidator.Validate(stream);

        // Assert
        // We only expect 1 error: the straight apostrophe in the footnote note ("c'est"),
        // but no style errors because style restrictions only apply to word/document.xml!
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Equal("ApostropheDroite", report.Errors[0].RuleName);
    }

    private static Stream CreateMockDocxWithParagraphs(List<(string text, string? style)> paragraphs, string entryName = "word/document.xml")
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            
            var pXmls = new List<string>();
            foreach (var p in paragraphs)
            {
                string stylePr = "";
                if (p.style != null)
                {
                    stylePr = $"""
                      <w:pPr>
                        <w:pStyle w:val="{p.style}"/>
                      </w:pPr>
                      """;
                }
                
                pXmls.Add($"""
                    <w:p>
                      {stylePr}
                      <w:r>
                        <w:t>{SecurityElement.Escape(p.text)}</w:t>
                      </w:r>
                    </w:p>
                    """);
            }

            writer.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    {string.Join("\n", pXmls)}
                  </w:body>
                </w:document>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Validate_WithManualPageBreak_ReturnsInvalid()
    {
        // Arrange: contains a manual page break <w:br w:type="page"/>
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("""
                <?xml version="1.0" encoding="utf-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:pPr><w:pStyle w:val="Titre1"/></w:pPr>
                      <w:r><w:t>Mon Titre</w:t></w:r>
                    </w:p>
                    <w:p>
                      <w:r>
                        <w:t>Texte avant</w:t>
                        <w:br w:type="page"/>
                        <w:t>Texte après</w:t>
                      </w:r>
                    </w:p>
                  </w:body>
                </w:document>
                """);
        }
        stream.Position = 0;

        // Act
        var report = DocxValidator.Validate(stream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Equal("SautDePageDetecte", report.Errors[0].RuleName);
        Assert.Contains("Saut de page manuel détecté", report.Errors[0].Message);
    }

    [Fact]
    public void Validate_WithPageBreakBefore_ReturnsInvalid()
    {
        // Arrange: contains a paragraph with w:pageBreakBefore
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("""
                <?xml version="1.0" encoding="utf-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:pPr><w:pStyle w:val="Titre1"/></w:pPr>
                      <w:r><w:t>Mon Titre</w:t></w:r>
                    </w:p>
                    <w:p>
                      <w:pPr>
                        <w:pageBreakBefore/>
                      </w:pPr>
                      <w:r><w:t>Paragraphe suivant</w:t></w:r>
                    </w:p>
                  </w:body>
                </w:document>
                """);
        }
        stream.Position = 0;

        // Act
        var report = DocxValidator.Validate(stream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Equal("SautDePageDetecte", report.Errors[0].RuleName);
        Assert.Contains("Saut de page avant", report.Errors[0].Message);
    }

    [Fact]
    public void Validate_WithNoTrailingSpaces_ReturnsValid()
    {
        // Arrange
        using var docxStream = CreateMockDocx("C’est parfait.");

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Theory]
    [InlineData("C’est imparfait. ")]       // Standard space
    [InlineData("C’est imparfait.\u00A0")]  // Non-breaking space
    [InlineData("C’est imparfait.\u202F")]  // Narrow non-breaking space
    [InlineData("C’est imparfait.\t")]      // Tabulation
    public void Validate_WithTrailingSpace_ReturnsInvalid(string text)
    {
        // Arrange
        using var docxStream = CreateMockDocx(text);

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Equal("EspaceFinParagraphe", report.Errors[0].RuleName);
        Assert.Contains("Espace en fin de paragraphe détectée", report.Errors[0].Message);
    }

    [Theory]
    [InlineData("Un  deux")]               // two standard spaces
    [InlineData("Un\u00A0\u00A0deux")]       // two non-breaking spaces
    [InlineData("Un\u202F\u202Fdeux")]       // two narrow non-breaking spaces
    [InlineData("Un \u00A0deux")]            // mixed: standard + non-breaking
    [InlineData("Un\u202F\u00A0deux")]       // mixed: narrow + non-breaking
    [InlineData("Un   deux")]              // three standard spaces
    public void Validate_WithConsecutiveSpaces_ReturnsInvalid(string text)
    {
        // Arrange
        using var docxStream = CreateMockDocx(text);

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Equal("DoubleEspace", report.Errors[0].RuleName);
        Assert.Contains("Deux espaces consécutives ou plus détectées", report.Errors[0].Message);
    }

    [Theory]
    [InlineData("a, b et c")]
    [InlineData("un sac, etc.")]
    [InlineData("et c’est tout.")]
    public void Validate_WithNoCommaBeforeEt_ReturnsValid(string text)
    {
        // Arrange
        using var docxStream = CreateMockDocx(text);

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        Assert.True(report.IsValid);
        Assert.DoesNotContain(report.Errors, e => e.RuleName == "VirguleAvantEt");
    }

    [Theory]
    [InlineData("a, b, et c")]
    [InlineData("a, b,et c")]
    [InlineData("a, b,\u00A0et c")]
    [InlineData("a, b,\u202Fet c")]
    [InlineData("a, b, ET c")]
    public void Validate_WithCommaBeforeEt_ReturnsWarningAndIsValid(string text)
    {
        // Arrange
        using var docxStream = CreateMockDocx(text);

        // Act
        var report = DocxValidator.Validate(docxStream);

        // Assert
        // A warning should NOT make the report invalid
        Assert.True(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Equal("VirguleAvantEt", report.Errors[0].RuleName);
        Assert.True(report.Errors[0].IsWarning);
    }
}
