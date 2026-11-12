using Cmdr.Core.Config;
using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>
/// The text of the main line of a command: the full line (exactly the requested width, or empty for width 0), the column of
/// the status glyph and the column of the name (-1 when that part is not visible).
/// </summary>
internal readonly record struct CommandMainText(string Text, int GlyphColumn, int NameColumn);

/// <summary>
/// The text of a group header: the full line (exactly the requested width, or empty for width 0), and the column and text of
/// the source label at its end (<c>SourceColumn</c> is -1 and <c>SourceText</c> empty when no label is shown).
/// </summary>
internal readonly record struct GroupHeaderText(string Text, int SourceColumn, string SourceText);

/// <summary>Pure text layout of the command list. Every result is exactly the requested width (or empty for width 0).</summary>
internal static class RowLayout
{
    /// <summary>
    /// Column of the selection mark in a command's main line. It is always the extreme left column, whatever the depth of the
    /// command's group.
    /// </summary>
    public const int MarkColumn = 0;

    /// <summary>
    /// Column of the status glyph in a command's main line, relative to the indent of the command's group: mark + spaces + glyph,
    /// with the mark at <see cref="MarkColumn"/>. It is the same column as the ▾/▸ of a group header one depth deeper (a sibling
    /// of the command).
    /// </summary>
    public const int GlyphColumn = 2;

    /// <summary>
    /// Column of the name in a command's main line, relative to the indent of the command's group, and the offset of its detail
    /// line. It is the same column as the name of a group header one depth deeper.
    /// </summary>
    public const int NameColumn = 4;

    private const string Marked = "→";
    private const string Unmarked = " ";
    private const string NoValue = "—";
    private const string NotStartedText = "not started";

    /// <summary>Columns of indentation per nesting depth. Depth 0 (a top-level group) has none.</summary>
    public const int IndentPerDepth = 2;

    /// <summary>The indentation in columns of an item at the given depth.</summary>
    public static int IndentOf(int depth) => IndentPerDepth * Math.Max(0, depth);

    /// <summary>Column of the name in a command's detail line, for a command whose group is at <paramref name="depth"/>.</summary>
    public static int CommandDetailIndent(int depth) => NameColumn + IndentOf(depth);

    /// <summary>
    /// Formats the main line of a command: <c>"&lt;mark&gt;&lt;indent&gt; &lt;glyph&gt; &lt;name…&gt;"</c>, where the mark is "→" when marked
    /// and " " otherwise, always at <see cref="MarkColumn"/> (column 0), and the indent is <see cref="IndentOf"/> the depth of the
    /// command's group. The name uses all remaining width and is truncated with an ellipsis only when it does not fit. Narrow
    /// widths drop the name first, then the glyph; a width that does not pass the indent gives a blank line. The returned columns
    /// are relative to the start of the line.
    /// </summary>
    public static CommandMainText FormatCommandMain(int width, bool marked, string glyph, string name, int depth = 0)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentNullException.ThrowIfNull(name);

        if (width <= 0)
        {
            return new CommandMainText(string.Empty, -1, -1);
        }

        int shift = IndentOf(depth);
        int glyphColumn = GlyphColumn + shift;
        int nameColumn = NameColumn + shift;
        string prefix = (marked ? Marked : Unmarked) + new string(' ', shift + 1) + glyph + " ";
        if (width <= nameColumn)
        {
            // Name hidden. When even the indent does not fit, the line is blank; otherwise it is the prefix (mark included), cut to the width.
            string head = width <= shift ? new string(' ', width) : TextWidth.Fit(prefix, width);
            return new CommandMainText(head, width > glyphColumn ? glyphColumn : -1, -1);
        }

        string body = prefix + TextWidth.Fit(name, width - nameColumn);
        return new CommandMainText(body, glyphColumn, nameColumn);
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
    /// sequence runs. A collapsed header shows <c>"▸"</c> and <paramref name="summary"/> after the mode. The header is indented by
    /// <see cref="IndentOf"/> <paramref name="depth"/>. Truncated and padded to <paramref name="width"/>.
    /// </summary>
    public static string FormatGroupRow(int width, string name, GroupMode mode, bool sequenceRunning, bool collapsed, string summary, int depth = 0) =>
        FormatGroupHeader(width, name, mode, sequenceRunning, collapsed, summary, depth).Text;

    /// <summary>
    /// Formats a group header as <see cref="FormatGroupRow"/> does, with <paramref name="sourceLabel"/> (the file a base group comes
    /// from) after it, separated by two spaces. The label is dropped when the whole header with the label does not fit, so the
    /// header itself is never truncated for it. Null or empty means no label.
    /// </summary>
    public static GroupHeaderText FormatGroupHeader(
        int width,
        string name,
        GroupMode mode,
        bool sequenceRunning,
        bool collapsed,
        string summary,
        int depth = 0,
        string? sourceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(summary);

        string modeText = mode == GroupMode.Sequential ? "sequential" : "parallel";
        // The summary of a collapsed header comes before the sequence marker, so that it is the part that stays visible when narrow.
        string text = (collapsed ? "▸ " : "▾ ") + name + "  [" + modeText + "]"
            + (collapsed && summary.Length > 0 ? "  " + summary : string.Empty)
            + (sequenceRunning ? "  ▶ sequence running" : string.Empty);

        int shift = IndentOf(depth);
        if (width <= shift)
        {
            return new GroupHeaderText(new string(' ', Math.Max(width, 0)), -1, string.Empty);
        }

        int room = width - shift;
        if (!string.IsNullOrEmpty(sourceLabel))
        {
            string withSource = text + "  " + sourceLabel;
            if (TextWidth.Columns(withSource) <= room)
            {
                return new GroupHeaderText(
                    new string(' ', shift) + TextWidth.Fit(withSource, room),
                    shift + TextWidth.Columns(text) + 2,
                    sourceLabel);
            }
        }

        return new GroupHeaderText(new string(' ', shift) + TextWidth.Fit(text, room), -1, string.Empty);
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
