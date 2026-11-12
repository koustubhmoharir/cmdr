using Cmdr.Core.Session;
using Terminal.Gui.Drawing;

namespace Cmdr.Tui;

/// <summary>
/// Colors of the status glyphs. A coloured status has a bright variant, which reads on dark backgrounds, and a dark variant,
/// which reads on light ones. <see cref="For"/> picks whichever contrasts more with the background of the row.
/// </summary>
internal static class StatusColors
{
    private static readonly Color BrightWarning = new(0xE5, 0xA5, 0x0A);
    private static readonly Color DarkGreen = new(0x10, 0x7C, 0x10);
    private static readonly Color DarkOlive = new(0x6B, 0x60, 0x00);
    private static readonly Color DarkRed = new(0xC5, 0x0F, 0x1F);
    private static readonly Color DarkAmber = new(0x8A, 0x5A, 0x00);
    private static readonly Color DarkCyan = new(0x0F, 0x6B, 0x73);

    /// <summary>
    /// The glyph color for <paramref name="kind"/> on <paramref name="background"/>. Kinds without a color keep
    /// <paramref name="normalForeground"/>. When the background is the terminal default (<see cref="Color.None"/>, or an
    /// unset color) the contrast cannot be computed, so the bright variant is used. Otherwise the variant with the higher
    /// <see cref="ContrastRatio"/> wins, and a tie goes to the bright variant.
    /// </summary>
    public static Color For(StatusKind kind, Color background, Color normalForeground)
    {
        if (!TryGetVariants(kind, out Color bright, out Color dark))
        {
            return normalForeground;
        }

        if (background.A == 0)
        {
            return bright;
        }

        return ContrastRatio(dark, background) > ContrastRatio(bright, background) ? dark : bright;
    }

    /// <summary>
    /// The WCAG 2.x contrast ratio of two colors, from 1 (no contrast) to 21 (black on white). The alpha channel is ignored.
    /// </summary>
    public static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>The bright and dark variants of a kind, or false when the kind has no color.</summary>
    private static bool TryGetVariants(StatusKind kind, out Color bright, out Color dark)
    {
        switch (kind)
        {
            case StatusKind.Running:
            case StatusKind.Success:
                bright = new Color(ColorName16.BrightGreen);
                dark = DarkGreen;
                return true;

            case StatusKind.Stopping:
                bright = new Color(ColorName16.BrightYellow);
                dark = DarkOlive;
                return true;

            case StatusKind.Failure:
                bright = new Color(ColorName16.BrightRed);
                dark = DarkRed;
                return true;

            case StatusKind.Warning:
                bright = BrightWarning;
                dark = DarkAmber;
                return true;

            case StatusKind.Queued:
                bright = new Color(ColorName16.BrightCyan);
                dark = DarkCyan;
                return true;

            default:
                bright = default;
                dark = default;
                return false;
        }
    }

    /// <summary>WCAG relative luminance of the sRGB channels of <paramref name="color"/>.</summary>
    private static double RelativeLuminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(int channel)
    {
        double c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
