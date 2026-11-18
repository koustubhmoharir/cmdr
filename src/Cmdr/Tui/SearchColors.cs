using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// The colors of search highlights. They are 16-color names, so they follow the terminal theme. The active match stands out from the
/// other matches by its color. Both keep the style of the attribute they replace, except Reverse, which would hide the highlight.
/// </summary>
internal static class SearchColors
{
    /// <summary>
    /// A cell of a match that is not the active one: black on bright yellow, keeping the style of <paramref name="attr"/> without Reverse.
    /// </summary>
    public static Attribute Match(Attribute attr) =>
        new(ColorName16.Black, ColorName16.BrightYellow, WithoutReverse(attr));

    /// <summary>
    /// A cell of the active match: black on bright cyan, keeping the style of <paramref name="attr"/> without Reverse.
    /// </summary>
    public static Attribute Active(Attribute attr) =>
        new(ColorName16.Black, ColorName16.BrightCyan, WithoutReverse(attr));

    private static TextStyle WithoutReverse(Attribute attr) => attr.Style & ~TextStyle.Reverse;
}
