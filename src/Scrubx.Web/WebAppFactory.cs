using Microsoft.AspNetCore.HttpOverrides;
using Scrubx.Cli;

namespace Scrubx.Web;

public static class WebAppFactory
{
    public const long MaxUploadBytes = 20 * 1024 * 1024; // 20 Mo

    // ContentRootPath explicite : nécessaire quand cet hôte est embarqué dans un autre
    // processus (ex. Scrubx.Desktop), où le répertoire courant ne correspond pas forcément
    // au dossier de l'exécutable (donc à wwwroot).
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        configure?.Invoke(builder);

        var app = builder.Build();

        // L'application tourne derrière un reverse proxy nginx : elle ne voit que des
        // requêtes en provenance de localhost, ces en-têtes restituent le vrai schéma/IP client.
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/rules", () => Results.Ok(RuleCatalog.All));

        app.MapPost("/api/validate", async (HttpRequest request) =>
        {
            var (error, upload) = await ReadUploadAsync(request);
            if (error != null) return error;

            using var content = upload!.Content;
            var report = DocxValidator.Validate(content, upload.EnabledRules);

            var response = new
            {
                isValid = report.IsValid,
                // Nombre de corrections que /api/revisions saurait écrire pour ce document.
                fixableCount = report.Errors.Count(e => e.Fix != null),
                errors = report.Errors.Select(e => new
                {
                    ruleName = e.RuleName,
                    title = RuleCatalog.GetTitle(e.RuleName),
                    message = e.Message,
                    context = e.Context,
                    isWarning = e.IsWarning
                })
            };

            return Results.Ok(response);
        });

        // Renvoie une copie du document où les corrections sont inscrites en révisions
        // suivies et commentées — équivalent de l'option --revisions de la CLI.
        app.MapPost("/api/revisions", async (HttpRequest request) =>
        {
            var (error, upload) = await ReadUploadAsync(request);
            if (error != null) return error;

            using var content = upload!.Content;
            var report = DocxValidator.Validate(content, upload.EnabledRules);
            content.Position = 0;

            if (!report.Errors.Any(e => e.Fix != null))
            {
                return Results.BadRequest(new { error = "Aucune correction automatique à proposer pour ce document." });
            }

            var options = new RevisionOptions();
            if (upload.Author.Length > 0) options.Author = upload.Author;

            var output = new MemoryStream();
            try
            {
                DocxRevisionWriter.Write(content, output, report, options);
            }
            catch (DocxRevisionException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
            }

            var downloadName = Path.GetFileNameWithoutExtension(upload.FileName) + "-relu.docx";
            return Results.File(
                output.ToArray(),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                downloadName);
        });

        return app;
    }

    private sealed record Upload(MemoryStream Content, string FileName, HashSet<string> EnabledRules, string Author);

    /// <summary>
    /// Lit et valide le formulaire commun aux deux points d'entrée : le document, les règles
    /// désactivées, et le nom du relecteur.
    /// </summary>
    private static async Task<(IResult? Error, Upload? Upload)> ReadUploadAsync(HttpRequest request)
    {
        if (!request.HasFormContentType)
        {
            return (Results.BadRequest(new { error = "Requête multipart/form-data attendue." }), null);
        }

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");

        if (file == null || file.Length == 0)
        {
            return (Results.BadRequest(new { error = "Aucun fichier fourni (champ 'file' attendu)." }), null);
        }

        if (!Path.GetExtension(file.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return (Results.BadRequest(new { error = "Le fichier doit avoir l'extension .docx." }), null);
        }

        if (file.Length > MaxUploadBytes)
        {
            return (Results.BadRequest(new { error = $"Fichier trop volumineux (max {MaxUploadBytes / (1024 * 1024)} Mo)." }), null);
        }

        // Règles désactivées transmises en tant que valeurs répétées du champ 'disabledRules'.
        var disabledRules = form["disabledRules"]
            .SelectMany(v => v?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            .ToHashSet();

        var enabledRules = RuleCatalog.AllRuleNames
            .Where(r => !disabledRules.Contains(r))
            .ToHashSet();

        // Le contenu est mis en mémoire : /api/revisions le relit après la validation.
        var content = new MemoryStream();
        await using (var stream = file.OpenReadStream())
        {
            await stream.CopyToAsync(content);
        }
        content.Position = 0;

        return (null, new Upload(content, file.FileName, enabledRules, SanitizeAuthor(form["author"].ToString())));
    }

    /// <summary>
    /// Le nom du relecteur atterrit dans des attributs XML : on écarte les caractères de
    /// contrôle et on borne la longueur.
    /// </summary>
    private static string SanitizeAuthor(string author)
    {
        var cleaned = new string(author.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > 100 ? cleaned[..100] : cleaned;
    }
}
