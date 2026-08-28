namespace Scrubx.Cli;

/// <summary>Nature de la correction proposée par une règle.</summary>
public enum SuggestedEditKind
{
    /// <summary>Supprimer les caractères `[Offset, Offset + Length)`.</summary>
    Delete,

    /// <summary>Insérer <see cref="SuggestedEdit.Text"/> à `Offset`.</summary>
    Insert,

    /// <summary>Remplacer `[Offset, Offset + Length)` par <see cref="SuggestedEdit.Text"/>.</summary>
    Replace,

    /// <summary>Appliquer au paragraphe le style <see cref="SuggestedEdit.Text"/>.</summary>
    ParagraphStyle,

    /// <summary>Retirer la propriété de paragraphe « saut de page avant ».</summary>
    RemovePageBreak,
}

/// <summary>
/// Correction qu'une règle propose pour une anomalie, exprimée sur le texte du
/// paragraphe au sens de <see cref="ParagraphTextMap"/>.
///
/// Une proposition n'est qu'une proposition : `DocxReviewer` la traduit en
/// marque de révision Word, que le relecteur accepte ou refuse. Une règle qui
/// relève d'un jugement éditorial (`VirguleAvantEt`) n'en produit aucune, et
/// se contente d'un commentaire.
/// </summary>
public sealed record SuggestedEdit(SuggestedEditKind Kind, int Offset, int Length, string Text)
{
    public static SuggestedEdit Delete(int offset, int length) =>
        new(SuggestedEditKind.Delete, offset, length, string.Empty);

    public static SuggestedEdit Insert(int offset, string text) =>
        new(SuggestedEditKind.Insert, offset, 0, text);

    public static SuggestedEdit Replace(int offset, int length, string text) =>
        new(SuggestedEditKind.Replace, offset, length, text);

    public static SuggestedEdit ParagraphStyle(string style) =>
        new(SuggestedEditKind.ParagraphStyle, 0, 0, style);

    public static SuggestedEdit RemovePageBreak() =>
        new(SuggestedEditKind.RemovePageBreak, 0, 0, string.Empty);
}

/// <summary>Ce qu'une passe de relecture a ajouté au document.</summary>
public readonly record struct ReviewResult(int Comments, int Revisions);
