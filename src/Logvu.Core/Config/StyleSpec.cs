namespace Logvu.Core.Config;

/// <summary>A configured style: the colors and attributes of a styled span. An unset color keeps the terminal's default.</summary>
/// <param name="Foreground">The foreground color, or <c>null</c> for the default.</param>
/// <param name="Background">The background color, or <c>null</c> for the default.</param>
/// <param name="Bold">Whether the text is bold.</param>
/// <param name="Italic">Whether the text is italic.</param>
/// <param name="Underline">Whether the text is underlined.</param>
public sealed record StyleSpec(LogColor? Foreground, LogColor? Background, bool Bold, bool Italic, bool Underline);
