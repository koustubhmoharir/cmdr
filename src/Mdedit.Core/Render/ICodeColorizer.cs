namespace Mdedit.Core.Render;

/// <summary>
/// Splits the text of a code block into tokens for colouring. The render core calls it once per fenced code block that has a
/// language, with the block's lines joined by line feeds, and sets each code cell's token from the span that covers the cell's
/// first character. The implementation lives outside Mdedit.Core, which has no syntax definitions of its own.
/// </summary>
public interface ICodeColorizer
{
    /// <summary>
    /// The spans of a code block. Characters that no span covers are <see cref="CodeToken.None"/>.
    /// </summary>
    /// <param name="code">The text of the block: its lines joined with <c>\n</c>, without their line endings.</param>
    /// <param name="language">The normalized language id from <see cref="CodeLanguages.Normalize"/>.</param>
    /// <returns>The spans, or null when there is no colouring for this language.</returns>
    IReadOnlyList<CodeSpan>? Colorize(string code, string language);
}
