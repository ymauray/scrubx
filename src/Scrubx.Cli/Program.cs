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

// Résolution des codes de règles à ignorer (surcharge la config, cf. -i/--ignore)
var ignoredRuleNames = new System.Collections.Generic.HashSet<string>();
foreach (var code in options.IgnoredRuleCodes)
{
    var rule = RuleCatalog.GetByCode(code);
    if (rule == null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Erreur : Code de règle inconnu '{code}'. Utilisez -r/--show-rules pour lister les codes valides.");
        Console.ResetColor();
        return 1;
    }
    ignoredRuleNames.Add(rule.RuleName);
}
var enabledRules = configEnabledRules.Except(ignoredRuleNames).ToHashSet();

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
    return 4;
}

if (warnings.Any())
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Le document est valide, mais des avertissements ont été relevés :");
    Console.WriteLine();
    DisplayWarnings(warnings, options.ShowWarnings, options.Verbose);
    Console.ResetColor();
    return 0;
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("Félicitations ! Le document est parfaitement valide.");
Console.ResetColor();
return 0;

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
    Console.WriteLine("  Scrubx.Cli <fichier.docx> [-v|--verbose] [-w|--warning] [-i|--ignore <code>[,<code>...]]");
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

