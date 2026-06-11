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
        var title = GetRuleTitle(group.Key);
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

static string GetRuleTitle(string ruleName)
{
    return ruleName switch
    {
        "ApostropheDroite" => "Apostrophes droites détectées (veuillez utiliser des apostrophes courbées ’)",
        "TiretDebutInvalide" => "Tirets/puces de début de ligne invalides (veuillez utiliser des tirets cadratins —)",
        "EspaceInsecableManquante" => "Espaces insécables manquantes après un tiret cadratin",
        "EspaceInsecablePonctuation" => "Espaces insécables manquantes avant un point d'exclamation ou d'interrogation (! ou ?)",
        "GuillemetDroit" => "Guillemets droits détectés (veuillez utiliser des guillemets français « ou »)",
        "EspaceGuillemet" => "Espaces insécables manquantes autour des guillemets français (« ou »)",
        "StyleParagrapheInvalide" => "Styles de paragraphe non autorisés (veuillez utiliser uniquement 'Normal', 'Titre1' ou 'Ellipse')",
        "StyleTitre1Manquant" => "Style de paragraphe 'Titre1' manquant (le document doit contenir au moins un paragraphe portant ce style)",
        "SautDePageDetecte" => "Saut de page détecté (les sauts de page ne sont pas autorisés)",
        "EspaceFinParagraphe" => "Espace en fin de paragraphe détectée (veuillez la supprimer)",
        "DoubleEspace" => "Espaces consécutives détectées (les doubles espaces ne sont pas autorisées)",
        "VirguleAvantEt" => "Virgule détectée juste avant le mot 'et' (avertissement d'énumération)",
        "LectureFichier" => "Erreur d'ouverture du fichier",
        "LectureDocument" => "Erreur de lecture du document",
        _ => "Autres anomalies"
    };
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
            var title = GetRuleTitle(group.Key);
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

