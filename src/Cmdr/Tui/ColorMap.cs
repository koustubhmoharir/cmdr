using Cmdr.Core.Output;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>Maps output span styles to Terminal.Gui attributes. Pure: no views or drivers involved.</summary>
internal static class ColorMap
{
    /// <summary>Foreground for stderr text that has no explicit color.</summary>
    public static readonly Color StdErrDefault = new(0xE0, 0x6C, 0x75);

    private static readonly ColorName16[] Named16 =
    {
        ColorName16.Black,
        ColorName16.Red,
        ColorName16.Green,
        ColorName16.Yellow,
        ColorName16.Blue,
        ColorName16.Magenta,
        ColorName16.Cyan,
        ColorName16.Gray,
        ColorName16.DarkGray,
        ColorName16.BrightRed,
        ColorName16.BrightGreen,
        ColorName16.BrightYellow,
        ColorName16.BrightBlue,
        ColorName16.BrightMagenta,
        ColorName16.BrightCyan,
        ColorName16.White,
    };

    private static readonly byte[] CubeLevels = { 0, 95, 135, 175, 215, 255 };

    /// <summary>
    /// Builds the attribute for a styled span. Default colors come from <paramref name="normal"/>; stderr and system
    /// lines without an explicit foreground get a tint. Inverse swaps foreground and background after defaults are resolved.
    /// </summary>
    public static Attribute ToAttribute(SpanStyle style, OutputSource source, Attribute normal)
    {
        Color? explicitFg = ToColor(style.Foreground);
        Color? explicitBg = ToColor(style.Background);
        TextStyle textStyle = ToTextStyle(style.Attrs);

        Color fg;
        if (explicitFg is { } f)
        {
            fg = f;
        }
        else
        {
            switch (source)
            {
                case OutputSource.StdErr:
                    fg = StdErrDefault;
                    break;
                case OutputSource.System:
                    fg = ColorName16.Cyan;
                    textStyle |= TextStyle.Faint;
                    break;
                default:
                    fg = normal.Foreground;
                    break;
            }
        }

        Color bg = explicitBg ?? normal.Background;

        if ((style.Attrs & TextAttrs.Inverse) != 0)
        {
            (fg, bg) = (bg, fg);
        }

        return new Attribute(fg, bg, textStyle);
    }

    /// <summary>The color of a 256-color palette index: 0-15 named, 16-231 the 6x6x6 cube, 232-255 grays.</summary>
    public static Color IndexedColor(byte index)
    {
        if (index < 16)
        {
            return Named16[index];
        }

        if (index < 232)
        {
            int i = index - 16;
            return new Color(CubeLevels[i / 36], CubeLevels[(i / 6) % 6], CubeLevels[i % 6]);
        }

        int gray = 8 + 10 * (index - 232);
        return new Color(gray, gray, gray);
    }

    private static Color? ToColor(AnsiColor color) => color.Kind switch
    {
        ColorKind.Indexed => IndexedColor(color.Index),
        ColorKind.Rgb => new Color(color.R, color.G, color.B),
        _ => null,
    };

    private static TextStyle ToTextStyle(TextAttrs attrs)
    {
        TextStyle style = TextStyle.None;
        if (attrs.HasFlag(TextAttrs.Bold))
        {
            style |= TextStyle.Bold;
        }

        if (attrs.HasFlag(TextAttrs.Dim))
        {
            style |= TextStyle.Faint;
        }

        if (attrs.HasFlag(TextAttrs.Italic))
        {
            style |= TextStyle.Italic;
        }

        if (attrs.HasFlag(TextAttrs.Underline))
        {
            style |= TextStyle.Underline;
        }

        if (attrs.HasFlag(TextAttrs.Blink))
        {
            style |= TextStyle.Blink;
        }

        if (attrs.HasFlag(TextAttrs.Strikethrough))
        {
            style |= TextStyle.Strikethrough;
        }

        return style;
    }
}
