using Cmdr.Core.Config;
using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>The text of a command row and the column of its status glyph (-1 when the glyph is not visible).</summary>
internal readonly record struct CommandRowText(string Text, int GlyphColumn);

/// <summary>Pure text layout of the command list. Every result is exactly the requested width (or empty for width 0).</summary>
internal static class RowLayout
{
    /// <summary>Column of the status glyph in a command row: "  " + mark + " " + glyph.</summary>
    public const int GlyphColumn = 4;

    private const int PrefixWidth = 6;
    private const int StartedWidth = 8;
    private const int DetailMaxWidth = 24;
    private const int NameMinWidth = 8;
    private const string Marked = "■";
    private const string Unmarked = " ";

    /// <summary>
    /// Formats a command row: <c>"  &lt;mark&gt; &lt;glyph&gt; &lt;name…&gt; &lt;started&gt; &lt;detail&gt;"</c>. The name is truncated with an
    /// ellipsis; the detail is right-aligned at the end of the row. When there is no room for the time columns, only the name is shown.
    /// </summary>
    public static CommandRowText FormatCommandRow(int width, bool marked, StatusText status, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (width <= 0)
        {
            return new CommandRowText(string.Empty, -1);
        }

        string prefix = "  " + (marked ? Marked : Unmarked) + " " + status.Glyph + " ";
        if (width < PrefixWidth)
        {
            int glyphColumn = width > GlyphColumn ? GlyphColumn : -1;
            return new CommandRowText(TextWidth.Fit(prefix, width), glyphColumn);
        }

        int avail = width - PrefixWidth;
        string body;
        if (avail < 1 + StartedWidth + 1 + NameMinWidth)
        {
            body = TextWidth.Fit(name, avail);
        }
        else
        {
            int detailWidth = Math.Min(DetailMaxWidth, avail - 1 - StartedWidth - 1 - NameMinWidth);
            int nameWidth = avail - 1 - StartedWidth - 1 - detailWidth;
            body = TextWidth.Fit(name, nameWidth)
                + " " + TextWidth.Fit(status.Started, StartedWidth)
                + " " + TextWidth.FitRight(status.Detail, detailWidth);
        }

        return new CommandRowText(prefix + body, GlyphColumn);
    }

    /// <summary>
    /// Formats a group header: <c>"▾ &lt;name&gt;  [parallel|sequential]"</c>, followed by <c>"  ▶ sequence running"</c> while a
    /// sequence runs. Truncated and padded to <paramref name="width"/>.
    /// </summary>
    public static string FormatGroupRow(int width, string name, GroupMode mode, bool sequenceRunning)
    {
        ArgumentNullException.ThrowIfNull(name);

        string modeText = mode == GroupMode.Sequential ? "sequential" : "parallel";
        string text = "▾ " + name + "  [" + modeText + "]" + (sequenceRunning ? "  ▶ sequence running" : string.Empty);
        return TextWidth.Fit(text, width);
    }
}
