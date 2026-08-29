using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Scrubx.Cli;

/// <summary>
/// Levée quand la structure d'un paragraphe ne permet pas d'y écrire une révision
/// (texte porté par autre chose qu'un <c>w:r</c>, par exemple).
/// </summary>
public class ParagraphStructureException : Exception
{
    public ParagraphStructureException(string message) : base(message) { }
}

/// <summary>
/// Fait le lien entre le texte concaténé d'un paragraphe — celui sur lequel raisonnent
/// les règles de <see cref="DocxValidator"/> — et les <c>w:t</c> qui le portent, et sait
/// découper les runs pour qu'une plage de caractères corresponde à des runs entiers.
/// </summary>
/// <remarks>
/// L'ordre de parcours doit rester identique à celui du validateur
/// (<c>p.Descendants(w:t)</c>) : les offsets des règles en dépendent.
/// </remarks>
public sealed class ParagraphTextMap
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly List<Segment> _segments = new();

    public ParagraphTextMap(XElement paragraph)
    {
        Paragraph = paragraph;
        Text = string.Empty;
        Build();
    }

    public XElement Paragraph { get; }

    /// <summary>Texte concaténé du paragraphe, tel que vu par les règles.</summary>
    public string Text { get; private set; }

    private readonly record struct Segment(XElement TextElement, int Start, int Length)
    {
        public int End => Start + Length;
    }

    private void Build()
    {
        _segments.Clear();
        int offset = 0;
        foreach (var t in Paragraph.Descendants(W + "t"))
        {
            var length = t.Value.Length;
            _segments.Add(new Segment(t, offset, length));
            offset += length;
        }
        Text = string.Concat(_segments.Select(s => s.TextElement.Value));
    }

    /// <summary>
    /// Découpe les runs pour que <c>[start, start + length)</c> corresponde exactement à une
    /// suite de runs entiers, et renvoie ces runs dans l'ordre du document.
    /// </summary>
    public IReadOnlyList<XElement> SplitRange(int start, int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (start < 0 || start + length > Text.Length) throw new ArgumentOutOfRangeException(nameof(start));

        // De droite à gauche : le découpage à la fin ne déplace pas l'offset de début.
        SplitAt(start + length);
        SplitAt(start);

        var runs = new List<XElement>();
        foreach (var segment in _segments)
        {
            if (segment.Length == 0 || segment.Start < start || segment.End > start + length) continue;
            var run = RunOf(segment.TextElement);
            if (!runs.Contains(run)) runs.Add(run);
        }
        return runs;
    }

    /// <summary>
    /// Découpe les runs à l'offset donné et renvoie le nœud devant lequel une insertion doit
    /// être placée, ou <c>null</c> si elle doit être ajoutée à la fin du paragraphe.
    /// </summary>
    public XElement? SplitForInsertion(int offset)
    {
        if (offset < 0 || offset > Text.Length) throw new ArgumentOutOfRangeException(nameof(offset));

        SplitAt(offset);

        foreach (var segment in _segments)
        {
            if (segment.Length > 0 && segment.Start >= offset) return RunOf(segment.TextElement);
        }
        return null;
    }

    /// <summary>Garantit qu'aucun run ne chevauche l'offset donné.</summary>
    private void SplitAt(int offset)
    {
        foreach (var segment in _segments)
        {
            if (offset > segment.Start && offset < segment.End)
            {
                SplitRunInside(segment.TextElement, offset - segment.Start);
                Build();
                return;
            }
        }

        // L'offset tombe déjà sur une frontière de w:t : il reste à s'assurer qu'aucun run
        // ne porte du contenu des deux côtés de cette frontière.
        bool changed = false;
        foreach (var segment in _segments.ToList())
        {
            if (segment.Length == 0) continue;
            if (segment.Start == offset) changed |= SplitRunBefore(segment.TextElement);
            else if (segment.End == offset) changed |= SplitRunAfter(segment.TextElement);
        }
        if (changed) Build();
    }

    /// <summary>Coupe le run portant <paramref name="t"/> en deux à l'intérieur du texte.</summary>
    private static void SplitRunInside(XElement t, int charOffset)
    {
        var run = RunOf(t);
        var value = t.Value;

        var newRun = new XElement(run.Name, run.Attributes());
        var rPr = run.Element(W + "rPr");
        if (rPr != null) newRun.Add(new XElement(rPr));

        newRun.Add(MakeText(value[charOffset..]));

        // Tout ce qui suit le w:t coupé (tabulations, sauts de ligne…) part avec la moitié droite.
        foreach (var following in t.ElementsAfterSelf().ToList())
        {
            following.Remove();
            newRun.Add(following);
        }

        t.Value = value[..charOffset];
        t.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        run.AddAfterSelf(newRun);
    }

    /// <summary>
    /// Coupe le run juste avant <paramref name="t"/> si celui-ci n'est pas déjà en tête de run.
    /// Renvoie <c>true</c> si une coupe a eu lieu.
    /// </summary>
    private static bool SplitRunBefore(XElement t)
    {
        var run = RunOf(t);
        if (t.ElementsBeforeSelf().All(e => e.Name == W + "rPr")) return false;

        var newRun = new XElement(run.Name, run.Attributes());
        var rPr = run.Element(W + "rPr");
        if (rPr != null) newRun.Add(new XElement(rPr));

        foreach (var moved in t.ElementsAfterSelf().Prepend(t).ToList())
        {
            moved.Remove();
            newRun.Add(moved);
        }

        run.AddAfterSelf(newRun);
        return true;
    }

    /// <summary>
    /// Déplace dans un nouveau run ce qui suit <paramref name="t"/> à l'intérieur de son run
    /// (tabulation, saut de ligne…). Renvoie <c>true</c> si une coupe a eu lieu.
    /// </summary>
    private static bool SplitRunAfter(XElement t)
    {
        var run = RunOf(t);
        var following = t.ElementsAfterSelf().ToList();
        if (following.Count == 0) return false;

        var newRun = new XElement(run.Name, run.Attributes());
        var rPr = run.Element(W + "rPr");
        if (rPr != null) newRun.Add(new XElement(rPr));

        foreach (var moved in following)
        {
            moved.Remove();
            newRun.Add(moved);
        }

        run.AddAfterSelf(newRun);
        return true;
    }

    internal static XElement MakeText(string value)
    {
        var t = new XElement(W + "t", value);
        t.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        return t;
    }

    private static XElement RunOf(XElement t)
    {
        var run = t.Parent;
        if (run == null || run.Name != W + "r")
        {
            throw new ParagraphStructureException(
                $"Texte porté par un élément inattendu ('{run?.Name.LocalName ?? "?"}') : révision impossible dans ce paragraphe.");
        }
        return run;
    }
}
