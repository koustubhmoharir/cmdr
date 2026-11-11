using Cmdr.Core.Config;
using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>
/// The text of the main line of a command: the full line (exactly the requested width, or empty for width 0), the column of
/// the status glyph and the column of the name (-1 when that part is not visible).
/// </summary>
internal readonly record struct CommandMainText(string Text, int GlyphColumn, int NameColumn);

/// <summary>Pure text layout of the command list. Every result is exactly the requested width (or empty for width 0).</summary>
internal static class RowLayout
{
    /// <summary>Column of the status glyph in a command's main line: "  " + mark + " " + glyph.</summary>
    public const int GlyphColumn = 4;

    /// <summary>Column of the name in a command's main line, and the indent of its detail line.</summary>
    public const int NameColumn = 6;

    private const string Marked = "■";
    private const string Unmarked = " ";
    private const string NoValue = "—";
    private const string NotStartedText = "not started";

    /// <summary>
    /// Formats the main line of a command: <c>"  &lt;mark&gt; &lt;glyph&gt; &lt;name…&gt;"</c>. The name uses all remaining width and is
    /// truncated with an ellipsis only when it does not fit. Narrow widths drop the name first, then the glyph.
    /// </summary>
    public static CommandMainText FormatCommandMain(int width, bool marked, string glyph, string name)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentNullException.ThrowIfNull(name);

        if (width <= 0)
        {
            return new CommandMainText(string.Empty, -1, -1);
        }

        string prefix = "  " + (marked ? Marked : Unmarked) + " " + glyph + " ";
        int glyphColumn = width > GlyphColumn ? GlyphColumn : -1;
        if (width <= NameColumn)
        {
            return new CommandMainText(TextWidth.Fit(prefix, width), glyphColumn, -1);
        }

        string body = prefix + TextWidth.Fit(name, width - NameColumn);
        return new CommandMainText(body, glyphColumn, NameColumn);
    }

    /// <summary>
    /// Formats the detail line of a command: the start time and the detail text, separated by two spaces, indented by
    /// <paramref name="indent"/> columns. A command that was never started reads "not started". Exactly <paramref name="width"/> wide.
    /// </summary>
    public static string FormatCommandDetail(int width, int indent, StatusText status)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        int shift = Math.Clamp(indent, 0, width);
        int room = width - shift;
        string body = room > 0 ? TextWidth.Fit(DetailBody(status), room) : string.Empty;
        return new string(' ', shift) + body;
    }

    /// <summary>
    /// Formats a group header: <c>"▾ &lt;name&gt;  [parallel|sequential]"</c>, followed by <c>"  ▶ sequence running"</c> while a
    /// sequence runs. A collapsed header shows <c>"▸"</c> and <paramref name="summary"/> after the mode. Truncated and padded to
    /// <paramref name="width"/>.
    /// </summary>
    public static string FormatGroupRow(int width, string name, GroupMode mode, bool sequenceRunning, bool collapsed, string summary)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(summary);

        string modeText = mode == GroupMode.Sequential ? "sequential" : "parallel";
        // The summary of a collapsed header comes before the sequence marker, so that it is the part that stays visible when narrow.
        string text = (collapsed ? "▸ " : "▾ ") + name + "  [" + modeText + "]"
            + (collapsed && summary.Length > 0 ? "  " + summary : string.Empty)
            + (sequenceRunning ? "  ▶ sequence running" : string.Empty);
        return TextWidth.Fit(text, width);
    }

    /// <summary>The detail text without indent: the start time and detail when there are any, otherwise the kind's own text.</summary>
    private static string DetailBody(StatusText status)
    {
        if (status.Kind == StatusKind.Idle)
        {
            return NotStartedText;
        }

        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(status.Started) && status.Started != NoValue)
        {
            parts.Add(status.Started);
        }

        if (!string.IsNullOrEmpty(status.Detail))
        {
            parts.Add(status.Detail);
        }

        return string.Join("  ", parts);
    }
}
