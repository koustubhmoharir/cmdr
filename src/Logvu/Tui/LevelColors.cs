using Logvu.Core.Parsing;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tui;

/// <summary>
/// The attributes of a timeline row by its level. Colors are foregrounds on the row's own background, so they work on light and dark
/// terminals. The level label is always drawn as text too, so the colors only add emphasis. Pure: no views or drivers involved.
/// </summary>
internal static class LevelColors
{
    /// <summary>The foreground of Warn labels.</summary>
    public static readonly Color Warn = new(ColorName16.Yellow);

    /// <summary>
    /// The foreground of Error labels, and of error text in the detail: a muted red (xterm 167). A named ANSI red is too glaring in
    /// common terminal themes, and its shade depends on the theme.
    /// </summary>
    public static readonly Color Error = new(0xD7, 0x5F, 0x5F);

    /// <summary>The foreground of Fatal labels, on <see cref="FatalBackground"/>.</summary>
    public static readonly Color FatalForeground = new(ColorName16.White);

    /// <summary>The background of Fatal labels: a darker muted red, under white text.</summary>
    public static readonly Color FatalBackground = new(0xAF, 0x3A, 0x3A);

    /// <summary>
    /// The attribute of a row's time, extra and message cells. Trace and Debug are dimmed. The selected row is drawn like the others: its
    /// cursor is the glyph in the gutter, not a change of colors.
    /// </summary>
    /// <param name="level">The row's level.</param>
    /// <param name="baseline">The attribute of the row, normal for a timeline row.</param>
    public static Attribute Text(LogLevel level, Attribute baseline) =>
        IsDimmed(level)
            ? new Attribute(baseline.Foreground, baseline.Background, baseline.Style | TextStyle.Faint)
            : baseline;

    /// <summary>
    /// The attribute of a row's message cell: the row's text attribute, in bold for Fatal rows.
    /// </summary>
    /// <param name="level">The row's level.</param>
    /// <param name="baseline">The attribute of the row, normal for a timeline row.</param>
    public static Attribute Message(LogLevel level, Attribute baseline)
    {
        if (level == LogLevel.Fatal)
        {
            return new Attribute(baseline.Foreground, baseline.Background, baseline.Style | TextStyle.Bold);
        }

        return Text(level, baseline);
    }

    /// <summary>
    /// The attribute of a row's level label. Warn and Error labels take their color. Fatal is white on red.
    /// </summary>
    /// <param name="level">The row's level.</param>
    /// <param name="baseline">The attribute of the row, normal for a timeline row.</param>
    public static Attribute Label(LogLevel level, Attribute baseline)
    {
        if (level == LogLevel.Fatal)
        {
            return new Attribute(FatalForeground, FatalBackground, TextStyle.Bold);
        }

        return level switch
        {
            LogLevel.Warn => new Attribute(Warn, baseline.Background, baseline.Style),
            LogLevel.Error => new Attribute(Error, baseline.Background, baseline.Style),
            _ when IsDimmed(level) => Text(level, baseline),
            _ => baseline,
        };
    }

    private static bool IsDimmed(LogLevel level) => level is LogLevel.Trace or LogLevel.Debug;
}
