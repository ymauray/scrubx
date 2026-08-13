namespace Scrubx.Cli;

public record RuleDefinition(string RuleName, string Code, string Title, bool IsWarningByDefault, string Theme);

public static class RuleCatalog
{
    public const string ThemeTypographie = "Typographie";
    public const string ThemeMiseEnPage = "Mise en page";

    public static readonly IReadOnlyList<RuleDefinition> All = new[]
    {
        new RuleDefinition("ApostropheDroite", "APOS", "Apostrophes droites détectées (veuillez utiliser des apostrophes courbées ’)", false, ThemeTypographie),
        new RuleDefinition("GuillemetDroit", "GDROIT", "Guillemets droits détectés (veuillez utiliser des guillemets français « ou »)", false, ThemeTypographie),
        new RuleDefinition("EspaceGuillemet", "EGUIL", "Espaces insécables manquantes autour des guillemets français (« ou »)", false, ThemeTypographie),
        new RuleDefinition("TiretDebutInvalide", "TIRET", "Tirets/puces de début de ligne invalides (veuillez utiliser des tirets cadratins —)", false, ThemeTypographie),
        new RuleDefinition("EspaceInsecableManquante", "EIMANQ", "Espaces insécables manquantes après un tiret cadratin", false, ThemeTypographie),
        new RuleDefinition("EspaceInsecablePonctuation", "EIPONC", "Espaces insécables manquantes avant un point d'exclamation ou d'interrogation (! ou ?)", false, ThemeTypographie),
        new RuleDefinition("EspaceFinParagraphe", "EFINPAR", "Espace en fin de paragraphe détectée (veuillez la supprimer)", false, ThemeTypographie),
        new RuleDefinition("DoubleEspace", "DESPACE", "Espaces consécutives détectées (les doubles espaces ne sont pas autorisées)", false, ThemeTypographie),
        new RuleDefinition("VirguleAvantEt", "VIRGET", "Virgule détectée juste avant le mot 'et' (avertissement d'énumération)", true, ThemeTypographie),
        new RuleDefinition("StyleParagrapheInvalide", "STYLEINV", "Styles de paragraphe non autorisés (veuillez utiliser uniquement 'Normal', 'Titre1' ou 'Ellipse')", false, ThemeMiseEnPage),
        new RuleDefinition("StyleTitre1Manquant", "TITRE1", "Style de paragraphe 'Titre1' manquant (le document doit contenir au moins un paragraphe portant ce style)", false, ThemeMiseEnPage),
        new RuleDefinition("SautDePageDetecte", "SAUTPAGE", "Saut de page détecté (les sauts de page ne sont pas autorisés)", false, ThemeMiseEnPage),
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

    public static RuleDefinition? GetByCode(string code) =>
        All.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
}
