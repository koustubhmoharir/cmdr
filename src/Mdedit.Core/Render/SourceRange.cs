namespace Mdedit.Core.Render;

/// <summary>A range of the source text: <paramref name="Start"/> inclusive, <paramref name="End"/> exclusive, absolute offsets in the document.</summary>
/// <param name="Start">The offset of the first character of the range.</param>
/// <param name="End">The offset after the last character of the range.</param>
public readonly record struct SourceRange(int Start, int End);
