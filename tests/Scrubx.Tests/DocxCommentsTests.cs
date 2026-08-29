using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Scrubx.Cli;
using Xunit;

namespace Scrubx.Tests;

public class DocxCommentsTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private static readonly XNamespace W16CID = "http://schemas.microsoft.com/office/word/2016/wordml/cid";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string Body =
        "<w:p><w:r><w:t xml:space=\"preserve\">Elle a peur. </w:t></w:r></w:p>" +
        "<w:p><w:r><w:t xml:space=\"preserve\">- </w:t></w:r><w:r><w:t>Elle a essayé.</w:t></w:r></w:p>";

    /// <summary>Un .docx minimal mais complet : types de contenu, relations et styles.</summary>
    private static MemoryStream CreateDocx(string bodyXml, params (string Name, string Content)[] extraParts)
    {
        var parts = new (string Name, string Content)[]
        {
            ("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>
                """),
            ("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                """),
            ("word/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="paragraph" w:styleId="Normal"><w:name w:val="Normal"/></w:style></w:styles>
                """),
            ("word/document.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:pPr><w:pStyle w:val="Titre1"/></w:pPr><w:r><w:t>Titre</w:t></w:r></w:p>{bodyXml}</w:body></w:document>
                """),
        };

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts.Concat(extraParts))
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream WriteRevisions(Stream source, RevisionOptions? options = null)
    {
        var report = DocxValidator.Validate(source, null);
        source.Position = 0;
        var destination = new MemoryStream();
        DocxRevisionWriter.Write(source, destination, report, options);
        destination.Position = 0;
        return destination;
    }

    private static XDocument Part(Stream docx, string name)
    {
        docx.Position = 0;
        using var archive = new ZipArchive(docx, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var stream = entry!.Open();
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        docx.Position = 0;
        return document;
    }

    [Fact]
    public void Write_AddsOneCommentPerRevision()
    {
        using var source = CreateDocx(Body);

        using var revised = WriteRevisions(source);
        var comments = Part(revised, "word/comments.xml").Root!.Elements(W + "comment").ToList();

        Assert.Equal(2, comments.Count);
        Assert.Equal(
            ["EFINPAR : Espace en fin de paragraphe détectée (veuillez la supprimer)",
             "TIRET : Tirets/puces de début de ligne invalides (veuillez utiliser des tirets cadratins —)"],
            comments.Select(c => string.Concat(c.Descendants(W + "t").Select(t => t.Value))).ToArray());
    }

    [Fact]
    public void Write_AnchorsEachCommentAroundItsRevision()
    {
        using var source = CreateDocx(Body);

        using var revised = WriteRevisions(source);
        var document = Part(revised, "word/document.xml");

        foreach (var comment in Part(revised, "word/comments.xml").Root!.Elements(W + "comment"))
        {
            var id = comment.Attribute(W + "id")!.Value;
            var start = Assert.Single(document.Descendants(W + "commentRangeStart"), e => e.Attribute(W + "id")!.Value == id);
            var end = Assert.Single(document.Descendants(W + "commentRangeEnd"), e => e.Attribute(W + "id")!.Value == id);
            Assert.Single(document.Descendants(W + "commentReference"), e => e.Attribute(W + "id")!.Value == id);

            // La plage commentée encadre bien la révision.
            var between = start.ElementsAfterSelf().TakeWhile(e => e != end).ToList();
            Assert.NotEmpty(between);
            Assert.All(between, e => Assert.Contains(e.Name.LocalName, new[] { "ins", "del" }));
        }
    }

    [Fact]
    public void Write_DeclaresTheCommentPartsInTheContentTypesAndRelationships()
    {
        using var source = CreateDocx(Body);

        using var revised = WriteRevisions(source);

        var overrides = Part(revised, "[Content_Types].xml").Root!
            .Elements(ContentTypes + "Override")
            .Select(o => o.Attribute("PartName")!.Value)
            .ToList();
        Assert.Contains("/word/comments.xml", overrides);
        Assert.Contains("/word/commentsExtended.xml", overrides);
        Assert.Contains("/word/commentsIds.xml", overrides);

        var relationships = Part(revised, "word/_rels/document.xml.rels").Root!
            .Elements(PackageRelationships + "Relationship")
            .ToList();
        Assert.Contains(relationships, r => r.Attribute("Target")!.Value == "comments.xml");
        Assert.Contains(relationships, r => r.Attribute("Target")!.Value == "commentsExtended.xml");
        Assert.Contains(relationships, r => r.Attribute("Target")!.Value == "commentsIds.xml");
        Assert.Equal(
            relationships.Count,
            relationships.Select(r => r.Attribute("Id")!.Value).Distinct().Count());
    }

    [Fact]
    public void Write_KeepsCompanionPartsInSyncWithTheComments()
    {
        using var source = CreateDocx(Body);

        using var revised = WriteRevisions(source);

        var paraIds = Part(revised, "word/comments.xml").Root!
            .Elements(W + "comment")
            .Select(c => c.Element(W + "p")!.Attribute(XNamespace.Get("http://schemas.microsoft.com/office/word/2010/wordml") + "paraId")!.Value)
            .ToList();

        Assert.Equal(
            paraIds,
            Part(revised, "word/commentsExtended.xml").Root!.Elements(W15 + "commentEx").Select(e => e.Attribute(W15 + "paraId")!.Value).ToList());
        Assert.Equal(
            paraIds,
            Part(revised, "word/commentsIds.xml").Root!.Elements(W16CID + "commentId").Select(e => e.Attribute(W16CID + "paraId")!.Value).ToList());
        Assert.Equal(paraIds.Count, paraIds.Distinct().Count());
    }

    [Fact]
    public void Write_AddsTheCommentStylesWhenTheyAreMissing()
    {
        using var source = CreateDocx(Body);

        using var revised = WriteRevisions(source);
        var styleIds = Part(revised, "word/styles.xml").Root!
            .Elements(W + "style")
            .Select(s => s.Attribute(W + "styleId")!.Value)
            .ToList();

        Assert.Contains("Marquedecommentaire", styleIds);
        Assert.Contains("Commentaire", styleIds);
        Assert.Equal(styleIds.Count, styleIds.Distinct().Count());
    }

    [Fact]
    public void Write_UsesTheGivenAuthorForTheComments()
    {
        using var source = CreateDocx(Body);

        using var revised = WriteRevisions(source, new RevisionOptions { Author = "Yannick Mauray" });
        var comment = Part(revised, "word/comments.xml").Root!.Elements(W + "comment").First();

        Assert.Equal("Yannick Mauray", comment.Attribute(W + "author")!.Value);
        Assert.Equal("YM", comment.Attribute(W + "initials")!.Value);
    }

    [Fact]
    public void Write_PreservesCommentsAlreadyPresentInTheDocument()
    {
        var existing = (
            "word/comments.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:comments xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:comment w:id="7" w:author="Relecteur" w:date="2026-01-01T00:00:00Z" w:initials="R"><w:p><w:r><w:t>Remarque existante</w:t></w:r></w:p></w:comment></w:comments>
            """);
        using var source = CreateDocx(Body, existing);

        using var revised = WriteRevisions(source);
        var comments = Part(revised, "word/comments.xml").Root!.Elements(W + "comment").ToList();

        Assert.Equal(3, comments.Count);
        Assert.Equal("Remarque existante", string.Concat(comments[0].Descendants(W + "t").Select(t => t.Value)));
        // Les identifiants reprennent après le plus élevé déjà utilisé.
        Assert.Equal(["7", "8", "9"], comments.Select(c => c.Attribute(W + "id")!.Value).ToArray());
    }
}
