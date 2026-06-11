namespace Scrubx.Cli;

public class CommandLineOptions
{
    public string? InputPath { get; set; }
    public bool ShowHelp { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Verbose { get; set; }
    public bool ShowWarnings { get; set; }
}

public static class ArgumentParser
{
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-i" || arg == "--input")
            {
                if (i + 1 < args.Length)
                {
                    options.InputPath = args[++i];
                }
                else
                {
                    options.ErrorMessage = "Erreur : Chemin du fichier d'entrée manquant après l'option -i/--input.";
                    return options;
                }
            }
            else if (arg == "-h" || arg == "--help")
            {
                options.ShowHelp = true;
            }
            else if (arg == "-v" || arg == "--verbose")
            {
                options.Verbose = true;
            }
            else if (arg == "-w" || arg == "--warning")
            {
                options.ShowWarnings = true;
            }
            else
            {
                options.ErrorMessage = $"Erreur : Argument inconnu '{arg}'.";
                return options;
            }
        }

        if (!options.ShowHelp && string.IsNullOrEmpty(options.InputPath))
        {
            options.ErrorMessage = "Erreur : L'option -i/--input <fichier.docx> est requise.";
        }

        return options;
    }
}
