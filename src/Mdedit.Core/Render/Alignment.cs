namespace Mdedit.Core.Render;

/// <summary>
/// Maps the characters of a string that Markdig produced back to the source they came from. Markdig drops the backslash of
/// an escape, drops the whitespace that prefixes a continuation line, turns a line ending inside a code span into a space and
/// drops the padding a code span strips, so the produced text is the source with some characters removed or replaced. The
/// walk here follows both at once and gives every produced character its source index.
/// </summary>
internal static class Alignment
{
    /// <summary>
    /// The source index of each character of <paramref name="content"/>, where the content was produced from the source
    /// between <paramref name="start"/> (inclusive) and <paramref name="end"/> (exclusive).
    /// </summary>
    /// <param name="source">The whole source text.</param>
    /// <param name="start">The start of the span the content came from.</param>
    /// <param name="end">The end of the span (exclusive).</param>
    /// <param name="content">The text Markdig produced for the span.</param>
    /// <param name="escapes">
    /// Whether backslash escapes apply: true for literal text, false for code spans, where a backslash is always a character.
    /// </param>
    /// <remarks>
    /// A line ending in the source maps the content character that stands for it (a space in a code span) to the first
    /// character of the line ending. With <paramref name="escapes"/>, a backslash before ASCII punctuation is skipped when the
    /// content has that punctuation in its place. Whitespace in the source that the content does not have is skipped. Any
    /// other mismatch maps one character to one, and the source character replaces the produced one; the walk never stops
    /// early. That last branch is a fallback for input the CommonMark rules do not describe (Markdig's NUL to U+FFFD is the
    /// one case seen); the tests check the inputs they cover.
    /// </remarks>
    public static AlignedText Align(string source, int start, int end, string content, bool escapes)
    {
        var map = new int[content.Length];
        var text = content.ToCharArray();
        int i = start;
        int j = 0;
        while (j < content.Length)
        {
            char c = content[j];
            if (i >= end)
            {
                // Content the source does not have. Map it to the last source character so that it stays inside the block.
                map[j++] = Math.Max(start, end - 1);
                continue;
            }

            char s = source[i];
            if (s is '\r' or '\n')
            {
                map[j++] = i;
                i += s == '\r' && i + 1 < end && source[i + 1] == '\n' ? 2 : 1;
            }
            else if (escapes && s == '\\' && i + 1 < end && IsAsciiPunctuation(source[i + 1]) && c == source[i + 1])
            {
                i++;
            }
            else if (c == s)
            {
                map[j++] = i++;
            }
            else if ((s is ' ' or '\t') && c is not (' ' or '\t'))
            {
                i++;
            }
            else
            {
                text[j] = s;
                map[j++] = i++;
            }
        }

        return new AlignedText(new string(text), map);
    }

    /// <summary>Whether <paramref name="c"/> is one of the ASCII punctuation characters that CommonMark lets a backslash escape.</summary>
    public static bool IsAsciiPunctuation(char c) =>
        c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';
}
