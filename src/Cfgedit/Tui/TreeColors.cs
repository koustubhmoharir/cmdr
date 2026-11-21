using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cfgedit.Tui;

/// <summary>
/// The look of the tree: each style of a segment as a Terminal.Gui attribute, the background tint of the cursor row, and the
/// reverse video of the focused part. The dimmed styles are faint. Pure: the terminal's defaults are passed in.
/// </summary>
/// <remarks>
/// The tint follows logvu's row stripe: a share of the way from the background toward the foreground, computed in integers, from
/// the terminal's default colors when it reported them, else the view's normal colors when they are explicit, else a fixed dark
/// shade that reads well on the usual dark terminals.
/// </remarks>
internal static class TreeColors
{
    /// <summary>How far the cursor row's tint is from the background toward the foreground, in percent.</summary>
    public const int TintPercent = 15;

    /// <summary>The tint used when neither the terminal's defaults nor explicit normal colors are known.</summary>
    public static readonly Color FallbackTint = new(0x26, 0x2A, 0x33);

    /// <summary>A JSON string, and the value of an XML attribute.</summary>
    private static readonly Color StringValue = new(0x98, 0xC3, 0x79);

    /// <summary>A JSON number.</summary>
    private static readonly Color NumberValue = new(0xC6, 0x78, 0xDD);

    /// <summary>
    /// The background of the cursor row: the tint of the terminal's default colors when it reported them, else of the view's normal
    /// colors when they are explicit, else <see cref="FallbackTint"/>.
    /// </summary>
    /// <param name="normal">The view's normal attribute.</param>
    /// <param name="terminalDefaults">The terminal's default attribute, or null when it has not reported one.</param>
    public static Color CursorRowBackground(Attribute normal, Attribute? terminalDefaults)
    {
        if (terminalDefaults is { } defaults && IsExplicit(defaults))
        {
            return Blend(defaults);
        }

        return IsExplicit(normal) ? Blend(normal) : FallbackTint;
    }

    /// <summary>
    /// The attribute of a segment with the given style, on the given background. The foreground is the style's color, or the
    /// normal foreground for the styles that have none.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <param name="normal">The view's normal attribute.</param>
    /// <param name="background">The background of the cell.</param>
    public static Attribute For(StyleKind style, Attribute normal, Color background)
    {
        Color fg = normal.Foreground;
        return style switch
        {
            StyleKind.Key => new Attribute(ColorName16.BrightBlue, background, TextStyle.None),
            StyleKind.Index => new Attribute(fg, background, TextStyle.Faint),
            StyleKind.String => new Attribute(StringValue, background, TextStyle.None),
            StyleKind.Number => new Attribute(NumberValue, background, TextStyle.None),
            StyleKind.Bool => new Attribute(ColorName16.Yellow, background, TextStyle.None),
            StyleKind.Null => new Attribute(fg, background, TextStyle.Italic),
            StyleKind.Summary => new Attribute(fg, background, TextStyle.Faint),
            StyleKind.Punctuation => new Attribute(fg, background, TextStyle.Faint),
            StyleKind.Comment => new Attribute(fg, background, TextStyle.Faint),
            StyleKind.ElementName => new Attribute(ColorName16.Cyan, background, TextStyle.None),
            StyleKind.AttributeName => new Attribute(ColorName16.Yellow, background, TextStyle.None),
            StyleKind.AttributeValue => new Attribute(StringValue, background, TextStyle.None),
            StyleKind.ReadOnly => new Attribute(fg, background, TextStyle.Faint),
            StyleKind.Glyph => new Attribute(fg, background, TextStyle.Faint),
            StyleKind.RawMarker => new Attribute(fg, background, TextStyle.Faint | TextStyle.Italic),
            _ => new Attribute(fg, background, TextStyle.None),
        };
    }

    /// <summary>The attribute of the focused part: the same colors, in reverse video.</summary>
    /// <param name="attr">The attribute the part would have.</param>
    public static Attribute Focused(Attribute attr) =>
        new(attr.Foreground, attr.Background, attr.Style | TextStyle.Reverse);

    private static bool IsExplicit(Attribute attr) => attr.Foreground.A != 0 && attr.Background.A != 0;

    private static Color Blend(Attribute attr) => new(
        Mix(attr.Background.R, attr.Foreground.R),
        Mix(attr.Background.G, attr.Foreground.G),
        Mix(attr.Background.B, attr.Foreground.B));

    /// <summary>The channel <see cref="TintPercent"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static int Mix(int from, int to) => from + (to - from) * TintPercent / 100;
}
