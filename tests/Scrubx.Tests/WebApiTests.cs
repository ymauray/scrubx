using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Scrubx.Cli;
using Scrubx.Web;
using Xunit;

namespace Scrubx.Tests;

/// <summary>
/// Démarre l'application Web sur un serveur en mémoire, une fois pour toute la classe.
/// </summary>
public sealed class ScrubxWebFixture : IAsyncLifetime
{
    private WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _app = WebAppFactory.Create([], builder => builder.WebHost.UseTestServer());
        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_app != null) await _app.DisposeAsync();
    }
}

public class WebApiTests : IClassFixture<ScrubxWebFixture>
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly HttpClient _client;

    public WebApiTests(ScrubxWebFixture fixture) => _client = fixture.Client;

    /// <summary>Un .docx minimal dont le corps est le XML donné.</summary>
    private static byte[] CreateDocx(string bodyXml)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write($"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:pPr><w:pStyle w:val="Titre1"/></w:pPr><w:r><w:t>Titre</w:t></w:r></w:p>{bodyXml}</w:body></w:document>
                """);
        }
        return stream.ToArray();
    }

    /// <summary>Deux fautes corrigeables : une espace finale et un tiret de dialogue.</summary>
    private static byte[] DocumentWithFixableErrors() => CreateDocx(
        "<w:p><w:r><w:t xml:space=\"preserve\">Elle a peur. </w:t></w:r></w:p>" +
        "<w:p><w:r><w:t xml:space=\"preserve\">- </w:t></w:r><w:r><w:t>Elle a essayé.</w:t></w:r></w:p>");

    private static MultipartFormDataContent Form(
        byte[]? document,
        string fileName = "mon-document.docx",
        string disabledRules = "",
        string? author = null)
    {
        var form = new MultipartFormDataContent();
        if (document != null)
        {
            form.Add(new ByteArrayContent(document), "file", fileName);
        }
        form.Add(new StringContent(disabledRules), "disabledRules");
        if (author != null) form.Add(new StringContent(author, Encoding.UTF8), "author");
        return form;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static XDocument PartOf(byte[] docx, string partName)
    {
        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var entry = archive.GetEntry(partName);
        Assert.NotNull(entry);
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    [Fact]
    public async Task GetRules_ReturnsTheWholeCatalogue()
    {
        var response = await _client.GetAsync("/api/rules");

        response.EnsureSuccessStatusCode();
        var rules = await ReadJsonAsync(response);
        var codes = rules.EnumerateArray().Select(r => r.GetProperty("code").GetString()).ToList();
        Assert.Contains("APOS", codes);
        Assert.Contains("ESPAPRES", codes);
        Assert.Equal(RuleCatalog.All.Count, codes.Count);
    }

    [Fact]
    public async Task Validate_ReportsTheErrorsAndHowManyCanBeFixed()
    {
        var response = await _client.PostAsync("/api/validate", Form(DocumentWithFixableErrors()));

        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        Assert.False(body.GetProperty("isValid").GetBoolean());
        Assert.Equal(2, body.GetProperty("fixableCount").GetInt32());
        Assert.Equal(2, body.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public async Task Validate_HonoursDisabledRules()
    {
        var response = await _client.PostAsync(
            "/api/validate",
            Form(DocumentWithFixableErrors(), disabledRules: "EspaceFinParagraphe"));

        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        Assert.Equal(1, body.GetProperty("fixableCount").GetInt32());
        Assert.DoesNotContain(
            body.GetProperty("errors").EnumerateArray(),
            e => e.GetProperty("ruleName").GetString() == "EspaceFinParagraphe");
    }

    [Fact]
    public async Task Validate_OnAValidDocument_AnnouncesNothingToFix()
    {
        var response = await _client.PostAsync(
            "/api/validate",
            Form(CreateDocx("<w:p><w:r><w:t>Tout va bien.</w:t></w:r></w:p>")));

        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("isValid").GetBoolean());
        Assert.Equal(0, body.GetProperty("fixableCount").GetInt32());
    }

    [Theory]
    [InlineData("/api/validate")]
    [InlineData("/api/revisions")]
    public async Task Endpoints_WithoutAFile_ReturnBadRequest(string route)
    {
        var response = await _client.PostAsync(route, Form(document: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Aucun fichier fourni", (await ReadJsonAsync(response)).GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("/api/validate")]
    [InlineData("/api/revisions")]
    public async Task Endpoints_WithAWrongExtension_ReturnBadRequest(string route)
    {
        var response = await _client.PostAsync(route, Form(DocumentWithFixableErrors(), fileName: "notes.txt"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(".docx", (await ReadJsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Revisions_ReturnsTheAnnotatedCopyAsADownload()
    {
        var response = await _client.PostAsync("/api/revisions", Form(DocumentWithFixableErrors()));

        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            response.Content.Headers.ContentType!.MediaType);

        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("mon-document-relu.docx", disposition.FileNameStar ?? disposition.FileName?.Trim('"'));

        var copy = await response.Content.ReadAsByteArrayAsync();
        var document = PartOf(copy, "word/document.xml");
        Assert.Equal(2, document.Descendants(W + "commentRangeStart").Count());
        Assert.NotEmpty(document.Descendants(W + "del"));
        Assert.Equal(2, PartOf(copy, "word/comments.xml").Root!.Elements(W + "comment").Count());
    }

    [Fact]
    public async Task Revisions_UsesTheGivenAuthor()
    {
        var response = await _client.PostAsync(
            "/api/revisions",
            Form(DocumentWithFixableErrors(), author: "Yannick Mauray"));

        response.EnsureSuccessStatusCode();
        var copy = await response.Content.ReadAsByteArrayAsync();

        var authors = PartOf(copy, "word/document.xml")
            .Descendants()
            .Select(e => e.Attribute(W + "author")?.Value)
            .Where(a => a != null)
            .Distinct()
            .ToList();
        Assert.Equal(["Yannick Mauray"], authors);
    }

    [Fact]
    public async Task Revisions_WithoutAnAuthor_FallsBackToScrubx()
    {
        var response = await _client.PostAsync("/api/revisions", Form(DocumentWithFixableErrors(), author: "   "));

        response.EnsureSuccessStatusCode();
        var copy = await response.Content.ReadAsByteArrayAsync();

        Assert.Contains(
            PartOf(copy, "word/document.xml").Descendants(),
            e => e.Attribute(W + "author")?.Value == "Scrubx");
    }

    [Fact]
    public async Task Revisions_HonoursDisabledRules()
    {
        var response = await _client.PostAsync(
            "/api/revisions",
            Form(DocumentWithFixableErrors(), disabledRules: "EspaceFinParagraphe"));

        response.EnsureSuccessStatusCode();
        var copy = await response.Content.ReadAsByteArrayAsync();

        Assert.Single(PartOf(copy, "word/comments.xml").Root!.Elements(W + "comment"));
    }

    [Fact]
    public async Task Revisions_WhenNothingCanBeFixed_ReturnsBadRequest()
    {
        var response = await _client.PostAsync(
            "/api/revisions",
            Form(CreateDocx("<w:p><w:r><w:t>Tout va bien.</w:t></w:r></w:p>")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Aucune correction", (await ReadJsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Revisions_OnADocumentThatAlreadyHasRevisions_ReturnsConflict()
    {
        var document = CreateDocx(
            "<w:p><w:ins w:id=\"1\" w:author=\"X\" w:date=\"2026-01-01T00:00:00Z\"><w:r><w:t>Déjà relu.</w:t></w:r></w:ins></w:p>" +
            "<w:p><w:r><w:t xml:space=\"preserve\">Elle a peur. </w:t></w:r></w:p>");

        var response = await _client.PostAsync("/api/revisions", Form(document));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("révisions suivies", (await ReadJsonAsync(response)).GetProperty("error").GetString());
    }
}
