using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace Scrubx.Cli;

/// <summary>
/// Correspondance entre le texte concaténé d'un paragraphe — celui sur lequel
/// raisonnent les règles de <see cref="DocxValidator"/> — et les éléments
/// `w:t` qui le portent réellement.
///
/// Word découpe le texte d'un paragraphe en `w:r` arbitrairement (correcteur
/// orthographique, mise en forme, rsid) : un offset dans le texte concaténé ne
/// dit donc pas à lui seul dans quel `w:t` on se trouve. Cette table fait le
/// pont, et constitue la définition unique du « texte d'un paragraphe » :
/// validateur et relecteur doivent passer par elle pour que les offsets
/// relevés par l'un soient interprétables par l'autre.
/// </summary>
public sealed class ParagraphTextMap
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>Un `w:t` et la plage qu'il occupe dans le texte du paragraphe.</summary>
    public readonly record struct Segment(XElement Text, int Start, int Length)
    {
        /// <summary>Offset suivant immédiatement le segment (exclu).</summary>
        public int End => Start + Length;
    }

    private ParagraphTextMap(string text, IReadOnlyList<Segment> segments)
    {
        Text = text;
        Segments = segments;
    }

    /// <summary>Texte concaténé du paragraphe.</summary>
    public string Text { get; }

    /// <summary>Segments non vides, dans l'ordre du document.</summary>
    public IReadOnlyList<Segment> Segments { get; }

    public static ParagraphTextMap Build(XElement paragraph)
    {
        var segments = new List<Segment>();
        var builder = new StringBuilder();

        foreach (var node in paragraph.Descendants(W + "t"))
        {
            var value = node.Value;
            if (value.Length == 0) continue;

            segments.Add(new Segment(node, builder.Length, value.Length));
            builder.Append(value);
        }

        return new ParagraphTextMap(builder.ToString(), segments);
    }

    /// <summary>Segment portant le caractère situé à <paramref name="offset"/>.</summary>
    public Segment? SegmentContaining(int offset)
    {
        foreach (var segment in Segments)
        {
            if (offset >= segment.Start && offset < segment.End) return segment;
        }
        return null;
    }
}
