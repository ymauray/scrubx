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
