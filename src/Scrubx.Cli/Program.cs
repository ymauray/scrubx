using System;
using System.IO;
using System.Linq;
using Scrubx.Cli;

var options = ArgumentParser.Parse(args);

if (options.ShowHelp || !string.IsNullOrEmpty(options.ErrorMessage))
{
    if (!string.IsNullOrEmpty(options.ErrorMessage))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(options.ErrorMessage);
        Console.ResetColor();
        Console.WriteLine();
    }
    PrintUsage();
    return !string.IsNullOrEmpty(options.ErrorMessage) ? 1 : 0;
}

if (options.CreateConfig)
{
    var existedBefore = File.Exists(RuleConfig.DefaultFileName);
    var added = RuleConfig.CreateOrUpdate(RuleConfig.DefaultFileName);

    if (added == null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Erreur : Le fichier '{RuleConfig.DefaultFileName}' existe mais n'est pas un JSON valide.");
        Console.ResetColor();
        return 1;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    if (!existedBefore)
    {
        Console.WriteLine($"Fichier '{RuleConfig.DefaultFileName}' créé avec {added} règle(s) activée(s).");
    }
    else if (added > 0)
    {
        Console.WriteLine($"Fichier '{RuleConfig.DefaultFileName}' mis à jour ({added} règle(s) ajoutée(s)).");
    }
    else
    {
        Console.WriteLine($"Fichier '{RuleConfig.DefaultFileName}' déjà à jour (aucune règle manquante).");
    }
    Console.ResetColor();
    return 0;
}

if (options.ShowRules)
{
    PrintRules();
    return 0;
}

// Règles activées/désactivées par scrubx.json (s'il existe), sinon toutes activées par défaut
var configEnabledRules = RuleConfig.TryLoadEnabledRuleNames(RuleConfig.DefaultFileName, out var configErrorMessage);
if (configEnabledRules == null)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(configErrorMessage);
    Console.ResetColor();
    return 1;
}

// Résolution des codes de règles à ignorer/forcer (surchargent la config, cf. -i/--ignore et -f/--force)
if (!TryResolveRuleCodes(options.IgnoredRuleCodes, out var ignoredRuleNames, out var ignoreError))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(ignoreError);
    Console.ResetColor();
    return 1;
}
if (!TryResolveRuleCodes(options.ForcedRuleCodes, out var forcedRuleNames, out var forceError))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(forceError);
    Console.ResetColor();
    return 1;
}

// -f/--force l'emporte sur -i/--ignore en cas de code commun aux deux options.
var enabledRules = configEnabledRules.Except(ignoredRuleNames).Union(forcedRuleNames).ToHashSet();

// Validation of file existence and extension
var fileInfo = new FileInfo(options.InputPath!);
if (!fileInfo.Exists)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Erreur : Le fichier '{options.InputPath}' n'existe pas.");
    Console.ResetColor();
    return 2;
}

if (fileInfo.Extension.ToLowerInvariant() != ".docx")
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Erreur : Le fichier '{options.InputPath}' doit être un document avec l'extension .docx.");
    Console.ResetColor();
    return 3;
}

Console.ForegroundColor = ConsoleColor.Blue;
Console.WriteLine($"Analyse du fichier : {fileInfo.FullName}...");
Console.ResetColor();

var report = DocxValidator.Validate(fileInfo.FullName, enabledRules);

var errors = report.Errors.Where(e => !e.IsWarning).ToList();
var warnings = report.Errors.Where(e => e.IsWarning).ToList();

int exitCode = 0;

if (errors.Any())
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Des erreurs de validation ont été détectées :");
    Console.WriteLine();
    DisplayGroupedIssues(errors, options.Verbose, ConsoleColor.Red);
    Console.ResetColor();

    if (warnings.Any())
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Avertissements :");
        Console.WriteLine();
        DisplayWarnings(warnings, options.ShowWarnings, options.Verbose);
        Console.ResetColor();
    }
    exitCode = 4;
}
else if (warnings.Any())
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Le document est valide, mais des avertissements ont été relevés :");
    Console.WriteLine();
    DisplayWarnings(warnings, options.ShowWarnings, options.Verbose);
    Console.ResetColor();
}
else
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Félicitations ! Le document est parfaitement valide.");
    Console.ResetColor();
}

if (options.WriteRevisions)
{
    Console.WriteLine();
    var destination = options.RevisionsPath ?? DefaultRevisionsPath(fileInfo);
    if (!WriteRevisions(fileInfo.FullName, destination, report, options.Author))
    {
        return 5;
    }
}

return exitCode;

static string DefaultRevisionsPath(FileInfo source) =>
    Path.Combine(
        source.DirectoryName ?? ".",
        Path.GetFileNameWithoutExtension(source.Name) + "-relu.docx");

static bool WriteRevisions(string sourcePath, string destinationPath, ValidationReport report, string? author)
{
    if (!report.Errors.Any(e => e.Fix != null))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Aucune correction automatique à proposer : aucune copie annotée n'a été écrite.");
        Console.ResetColor();
        return true;
    }

    if (File.Exists(destinationPath))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Erreur : Le fichier '{destinationPath}' existe déjà. Supprimez-le ou indiquez un autre nom avec --revisions=<fichier>.");
        Console.ResetColor();
        return false;
    }

    var revisionOptions = new RevisionOptions();
    if (!string.IsNullOrWhiteSpace(author)) revisionOptions.Author = author;

    RevisionResult result;
    try
    {
        result = DocxRevisionWriter.Write(sourcePath, destinationPath, report, revisionOptions);
    }
    catch (Exception ex) when (ex is DocxRevisionException or IOException or UnauthorizedAccessException)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Erreur : {ex.Message}");
        Console.ResetColor();
        return false;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Copie annotée écrite dans '{destinationPath}' : {result.RevisionCount} révision(s) et {result.CommentCount} commentaire(s).");
    Console.ResetColor();

    if (result.SkippedCount > 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"{result.SkippedCount} correction(s) n'ont pas pu être écrites et restent à traiter à la main.");
        Console.ResetColor();
    }

    return true;
}

static bool TryResolveRuleCodes(System.Collections.Generic.List<string> codes, out System.Collections.Generic.HashSet<string> ruleNames, out string? errorMessage)
{
    ruleNames = new System.Collections.Generic.HashSet<string>();
    errorMessage = null;

    foreach (var code in codes)
    {
        var rule = RuleCatalog.GetByCode(code);
        if (rule == null)
        {
            errorMessage = $"Erreur : Code de règle inconnu '{code}'. Utilisez -r/--show-rules pour lister les codes valides.";
            return false;
        }
        ruleNames.Add(rule.RuleName);
    }

    return true;
}

static void DisplayGroupedIssues(System.Collections.Generic.List<ValidationError> issues, bool verbose, ConsoleColor lineColor)
{
    var grouped = issues.GroupBy(e => e.RuleName);
    foreach (var group in grouped)
    {
        var title = RuleCatalog.GetTitle(group.Key);
        var code = RuleCatalog.GetCode(group.Key);
        var count = group.Count();

        Console.Write("* ");
        if (code != null)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(code);
            Console.ForegroundColor = lineColor;
            Console.Write(" : ");
        }
        Console.WriteLine($"{title} ({count} occurrence{(count > 1 ? "s" : "")})");

        if (verbose)
        {
            foreach (var error in group)
            {
                if (!string.IsNullOrEmpty(error.Context))
                {
                    Console.WriteLine($"    Contexte : {error.Context}");
                }
            }
        }
    }
}

static void DisplayWarnings(System.Collections.Generic.List<ValidationError> warnings, bool showWarnings, bool verbose)
{
    if (showWarnings)
    {
        DisplayGroupedIssues(warnings, verbose, ConsoleColor.Yellow);
    }
    else
    {
        var grouped = warnings.GroupBy(e => e.RuleName);
        foreach (var group in grouped)
        {
            var title = RuleCatalog.GetTitle(group.Key);
            var code = RuleCatalog.GetCode(group.Key);

            Console.Write("* ");
            if (code != null)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(code);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(" : ");
            }
            Console.WriteLine(title);
        }
    }
}

static void PrintUsage()
{
    Console.WriteLine("Utilisation :");
    Console.WriteLine("  Scrubx.Cli <fichier.docx> [-v|--verbose] [-w|--warning] [-i|--ignore <code>[,<code>...]] [-f|--force <code>[,<code>...]]");
    Console.WriteLine("                            [--revisions[=<fichier.docx>]] [--author <nom>]");
    Console.WriteLine("  Scrubx.Cli -r|--show-rules");
    Console.WriteLine("  Scrubx.Cli -c|--create-config");
    Console.WriteLine("  Scrubx.Cli -h|--help");
}

static void PrintRules()
{
    var themes = RuleCatalog.All.GroupBy(r => r.Theme);
    foreach (var theme in themes)
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine(theme.Key);
        Console.ResetColor();
        foreach (var rule in theme)
        {
            Console.WriteLine($"  {rule.Code,-10}{rule.Title}");
        }
    }
}

