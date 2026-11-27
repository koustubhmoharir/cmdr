namespace Mdedit.Core.Editing;

/// <summary>The line ending a structure edit writes: the document's own, so that new lines match the lines around them.</summary>
internal static class LineEnding
{
    /// <summary>
    /// The line ending of the first line break in <paramref name="text"/>: <c>\r\n</c>, <c>\n</c> or <c>\r</c>. <c>\n</c> when the text has none.
    /// </summary>
    /// <param name="text">The source text.</param>
    public static string Of(string text)
    {
        int index = text.IndexOfAny(['\r', '\n']);
        if (index < 0 || text[index] == '\n')
        {
            return "\n";
        }

        return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
    }
}
