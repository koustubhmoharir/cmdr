namespace Cmdr.Core.Output;

/// <summary>An immutable color: the default, a 256-color palette index, or 24-bit RGB.</summary>
public readonly record struct AnsiColor
{
    private AnsiColor(ColorKind kind, byte index, byte r, byte g, byte b)
    {
        Kind = kind;
        Index = index;
        R = r;
        G = g;
        B = b;
    }

    /// <summary>How this color is specified.</summary>
    public ColorKind Kind { get; }

    /// <summary>Palette index (valid when <see cref="Kind"/> is <see cref="ColorKind.Indexed"/>).</summary>
    public byte Index { get; }

    /// <summary>Red component (valid when <see cref="Kind"/> is <see cref="ColorKind.Rgb"/>).</summary>
    public byte R { get; }

    /// <summary>Green component (valid when <see cref="Kind"/> is <see cref="ColorKind.Rgb"/>).</summary>
    public byte G { get; }

    /// <summary>Blue component (valid when <see cref="Kind"/> is <see cref="ColorKind.Rgb"/>).</summary>
    public byte B { get; }

    /// <summary>The terminal's default color.</summary>
    public static AnsiColor Default => new(ColorKind.Default, 0, 0, 0, 0);

    /// <summary>A 256-color palette entry.</summary>
    public static AnsiColor FromIndex(byte index) => new(ColorKind.Indexed, index, 0, 0, 0);

    /// <summary>A 24-bit color.</summary>
    public static AnsiColor FromRgb(byte r, byte g, byte b) => new(ColorKind.Rgb, 0, r, g, b);
}
