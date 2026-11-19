using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tui;

/// <summary>The look of selected text, shared by the timeline and the detail. Pure.</summary>
internal static class SelectionColors
{
    /// <summary>
    /// The attribute of a selected cell: the cell's own attribute in reverse video (toggled, so a reversed cell reads normally). Reverse video
    /// rather than swapped colors, which do nothing when a color is the terminal's default.
    /// </summary>
    /// <param name="attr">The cell's attribute.</param>
    public static Attribute Invert(Attribute attr) => new(
        attr.Foreground,
        attr.Background,
        attr.Style.HasFlag(TextStyle.Reverse) ? attr.Style & ~TextStyle.Reverse : attr.Style | TextStyle.Reverse);
}
