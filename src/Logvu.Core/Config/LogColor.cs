using System.Globalization;

namespace Logvu.Core.Config;

/// <summary>The 16 named ANSI colors, in their conventional order.</summary>
public enum AnsiColor
{
    /// <summary>ANSI color 0.</summary>
    Black,

    /// <summary>ANSI color 1.</summary>
    Red,

    /// <summary>ANSI color 2.</summary>
    Green,

    /// <summary>ANSI color 3.</summary>
    Yellow,

    /// <summary>ANSI color 4.</summary>
    Blue,

    /// <summary>ANSI color 5.</summary>
    Magenta,

    /// <summary>ANSI color 6.</summary>
    Cyan,

    /// <summary>ANSI color 7.</summary>
    White,

    /// <summary>ANSI color 8.</summary>
    BrightBlack,

    /// <summary>ANSI color 9.</summary>
    BrightRed,

    /// <summary>ANSI color 10.</summary>
    BrightGreen,

    /// <summary>ANSI color 11.</summary>
    BrightYellow,

    /// <summary>ANSI color 12.</summary>
    BrightBlue,

    /// <summary>ANSI color 13.</summary>
    BrightMagenta,

    /// <summary>ANSI color 14.</summary>
    BrightCyan,

    /// <summary>ANSI color 15.</summary>
    BrightWhite,
}

/// <summary>A 24-bit color.</summary>
/// <param name="R">The red component.</param>
/// <param name="G">The green component.</param>
/// <param name="B">The blue component.</param>
public readonly record struct RgbColor(byte R, byte G, byte B);

/// <summary>
/// A color of a style: one of the 16 named ANSI colors, or an RGB color written <c>#rrggbb</c> in configuration. Exactly one of
/// <see cref="Named"/> and <see cref="Rgb"/> is set.
/// </summary>
public readonly record struct LogColor
{
    private LogColor(AnsiColor? named, RgbColor? rgb)
    {
        Named = named;
        Rgb = rgb;
    }

    /// <summary>The named ANSI color, or <c>null</c> for an RGB color.</summary>
    public AnsiColor? Named { get; }

    /// <summary>The RGB color, or <c>null</c> for a named color.</summary>
    public RgbColor? Rgb { get; }

    /// <summary>Creates a named color.</summary>
    public static LogColor FromNamed(AnsiColor color) => new(color, null);

    /// <summary>Creates an RGB color.</summary>
    public static LogColor FromRgb(RgbColor color) => new(null, color);

    /// <summary>
    /// Parses a configuration color: one of the 16 ANSI names (case-insensitive, for example <c>brightCyan</c>) or <c>#rrggbb</c>
    /// with six hexadecimal digits.
    /// </summary>
    /// <returns><c>true</c> when <paramref name="text"/> is a valid color.</returns>
    public static bool TryParse(string text, out LogColor color)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 7 && text[0] == '#')
        {
            ReadOnlySpan<char> digits = text.AsSpan(1);
            foreach (char c in digits)
            {
                if (!char.IsAsciiHexDigit(c))
                {
                    color = default;
                    return false;
                }
            }

            color = FromRgb(new RgbColor(
                byte.Parse(digits[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(digits[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
            return true;
        }

        foreach (AnsiColor named in Enum.GetValues<AnsiColor>())
        {
            if (string.Equals(named.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                color = FromNamed(named);
                return true;
            }
        }

        color = default;
        return false;
    }
}
