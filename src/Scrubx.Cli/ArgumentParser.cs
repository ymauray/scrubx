namespace Scrubx.Cli;

public class CommandLineOptions
{
    public string? InputPath { get; set; }
    public bool ShowHelp { get; set; }
    public bool ShowRules { get; set; }
    public bool CreateConfig { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Verbose { get; set; }
    public bool ShowWarnings { get; set; }
    public List<string> IgnoredRuleCodes { get; set; } = new();
    public List<string> ForcedRuleCodes { get; set; } = new();

    /// <summary>Écrire une copie du document avec les corrections en révisions suivies.</summary>
    public bool WriteRevisions { get; set; }

    /// <summary>Chemin de la copie annotée, ou <c>null</c> pour « &lt;source&gt;-relu.docx ».</summary>
    public string? RevisionsPath { get; set; }

    /// <summary>Auteur affiché par Word pour les révisions et les commentaires.</summary>
    public string? Author { get; set; }
}

public static class ArgumentParser
{
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-i" || arg == "--ignore")
            {
                if (i + 1 < args.Length)
                {
                    var codes = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    options.IgnoredRuleCodes.AddRange(codes);
                }
                else
                {
                    options.ErrorMessage = "Erreur : Code(s) de règle manquant(s) après l'option -i/--ignore.";
                    return options;
                }
            }
            else if (arg == "-f" || arg == "--force")
            {
                if (i + 1 < args.Length)
                {
                    var codes = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    options.ForcedRuleCodes.AddRange(codes);
                }
                else
                {
                    options.ErrorMessage = "Erreur : Code(s) de règle manquant(s) après l'option -f/--force.";
                    return options;
                }
            }
            else if (arg == "--revisions" || arg.StartsWith("--revisions="))
            {
                options.WriteRevisions = true;

                // La forme « --revisions <fichier> » serait ambiguë avec le fichier à analyser :
                // le chemin de sortie se donne avec un signe égal.
                if (arg.Length > "--revisions".Length)
                {
                    var path = arg["--revisions=".Length..];
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        options.ErrorMessage = "Erreur : Chemin manquant après l'option --revisions=.";
                        return options;
                    }
                    options.RevisionsPath = path;
                }
            }
            else if (arg == "--author")
            {
                if (i + 1 < args.Length)
                {
                    options.Author = args[++i];
                }
                else
                {
                    options.ErrorMessage = "Erreur : Nom manquant après l'option --author.";
                    return options;
                }
            }
            else if (arg == "-h" || arg == "--help")
            {
                options.ShowHelp = true;
            }
            else if (arg == "-r" || arg == "--show-rules")
            {
                options.ShowRules = true;
            }
            else if (arg == "-c" || arg == "--create-config")
            {
                options.CreateConfig = true;
            }
            else if (arg == "-v" || arg == "--verbose")
            {
                options.Verbose = true;
            }
            else if (arg == "-w" || arg == "--warning")
            {
                options.ShowWarnings = true;
            }
            else if (arg.StartsWith('-'))
            {
                options.ErrorMessage = $"Erreur : Argument inconnu '{arg}'.";
                return options;
            }
            else if (options.InputPath == null)
            {
                options.InputPath = arg;
            }
            else
            {
                options.ErrorMessage = $"Erreur : Argument positionnel inattendu '{arg}' (fichier déjà spécifié : '{options.InputPath}').";
                return options;
            }
        }

        if (!options.ShowHelp && !options.ShowRules && !options.CreateConfig && string.IsNullOrEmpty(options.InputPath))
        {
            options.ErrorMessage = "Erreur : Le fichier .docx à analyser est requis.";
        }

        return options;
    }
}
