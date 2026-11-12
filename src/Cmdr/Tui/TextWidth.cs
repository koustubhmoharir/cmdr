using System.Text;
using Terminal.Gui.Text;

namespace Cmdr.Tui;

/// <summary>Column-based text measuring and fitting. Widths follow Terminal.Gui's rune widths, so wide characters count as two columns.</summary>
internal static class TextWidth
{
    private const char Ellipsis = '…';

    /// <summary>Number of terminal columns the text occupies. Control characters count as one column each.</summary>
    public static int Columns(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int total = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            total += Width(rune);
        }

        return total;
    }

    /// <summary>
    /// The longest prefix of <paramref name="text"/> that fits in <paramref name="width"/> columns. When the text is cut,
    /// the last column holds an ellipsis. The result is not padded.
    /// </summary>
    public static string Truncate(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (width <= 0)
        {
            return string.Empty;
        }

        string clean = Clean(text);
        if (Columns(clean) <= width)
        {
            return clean;
        }

        var sb = new StringBuilder();
        int used = 0;
        foreach (Rune rune in clean.EnumerateRunes())
        {
            int w = Width(rune);
            if (w == 0)
            {
                continue;
            }

            if (used + w > width - 1)
            {
                break;
            }

            sb.Append(rune.ToString());
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
    /// spaces at a break are dropped; a token longer than the width is broken where it does not fit. Control characters become
    /// spaces. Empty text gives one empty line; a width that is not positive gives no lines.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<string>();
        if (width <= 0)
        {
            return lines;
        }

        var line = new List<(string Text, int Width)>();
        int used = 0;
        foreach (Rune rune in Clean(text).EnumerateRunes())
        {
            string s = rune.ToString();
            int w = Width(rune);
            while (used + w > width && line.Count > 0)
            {
                if (s == " ")
                {
                    // A space that does not fit ends the line; the space itself is dropped.
                    lines.Add(JoinLine(line, TrimmedCount(line, line.Count)));
                    line.Clear();
                    used = 0;
                    break;
                }

                int space = line.FindLastIndex(c => c.Text == " ");
                int end = space < 0 ? 0 : TrimmedCount(line, space);
                if (end > 0)
                {
                    // Break after the last space; the partial word after it starts the next line.
                    lines.Add(JoinLine(line, end));
                    line.RemoveRange(0, space + 1);
                    while (line.Count > 0 && line[0].Text == " ")
                    {
                        line.RemoveAt(0);
                    }
                }
                else
                {
                    lines.Add(JoinLine(line, line.Count));
                    line.Clear();
                }

                used = line.Sum(c => c.Width);
            }

            // Spaces at the start of a wrapped line are dropped.
            if (s == " " && line.Count == 0 && lines.Count > 0)
            {
                continue;
            }

            line.Add((s, w));
            used += w;
        }

        if (line.Count > 0 || lines.Count == 0)
        {
            lines.Add(JoinLine(line, line.Count));
        }

        return lines;
    }

    private static string JoinLine(List<(string Text, int Width)> line, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            sb.Append(line[i].Text);
        }

        return sb.ToString();
    }

    private static int TrimmedCount(List<(string Text, int Width)> line, int count)
    {
        while (count > 0 && line[count - 1].Text == " ")
        {
            count--;
        }

        return count;
    }

    /// <summary>Replaces control characters with spaces, so they cannot disturb the layout.</summary>
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

    internal static int Width(Rune rune)
    {
        if (Rune.IsControl(rune))
        {
            return 1;
        }

        return Math.Max(0, rune.GetColumns());
    }
}
