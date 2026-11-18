using Logvu.Core.Detail;
using Shared.Core;

namespace Logvu.Tui;

/// <summary>A match in a detail: the index of its line among the full lines, and its UTF-16 range in that line's text.</summary>
/// <param name="Line">The index in <see cref="DetailDocument.FullLines"/>.</param>
/// <param name="Start">The first UTF-16 unit of the match in the line's text.</param>
/// <param name="Length">The number of UTF-16 units of the match.</param>
internal readonly record struct DetailHit(int Line, int Start, int Length);

/// <summary>
/// Finds the matches of a pattern among the lines of a detail, in document order, one line at a time. Each line's text is searched
/// on its own, so a match never spans two lines. Pure: no views.
/// </summary>
internal static class DetailSearch
{
    /// <summary>The first match of the document, or <c>null</c> when there is none.</summary>
    /// <param name="lines">The full lines of the document.</param>
    /// <param name="pattern">The pattern to find.</param>
    public static DetailHit? First(IReadOnlyList<DetailLine> lines, SearchPattern pattern) => Next(lines, pattern, 0, -1);

    /// <summary>
    /// The first match after the position (<paramref name="line"/>, <paramref name="start"/>): a later match of the same line, or a match
    /// of a later line. A <paramref name="start"/> of -1 makes every match of <paramref name="line"/> count. Null when there is none.
    /// </summary>
    /// <param name="lines">The full lines of the document.</param>
    /// <param name="pattern">The pattern to find.</param>
    /// <param name="line">The index of the line the position is on.</param>
    /// <param name="start">The UTF-16 offset of the position in that line, or -1 for the start of the line.</param>
    public static DetailHit? Next(IReadOnlyList<DetailLine> lines, SearchPattern pattern, int line, int start)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(pattern);

        for (int index = Math.Max(0, line); index < lines.Count; index++)
        {
            foreach (SearchMatch match in pattern.Matches(lines[index].Text))
            {
                if (index > line || match.Start > start)
                {
                    return new DetailHit(index, match.Start, match.Length);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The last match before the position (<paramref name="line"/>, <paramref name="start"/>): an earlier match of the same line, or a
    /// match of an earlier line. A <paramref name="start"/> of -1 leaves no match of <paramref name="line"/> before it. Null when there is none.
    /// </summary>
    /// <param name="lines">The full lines of the document.</param>
    /// <param name="pattern">The pattern to find.</param>
    /// <param name="line">The index of the line the position is on.</param>
    /// <param name="start">The UTF-16 offset of the position in that line, or -1 for the start of the line.</param>
    public static DetailHit? Previous(IReadOnlyList<DetailLine> lines, SearchPattern pattern, int line, int start)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(pattern);

        for (int index = Math.Min(line, lines.Count - 1); index >= 0; index--)
        {
            IReadOnlyList<SearchMatch> matches = pattern.Matches(lines[index].Text);
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                SearchMatch match = matches[i];
                if (index < line || match.Start < start)
                {
                    return new DetailHit(index, match.Start, match.Length);
                }
            }
        }

        return null;
    }
}
