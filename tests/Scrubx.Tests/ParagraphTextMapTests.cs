using System.Linq;
using System.Xml.Linq;
using Xunit;
using Scrubx.Cli;

namespace Scrubx.Tests;

public class ParagraphTextMapTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static XElement Paragraph(string innerXml) =>
        XElement.Parse($"""
            <w:p xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">{innerXml}</w:p>
            """);

    [Fact]
    public void Build_ConcatenatesTextInDocumentOrder()
    {
        // Arrange — Word découpe librement un paragraphe en plusieurs runs
        var paragraph = Paragraph("<w:r><w:t>Bon</w:t></w:r><w:r><w:t>jour</w:t></w:r>");

        // Act
        var map = ParagraphTextMap.Build(paragraph);

        // Assert
        Assert.Equal("Bonjour", map.Text);
    }

    [Fact]
    public void Build_RecordsOffsetOfEachTextNode()
    {
        // Arrange
        var paragraph = Paragraph("<w:r><w:t>Bon</w:t></w:r><w:r><w:t>jour</w:t></w:r>");

        // Act
        var segments = ParagraphTextMap.Build(paragraph).Segments;

        // Assert
        Assert.Equal(2, segments.Count);
        Assert.Equal((0, 3, 3), (segments[0].Start, segments[0].Length, segments[0].End));
        Assert.Equal((3, 4, 7), (segments[1].Start, segments[1].Length, segments[1].End));
        Assert.Equal("jour", segments[1].Text.Value);
    }

    [Fact]
    public void Build_SkipsEmptyTextNodes()
    {
        // Arrange
        var paragraph = Paragraph("<w:r><w:t></w:t></w:r><w:r><w:t>Bonjour</w:t></w:r>");

        // Act
        var map = ParagraphTextMap.Build(paragraph);

        // Assert
        Assert.Equal("Bonjour", map.Text);
        Assert.Single(map.Segments);
        Assert.Equal(0, map.Segments[0].Start);
    }

    [Fact]
    public void Build_IncludesTextCarriedByOtherContainers()
    {
        // Arrange — un run peut être imbriqué dans un lien hypertexte
        var paragraph = Paragraph("<w:r><w:t>Voir </w:t></w:r><w:hyperlink><w:r><w:t>ici</w:t></w:r></w:hyperlink>");

        // Act
        var map = ParagraphTextMap.Build(paragraph);

        // Assert
        Assert.Equal("Voir ici", map.Text);
        Assert.Equal(5, map.Segments[1].Start);
    }

    [Theory]
    [InlineData(0, "Bon")]
    [InlineData(2, "Bon")]
    [InlineData(3, "jour")]
    [InlineData(6, "jour")]
    public void SegmentContaining_ReturnsTheNodeCarryingTheOffset(int offset, string expected)
    {
        // Arrange
        var paragraph = Paragraph("<w:r><w:t>Bon</w:t></w:r><w:r><w:t>jour</w:t></w:r>");
        var map = ParagraphTextMap.Build(paragraph);

        // Act
        var segment = map.SegmentContaining(offset);

        // Assert
        Assert.NotNull(segment);
        Assert.Equal(expected, segment!.Value.Text.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void SegmentContaining_OutsideTheText_ReturnsNull(int offset)
    {
        // Arrange
        var paragraph = Paragraph("<w:r><w:t>Bon</w:t></w:r><w:r><w:t>jour</w:t></w:r>");
        var map = ParagraphTextMap.Build(paragraph);

        // Act & Assert
        Assert.Null(map.SegmentContaining(offset));
    }

    [Fact]
    public void Build_MatchesTheTextTheValidatorReasonsOn()
    {
        // Arrange — garde-fou : la table est la définition unique du « texte
        // d'un paragraphe », les offsets relevés par le validateur en dépendent
        var paragraph = Paragraph("<w:r><w:t>C'est </w:t></w:r><w:r><w:t>l'été</w:t></w:r>");
        var map = ParagraphTextMap.Build(paragraph);

        // Act
        var concatenated = string.Concat(paragraph.Descendants(W + "t").Select(t => t.Value));

        // Assert
        Assert.Equal(concatenated, map.Text);
    }
}
