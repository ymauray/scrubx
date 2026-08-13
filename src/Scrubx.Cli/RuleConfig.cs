using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scrubx.Cli;

public static class RuleConfig
{
    public const string DefaultFileName = "scrubx.json";

    /// <summary>
    /// Crée le fichier de config s'il n'existe pas, ou y ajoute les codes de règles du
    /// catalogue absents du fichier (valeur par défaut : activée), sans toucher aux
    /// entrées déjà présentes. Retourne le nombre de règles ajoutées, ou null si le
    /// fichier existant n'est pas un JSON valide.
    /// </summary>
    public static int? CreateOrUpdate(string path)
    {
        JsonObject root;
        if (File.Exists(path))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject();
            }
            catch (JsonException)
            {
                return null;
            }
        }
        else
        {
            root = new JsonObject();
        }

        var added = 0;
        foreach (var rule in RuleCatalog.All)
        {
            if (!root.ContainsKey(rule.Code))
            {
                root[rule.Code] = true;
                added++;
            }
        }

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return added;
    }

    /// <summary>
    /// Lit le fichier de config s'il existe et retourne l'ensemble des règles activées
    /// (RuleName). Un code absent du fichier reste activé par défaut ; un code du fichier
    /// inconnu du catalogue est ignoré. Retourne null (et <paramref name="errorMessage"/>
    /// renseigné) si le fichier existe mais est invalide.
    /// </summary>
    public static IReadOnlySet<string>? TryLoadEnabledRuleNames(string path, out string? errorMessage)
    {
        errorMessage = null;

        if (!File.Exists(path))
        {
            return RuleCatalog.AllRuleNames;
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            errorMessage = $"Erreur : Le fichier de configuration '{path}' n'est pas un JSON valide.";
            return null;
        }

        var enabled = new HashSet<string>();
        foreach (var rule in RuleCatalog.All)
        {
            var isEnabled = true;
            if (root.TryGetPropertyValue(rule.Code, out var value) && value is not null)
            {
                try
                {
                    isEnabled = value.GetValue<bool>();
                }
                catch (Exception)
                {
                    errorMessage = $"Erreur : La valeur de '{rule.Code}' dans '{path}' doit être un booléen (true/false).";
                    return null;
                }
            }

            if (isEnabled)
            {
                enabled.Add(rule.RuleName);
            }
        }

        return enabled;
    }
}
