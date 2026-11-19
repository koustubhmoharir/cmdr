using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tui;

/// <summary>
/// The background of the alternate rows of the timeline: a subtle shade of the normal background, toward the normal foreground. Pure: no
/// views or drivers involved, the terminal's defaults are passed in.
/// </summary>
/// <remarks>
/// <para>
/// The shade is <see cref="Percent"/> of the way from the background toward the foreground, computed in integer arithmetic as cmdr's cursor
/// shade is. A light theme's stripe is therefore a little darker than its background, and a dark theme's a little lighter.
/// </para>
/// <para>
/// The colors the shade is taken from are, in order: the terminal's default colors when the terminal reported them (the driver's
/// <c>DefaultAttribute</c>, from the OSC 10/11 query, which is null until a reply arrives or when there is none); otherwise the view's
/// normal colors, when they are explicit; otherwise <see cref="FallbackShade"/>. The normal colors are the default (alpha 0) when the view
/// uses the terminal's own colors, which is why they cannot be blended without the terminal's defaults. The fallback is a fixed dark shade,
/// which reads well on the usual dark terminals.
/// </para>
/// </remarks>
internal static class RowStripe
{
    /// <summary>How far the shade is from the background toward the foreground, in percent.</summary>
    public const int Percent = 6;

    /// <summary>The shade used when neither the terminal's defaults nor explicit normal colors are known.</summary>
    public static readonly Color FallbackShade = new(0x1A, 0x1C, 0x22);

    /// <summary>
    /// The stripe color for a view whose normal attribute is <paramref name="normal"/>, given the terminal's default attribute, or <c>null</c>
    /// when it is not known.
    /// </summary>
    /// <param name="normal">The view's normal attribute.</param>
    /// <param name="terminalDefaults">The terminal's default attribute, when it reported one.</param>
    /// <returns>The stripe background.</returns>
    public static Color For(Attribute normal, Attribute? terminalDefaults)
    {
        if (terminalDefaults is { } defaults && IsExplicit(defaults))
        {
            return Blend(defaults);
        }

        if (IsExplicit(normal))
        {
            return Blend(normal);
        }

        return FallbackShade;
    }

    /// <summary>
    /// The attribute of a cell on a striped row: when its background is the normal background, the stripe replaces it; a cell with its own
    /// background (a match, a level label, a selection) keeps it. The foreground and the style are kept.
    /// </summary>
    /// <param name="attr">The cell's attribute.</param>
    /// <param name="normalBackground">The view's normal background.</param>
    /// <param name="stripe">The stripe background.</param>
    public static Attribute Apply(Attribute attr, Color normalBackground, Color stripe) =>
        attr.Background.Equals(normalBackground) ? new Attribute(attr.Foreground, stripe, attr.Style) : attr;

    private static bool IsExplicit(Attribute attr) => attr.Foreground.A != 0 && attr.Background.A != 0;

    private static Color Blend(Attribute attr) => new(
        Mix(attr.Background.R, attr.Foreground.R),
        Mix(attr.Background.G, attr.Foreground.G),
        Mix(attr.Background.B, attr.Foreground.B));

    /// <summary>The channel <see cref="Percent"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static int Mix(int from, int to) => from + (to - from) * Percent / 100;
}
