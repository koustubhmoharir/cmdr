namespace Cmdr.Core.Output;

/// <summary>A run of text rendered with a single style.</summary>
/// <param name="Text">The text. Never empty inside an <see cref="OutputLine"/>.</param>
/// <param name="Style">The style applied to <paramref name="Text"/>.</param>
public readonly record struct StyledSpan(string Text, SpanStyle Style);
