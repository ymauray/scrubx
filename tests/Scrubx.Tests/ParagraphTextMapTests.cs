using System.Linq;
using System.Xml.Linq;
using Scrubx.Cli;
using Xunit;

namespace Scrubx.Tests;

public class ParagraphTextMapTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static XElement Paragraph(string innerXml) =>
        XElement.Parse($"""
            <w:p xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                 xmlns:xml="http://www.w3.org/XML/1998/namespace">{innerXml}</w:p>
            """);

    private static string Run(string text, string? rPr = null) =>
        $"<w:r>{rPr ?? ""}<w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    [Fact]
    public void Text_ConcatenatesAllTextElements()
    {
        var map = new ParagraphTextMap(Paragraph(Run("Bonjour ") + Run("le ") + Run("monde")));

        Assert.Equal("Bonjour le monde", map.Text);
    }

    [Fact]
    public void SplitRange_InsideASingleRun_IsolatesTheRangeWithoutChangingTheText()
    {
        var paragraph = Paragraph(Run("Bonjour le monde"));
        var map = new ParagraphTextMap(paragraph);

        var runs = map.SplitRange(8, 2); // "le"

        var isolated = Assert.Single(runs);
        Assert.Equal("le", isolated.Element(W + "t")!.Value);
        Assert.Equal("Bonjour le monde", new ParagraphTextMap(paragraph).Text);
        Assert.Equal(3, paragraph.Elements(W + "r").Count());
    }

    [Fact]
    public void SplitRange_SpanningTwoRuns_ReturnsBothRuns()
    {
        var paragraph = Paragraph(Run("Bonjour ") + Run("le monde"));
        var map = new ParagraphTextMap(paragraph);

        var runs = map.SplitRange(4, 6); // "our le"

        Assert.Equal(2, runs.Count);
        Assert.Equal("our ", runs[0].Element(W + "t")!.Value);
        Assert.Equal("le", runs[1].Element(W + "t")!.Value);
        Assert.Equal("Bonjour le monde", new ParagraphTextMap(paragraph).Text);
    }

    [Fact]
    public void SplitRange_OnAnExistingRunBoundary_DoesNotSplitAnything()
    {
        var paragraph = Paragraph(Run("Bonjour ") + Run("le monde"));
        var map = new ParagraphTextMap(paragraph);

        var runs = map.SplitRange(0, 8); // "Bonjour "

        Assert.Single(runs);
        Assert.Equal(2, paragraph.Elements(W + "r").Count());
    }

    [Fact]
    public void SplitRange_PreservesRunProperties()
    {
        var paragraph = Paragraph(Run("Bonjour le monde", "<w:rPr><w:i/></w:rPr>"));
        var map = new ParagraphTextMap(paragraph);

        var runs = map.SplitRange(8, 2);

        Assert.All(paragraph.Elements(W + "r"), r => Assert.NotNull(r.Element(W + "rPr")!.Element(W + "i")));
        Assert.NotNull(runs[0].Element(W + "rPr"));
    }

    [Fact]
    public void SplitRange_KeepsXmlSpacePreserveOnBothHalves()
    {
        var paragraph = Paragraph(Run("Bonjour  monde"));
        var map = new ParagraphTextMap(paragraph);

        var runs = map.SplitRange(7, 2); // les deux espaces

        Assert.All(
            paragraph.Descendants(W + "t"),
            t => Assert.Equal("preserve", t.Attribute(XNamespace.Xml + "space")?.Value));
        Assert.Equal("  ", runs[0].Element(W + "t")!.Value);
    }

    [Fact]
    public void SplitRange_DoesNotSwallowNonTextSiblingsOfTheSameRun()
    {
        // Un même run porte « ab », une tabulation, puis « cd » : isoler « ab » ne doit pas
        // emporter la tabulation avec lui.
        var paragraph = Paragraph(
            "<w:r><w:t>ab</w:t><w:tab/><w:t>cd</w:t></w:r>");
        var map = new ParagraphTextMap(paragraph);

        var runs = map.SplitRange(0, 2);

        var isolated = Assert.Single(runs);
        Assert.Null(isolated.Element(W + "tab"));
        Assert.Equal("abcd", new ParagraphTextMap(paragraph).Text);
        Assert.Single(paragraph.Descendants(W + "tab"));
    }

    [Fact]
    public void SplitForInsertion_AtTheStart_ReturnsTheFirstRun()
    {
        var paragraph = Paragraph(Run("Bonjour ") + Run("le monde"));
        var map = new ParagraphTextMap(paragraph);

        var anchor = map.SplitForInsertion(0);

        Assert.Same(paragraph.Elements(W + "r").First(), anchor);
    }

    [Fact]
    public void SplitForInsertion_InTheMiddle_ReturnsTheRunStartingThere()
    {
        var paragraph = Paragraph(Run("Bonjour le monde"));
        var map = new ParagraphTextMap(paragraph);

        var anchor = map.SplitForInsertion(8);

        Assert.NotNull(anchor);
        Assert.Equal("le monde", anchor!.Element(W + "t")!.Value);
        Assert.Equal("Bonjour le monde", new ParagraphTextMap(paragraph).Text);
    }

    [Fact]
    public void SplitForInsertion_AtTheEnd_ReturnsNull()
    {
        var paragraph = Paragraph(Run("Bonjour"));
        var map = new ParagraphTextMap(paragraph);

        Assert.Null(map.SplitForInsertion(7));
    }

    [Fact]
    public void SplitRange_WhenTextIsNotCarriedByARun_Throws()
    {
        var paragraph = Paragraph("<w:fldSimple><w:t>Bonjour</w:t></w:fldSimple>");
        var map = new ParagraphTextMap(paragraph);

        Assert.Throws<ParagraphStructureException>(() => map.SplitRange(0, 3));
    }
}
