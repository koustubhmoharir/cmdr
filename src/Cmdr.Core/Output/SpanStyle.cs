namespace Cmdr.Core.Output;

/// <summary>The complete rendition of a run of text: colors and attributes.</summary>
/// <param name="Foreground">Foreground color.</param>
/// <param name="Background">Background color.</param>
/// <param name="Attrs">Text attributes.</param>
public readonly record struct SpanStyle(AnsiColor Foreground, AnsiColor Background, TextAttrs Attrs)
{
    /// <summary>Default foreground, default background, no attributes.</summary>
    public static SpanStyle Default => new(AnsiColor.Default, AnsiColor.Default, TextAttrs.None);
}
