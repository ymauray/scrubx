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

var report = DocxValidator.Validate(fileInfo.FullName);

var errors = report.Errors.Where(e => !e.IsWarning).ToList();
var warnings = report.Errors.Where(e => e.IsWarning).ToList();

if (errors.Any())
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Des erreurs de validation ont été détectées :");
    Console.WriteLine();
    DisplayGroupedIssues(errors, options.Verbose);
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

static void DisplayGroupedIssues(System.Collections.Generic.List<ValidationError> issues, bool verbose)
{
    var grouped = issues.GroupBy(e => e.RuleName);
    foreach (var group in grouped)
    {
        var title = RuleCatalog.GetTitle(group.Key);
        var count = group.Count();
        Console.WriteLine($"* {title} ({count} occurrence{(count > 1 ? "s" : "")})");
        
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
        DisplayGroupedIssues(warnings, verbose);
    }
    else
    {
        var grouped = warnings.GroupBy(e => e.RuleName);
        foreach (var group in grouped)
        {
            var title = RuleCatalog.GetTitle(group.Key);
            Console.WriteLine($"* {title}");
        }
    }
}

static void PrintUsage()
{
    Console.WriteLine("Utilisation :");
    Console.WriteLine("  Scrubx.Cli -i|--input <fichier.docx> [-v|--verbose] [-w|--warning]");
    Console.WriteLine("  Scrubx.Cli -h|--help");
}

