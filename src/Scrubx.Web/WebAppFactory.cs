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
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Requête multipart/form-data attendue." });
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "Aucun fichier fourni (champ 'file' attendu)." });
            }

            if (!Path.GetExtension(file.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "Le fichier doit avoir l'extension .docx." });
            }

            if (file.Length > MaxUploadBytes)
            {
                return Results.BadRequest(new { error = $"Fichier trop volumineux (max {MaxUploadBytes / (1024 * 1024)} Mo)." });
            }

            // Règles désactivées transmises en tant que valeurs répétées du champ 'disabledRules'.
            var disabledRules = form["disabledRules"]
                .SelectMany(v => v?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
                .ToHashSet();

            var enabledRules = RuleCatalog.AllRuleNames
                .Where(r => !disabledRules.Contains(r))
                .ToHashSet();

            await using var stream = file.OpenReadStream();
            var report = DocxValidator.Validate(stream, enabledRules);

            var response = new
            {
                isValid = report.IsValid,
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

        return app;
    }
}
