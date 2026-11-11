namespace Cmdr.Core.Output;

/// <summary>How an <see cref="AnsiColor"/> is specified.</summary>
public enum ColorKind
{
    /// <summary>The terminal's default foreground or background.</summary>
    Default,

    /// <summary>A 256-color palette index in <see cref="AnsiColor.Index"/>.</summary>
    Indexed,

    /// <summary>A 24-bit color in <see cref="AnsiColor.R"/>, <see cref="AnsiColor.G"/>, <see cref="AnsiColor.B"/>.</summary>
    Rgb,
}
