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

    private static int Width(Rune rune)
    {
        if (Rune.IsControl(rune))
        {
            return 1;
        }

        return Math.Max(0, rune.GetColumns());
    }
}
