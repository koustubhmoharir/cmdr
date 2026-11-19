using Terminal.Gui.Drawing;

namespace Cmdr.Tui;

/// <summary>
/// The colours of the key hints' click targets. A target is drawn as a button on the bar, in these fixed colours, so that it reads
/// the same on a dark terminal and on a light one. The rest of the bar keeps the theme's colours.
/// </summary>
internal static class HintColors
{
    /// <summary>The background of a key hint's click target, drawn as a button on the bar.</summary>
    internal static readonly Color TargetBackground = new(0x4C, 0x56, 0x6A);

    /// <summary>The text of a key hint's click target.</summary>
    internal static readonly Color TargetText = new(0xEC, 0xEF, 0xF4);
}
