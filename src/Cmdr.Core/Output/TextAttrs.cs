namespace Cmdr.Core.Output;

/// <summary>Text rendition attributes (SGR).</summary>
[Flags]
public enum TextAttrs
{
    /// <summary>No attributes.</summary>
    None = 0,

    /// <summary>Bold / increased intensity (SGR 1).</summary>
    Bold = 1,

    /// <summary>Dim / decreased intensity (SGR 2).</summary>
    Dim = 2,

    /// <summary>Italic (SGR 3).</summary>
    Italic = 4,

    /// <summary>Underline (SGR 4).</summary>
    Underline = 8,

    /// <summary>Blink (SGR 5).</summary>
    Blink = 16,

    /// <summary>Inverse video (SGR 7).</summary>
    Inverse = 32,

    /// <summary>Strikethrough (SGR 9).</summary>
    Strikethrough = 64,
}
