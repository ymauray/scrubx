namespace Scrubx.Cli;

public record RuleDefinition(string RuleName, string Title, bool IsWarningByDefault);

public static class RuleCatalog
{
    public static readonly IReadOnlyList<RuleDefinition> All = new[]
    {
        new RuleDefinition("ApostropheDroite", "Apostrophes droites détectées (veuillez utiliser des apostrophes courbées ’)", false),
        new RuleDefinition("TiretDebutInvalide", "Tirets/puces de début de ligne invalides (veuillez utiliser des tirets cadratins —)", false),
        new RuleDefinition("EspaceInsecableManquante", "Espaces insécables manquantes après un tiret cadratin", false),
        new RuleDefinition("EspaceInsecablePonctuation", "Espaces insécables manquantes avant un point d'exclamation ou d'interrogation (! ou ?)", false),
        new RuleDefinition("GuillemetDroit", "Guillemets droits détectés (veuillez utiliser des guillemets français « ou »)", false),
        new RuleDefinition("EspaceGuillemet", "Espaces insécables manquantes autour des guillemets français (« ou »)", false),
        new RuleDefinition("StyleParagrapheInvalide", "Styles de paragraphe non autorisés (veuillez utiliser uniquement 'Normal', 'Titre1' ou 'Ellipse')", false),
        new RuleDefinition("StyleTitre1Manquant", "Style de paragraphe 'Titre1' manquant (le document doit contenir au moins un paragraphe portant ce style)", false),
        new RuleDefinition("SautDePageDetecte", "Saut de page détecté (les sauts de page ne sont pas autorisés)", false),
        new RuleDefinition("EspaceFinParagraphe", "Espace en fin de paragraphe détectée (veuillez la supprimer)", false),
        new RuleDefinition("DoubleEspace", "Espaces consécutives détectées (les doubles espaces ne sont pas autorisées)", false),
        new RuleDefinition("VirguleAvantEt", "Virgule détectée juste avant le mot 'et' (avertissement d'énumération)", true),
    };

    public static readonly IReadOnlySet<string> AllRuleNames =
        All.Select(r => r.RuleName).ToHashSet();

    // Codes de diagnostic internes (erreurs de lecture/parsing), non désactivables : pas des règles de contenu.
    private static readonly IReadOnlyDictionary<string, string> DiagnosticTitles = new Dictionary<string, string>
    {
        ["LectureFichier"] = "Erreur d'ouverture du fichier",
        ["LectureDocument"] = "Erreur de lecture du document",
    };

    public static string GetTitle(string ruleName)
    {
        var match = All.FirstOrDefault(r => r.RuleName == ruleName);
        if (match != null) return match.Title;
        return DiagnosticTitles.TryGetValue(ruleName, out var title) ? title : "Autres anomalies";
    }
}
