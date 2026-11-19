using Terminal.Gui.Drawing;

namespace Shared.Tui;

/// <summary>
/// The fixed colours of the bars: the key hints on the last row (<see cref="HintsLine"/>) and the status bar of logvu. They are fixed
/// RGB values rather than the theme's roles, so the bars read the same on a dark terminal and on a light one.
/// </summary>
public static class BarColors
{
    /// <summary>The background of a bar.</summary>
    public static readonly Color Background = new(0x2E, 0x34, 0x40);

    /// <summary>The text of a bar.</summary>
    public static readonly Color Text = new(0xE5, 0xE9, 0xF0);

    /// <summary>The background of a key hint's click target, drawn as a button on the bar.</summary>
    public static readonly Color TargetBackground = new(0x4C, 0x56, 0x6A);

    /// <summary>The text of a key hint's click target.</summary>
    public static readonly Color TargetText = new(0xEC, 0xEF, 0xF4);
}
