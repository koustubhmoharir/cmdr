using System.Text.RegularExpressions;

namespace Mdedit.Core.Editing;

/// <summary>
/// Writes a typed character as source that renders as that character. Inline text escapes the characters Markdown reads as markup
/// everywhere; the ones that only start a line are escaped where the line they are typed on begins, which is the only place they
/// would be read as markup. Code spans and code, HTML and raw blocks take the character as it is.
/// </summary>
internal static class Escaper
{
    /// <summary>The characters escaped wherever they are typed: the backslash, emphasis and strikethrough markers, code, links and HTML.</summary>
    private const string Everywhere = @"\*_`[]<&~";

    /// <summary>The characters that start a block when they open its line: a heading, a quote, a list item, a setext underline.</summary>
    private const string LineStart = "#>-+=";

    /// <summary>
    /// A line's text before a typed character when the character would be the first one that is not a container marker, a list marker, a
    /// quote bar or an ordinal: spaces, tabs, <c>&gt;</c>, bullets, and digits (with their delimiter) before the caret.
    /// </summary>
    private static readonly Regex LineStartPrefix = new(@"^[ \t>]*(?:(?:[-+*]|\d{1,9}[.)])[ \t]+)*\d{0,9}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The source for a typed character.
    /// </summary>
    /// <param name="typed">The typed text. Only a single character is escaped; anything longer is written as it is.</param>
    /// <param name="linePrefix">The source of the line from its start to the insertion point, with any text written in front of the typed one.</param>
    /// <param name="raw">Whether the text is written raw (a code span, or a code, HTML or raw block).</param>
    /// <returns>The typed text, or the same character with the backslash that makes it literal.</returns>
    public static string Escape(string typed, string linePrefix, bool raw)
    {
        if (raw || typed.Length != 1)
        {
            return typed;
        }

        char c = typed[0];
        if (Everywhere.Contains(c))
        {
            return @"\" + typed;
        }

        if (LineStartPrefix.IsMatch(linePrefix) && (LineStart.Contains(c) || c is '.' or ')' && char.IsAsciiDigit(linePrefix.LastOrDefault())))
        {
            return @"\" + typed;
        }

        return typed;
    }
}
