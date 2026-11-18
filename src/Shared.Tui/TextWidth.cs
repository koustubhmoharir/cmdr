using System.Globalization;
using System.Text;
using Shared.Core;

namespace Shared.Tui;

/// <summary>A wrapped line of styled spans: its UTF-16 range <c>[Start, End)</c> in the cleaned plain text, and its spans.</summary>
public sealed record WrappedSpans<T>(int Start, int End, IReadOnlyList<(string Text, T Style)> Spans);

/// <summary>
/// Column-based text measuring and fitting. Text is measured and cut by grapheme cluster (see <see cref="Graphemes"/>), so a
/// base character with its combining marks, or an emoji sequence, is one unit that is never split. Widths come from
/// <see cref="DisplayWidth"/>, which the UI sets to Terminal.Gui's measure.
/// </summary>
public static class TextWidth
{
    private const char Ellipsis = '…';

    /// <summary>
    /// The grapheme clusters of <paramref name="text"/>, in order: the UTF-16 offset and length of each, and its display width
    /// (see <see cref="WidthOf"/>). Zero-width graphemes are included, with width 0.
    /// </summary>
    public static IEnumerable<(int Offset, int Length, int Width)> Graphemes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int offset = 0;
        while (offset < text.Length)
        {
            int length = StringInfo.GetNextTextElementLength(text.AsSpan(offset));
            yield return (offset, length, WidthOf(text.AsSpan(offset, length)));
            offset += length;
        }
    }

    /// <summary>
    /// The width of one grapheme: 1 when it starts with a control character (the control character is drawn as a space), and
    /// otherwise <see cref="DisplayWidth.OfGrapheme"/>. Zero-width graphemes give 0.
    /// </summary>
    public static int WidthOf(ReadOnlySpan<char> grapheme) =>
        !grapheme.IsEmpty && char.IsControl(grapheme[0]) ? 1 : DisplayWidth.OfGrapheme(grapheme);

    /// <summary>Number of terminal columns the text occupies, after <see cref="Clean"/>. Control characters count as one column each.</summary>
    public static int Columns(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return DisplayWidth.Of(Clean(text));
    }

    /// <summary>
    /// The longest prefix of <paramref name="text"/> that fits in <paramref name="width"/> columns. When the text is cut,
    /// the last column holds an ellipsis. The result is not padded. Zero-width graphemes before the cut are kept.
    /// </summary>
    public static string Truncate(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (width <= 0)
        {
            return string.Empty;
        }

        string clean = Clean(text);
        if (DisplayWidth.Of(clean) <= width)
        {
            return clean;
        }

        var sb = new StringBuilder();
        int used = 0;
        foreach ((int offset, int length, int w) in Graphemes(clean))
        {
            if (w == 0)
            {
                sb.Append(clean, offset, length);
                continue;
            }

            if (used + w > width - 1)
            {
                break;
            }

            sb.Append(clean, offset, length);
            used += w;
        }

        sb.Append(Ellipsis);
        return sb.ToString();
    }

    /// <summary>Truncates to <paramref name="width"/> columns and pads on the right, so the result is exactly that wide.</summary>
    public static string Fit(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        string truncated = Truncate(text, width);
        return truncated + new string(' ', Math.Max(0, width - Columns(truncated)));
    }

    /// <summary>Truncates to <paramref name="width"/> columns and pads on the left, so the result is exactly that wide.</summary>
    public static string FitRight(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        string truncated = Truncate(text, width);
        return new string(' ', Math.Max(0, width - Columns(truncated))) + truncated;
    }

    /// <summary>
    /// Wraps the text into lines of at most <paramref name="width"/> columns. A line breaks after the last space that fits, and
    /// spaces at a break are dropped; a token longer than the width is broken where it does not fit, never inside a grapheme.
    /// Control characters become spaces. Empty text gives one empty line; a width that is not positive gives no lines.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);

        var cells = Cells(Clean(text));
        return WrapRanges(cells, width).Select(range => Join(cells, range.Start, range.End)).ToList();
    }

    /// <summary>
    /// Wraps a line made of styled spans with the same breaks as <see cref="Wrap(string, int)"/>: the plain text of each
    /// returned line is what <see cref="Wrap(string, int)"/> gives for the concatenated text. A span may be split over lines,
    /// and adjacent characters with the same style form one span within a line. Non-positive widths give no lines.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(string Text, T Style)>> WrapSpans<T>(
        IReadOnlyList<(string Text, T Style)> spans,
        int width) =>
        WrapSpanRanges(spans, width).Select(line => line.Spans).ToList();

    /// <summary>
    /// The breaks of <see cref="WrapSpans{T}"/>, with the range of the cleaned plain text that each line shows: its UTF-16
    /// offsets <c>[Start, End)</c>. The spaces dropped at a break lie between one line's End and the next line's Start. A
    /// grapheme takes the style of its first UTF-16 unit.
    /// </summary>
    internal static List<WrappedSpans<T>> WrapSpanRanges<T>(IReadOnlyList<(string Text, T Style)> spans, int width)
    {
        ArgumentNullException.ThrowIfNull(spans);

        // The style of each UTF-16 unit of the plain text. Cleaning keeps the length, so the offsets still line up.
        string plain = string.Concat(spans.Select(span => span.Text));
        var styleAt = new T[plain.Length];
        int at = 0;
        foreach ((string text, T style) in spans)
        {
            Array.Fill(styleAt, style, at, text.Length);
            at += text.Length;
        }

        // Segment after cleaning, so a control character is a grapheme of its own (a space).
        string cleaned = Clean(plain);
        var cells = new List<(string Text, int Width)>();
        var styles = new List<T>();
        var offsets = new List<int>(); // The UTF-16 offset of each cell, then the end of the text.
        foreach ((int offset, int length, int cellWidth) in Graphemes(cleaned))
        {
            cells.Add((cleaned.Substring(offset, length), cellWidth));
            styles.Add(styleAt[offset]);
            offsets.Add(offset);
        }

        offsets.Add(cleaned.Length);

        var lines = new List<WrappedSpans<T>>();
        foreach ((int start, int end) in WrapRanges(cells, width))
        {
            var line = new List<(string Text, T Style)>();
            int runStart = start;
            for (int i = start + 1; i <= end; i++)
            {
                if (i == end || !EqualityComparer<T>.Default.Equals(styles[i], styles[runStart]))
                {
                    line.Add((Join(cells, runStart, i), styles[runStart]));
                    runStart = i;
                }
            }

            lines.Add(new WrappedSpans<T>(offsets[start], offsets[end], line));
        }

        return lines;
    }

    /// <summary>
    /// The breaking rules of <see cref="Wrap(string, int)"/>, on cells (one per grapheme: its text and its width). Each line is the
    /// range of cells it holds, <c>[Start, End)</c>; the cells that fall between one line's end and the next line's start are
    /// the spaces dropped at a break. Empty cells give one empty range; a width that is not positive gives none.
    /// </summary>
    public static List<(int Start, int End)> WrapRanges(IReadOnlyList<(string Text, int Width)> cells, int width)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var ranges = new List<(int Start, int End)>();
        if (width <= 0)
        {
            return ranges;
        }

        // The current line is cells[start, i); used is its width.
        int start = 0;
        int used = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            string s = cells[i].Text;
            int w = cells[i].Width;
            while (used + w > width && i > start)
            {
                if (s == " ")
                {
                    // A space that does not fit ends the line; the space itself is dropped.
                    ranges.Add((start, TrimmedEnd(cells, start, i)));
                    start = i;
                    used = 0;
                    break;
                }

                int space = LastSpace(cells, start, i);
                int cut = space < 0 ? start : TrimmedEnd(cells, start, space);
                if (cut > start)
                {
                    // Break after the last space; the partial word after it starts the next line.
                    ranges.Add((start, cut));
                    start = space + 1;
                    while (start < i && cells[start].Text == " ")
                    {
                        start++;
                    }
                }
                else
                {
                    ranges.Add((start, i));
                    start = i;
                }

                used = Sum(cells, start, i);
            }

            // Spaces at the start of a wrapped line are dropped.
            if (s == " " && start == i && ranges.Count > 0)
            {
                start = i + 1;
                continue;
            }

            used += w;
        }

        if (start < cells.Count || ranges.Count == 0)
        {
            ranges.Add((start, cells.Count));
        }

        return ranges;
    }

    /// <summary>The cells of cleaned text: one per grapheme, with its text and width.</summary>
    private static List<(string Text, int Width)> Cells(string cleaned) =>
        Graphemes(cleaned).Select(g => (cleaned.Substring(g.Offset, g.Length), g.Width)).ToList();

    private static string Join(IReadOnlyList<(string Text, int Width)> cells, int start, int end)
    {
        var sb = new StringBuilder();
        for (int i = start; i < end; i++)
        {
            sb.Append(cells[i].Text);
        }

        return sb.ToString();
    }

    private static int TrimmedEnd(IReadOnlyList<(string Text, int Width)> cells, int start, int end)
    {
        while (end > start && cells[end - 1].Text == " ")
        {
            end--;
        }

        return end;
    }

    private static int LastSpace(IReadOnlyList<(string Text, int Width)> cells, int start, int end)
    {
        for (int i = end - 1; i >= start; i--)
        {
            if (cells[i].Text == " ")
            {
                return i;
            }
        }

        return -1;
    }

    private static int Sum(IReadOnlyList<(string Text, int Width)> cells, int start, int end)
    {
        int total = 0;
        for (int i = start; i < end; i++)
        {
            total += cells[i].Width;
        }

        return total;
    }

    /// <summary>Replaces control characters with spaces, so they cannot disturb the layout. Keeps the UTF-16 length.</summary>
    public static string Clean(string text)
    {
        if (text.All(c => !char.IsControl(c)))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            sb.Append(Rune.IsControl(rune) ? " " : rune.ToString());
        }

        return sb.ToString();
    }
}
