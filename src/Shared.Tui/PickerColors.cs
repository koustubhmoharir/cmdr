using Terminal.Gui.Drawing;

namespace Shared.Tui;

/// <summary>
/// The fixed colours of the recent-configs picker's rows (<see cref="RecentRowsSource"/>). The number is faint on the normal foreground,
/// and the file name is in the normal foreground; these are the other parts. They are fixed RGB values rather than the theme's roles,
/// so the rows read the same on a dark terminal and on a light one.
/// </summary>
public static class PickerColors
{
    /// <summary>
    /// The directory. A muted grey (between the two backgrounds' extremes) that stays behind the file name on both kinds of terminal.
    /// </summary>
    public static readonly Color Directory = new(0x8A, 0x8F, 0x98);

    /// <summary>
    /// The name of the outermost repository folder in the directory: a blue accent that is clearly apart from the grey directory.
    /// </summary>
    public static readonly Color Repo = new(0x61, 0xAF, 0xEF);

    /// <summary>
    /// The <c>(not found)</c> column: the same muted red as logvu's and cfgedit's errors (xterm 167).
    /// </summary>
    public static readonly Color Missing = new(0xD7, 0x5F, 0x5F);
}
