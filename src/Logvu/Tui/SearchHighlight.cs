using System.Globalization;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tui;

/// <summary>How a run of text is highlighted: not at all, as a match, or as the active match.</summary>
internal enum SearchRunKind
{
    /// <summary>Text outside every match.</summary>
    Plain,

    /// <summary>Text of a match that is not the active one.</summary>
    Match,

    /// <summary>Text of the active match.</summary>
    Active,
}

/// <summary>A run of text: its UTF-16 range in the searched text, and its highlight.</summary>
/// <param name="Start">The first UTF-16 unit of the run.</param>
/// <param name="End">The offset just after the run's last unit.</param>
/// <param name="Kind">The highlight of the run.</param>
internal readonly record struct SearchRun(int Start, int End, SearchRunKind Kind);

/// <summary>
/// Maps the matches of a text onto the runs that are drawn: the text between matches is plain, and each match is a run of its own. A
/// match that crosses the edge of the range is cut at the edge, so the rows of a wrapped line each show their part. Runs are cut at
/// grapheme cluster boundaries, so a cluster is never drawn in two parts. Pure: no views or drivers.
/// </summary>
internal static class SearchHighlight
{
    /// <summary>
    /// The runs of <paramref name="from"/> to <paramref name="to"/> of <paramref name="text"/>, in order, covering the whole range. Matches
    /// outside the range are ignored. Without matches the range is one plain run; an empty range has no runs.
    /// </summary>
    /// <param name="text">The text the matches are offsets in.</param>
    /// <param name="from">The first unit of the range. It is a grapheme boundary.</param>
    /// <param name="to">The offset just after the range.</param>
    /// <param name="matches">The matches of <paramref name="text"/>, in order and not overlapping, or <c>null</c> for none.</param>
    /// <param name="isActive">Tells whether a match is the active one. Every match is a plain match when this is <c>null</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <c>null</c>.</exception>
    public static IReadOnlyList<SearchRun> Split(
        string text,
        int from,
        int to,
        IReadOnlyList<SearchMatch>? matches,
        Func<SearchMatch, bool>? isActive = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var runs = new List<SearchRun>();
        if (from >= to)
        {
            return runs;
        }

        if (matches is null || matches.Count == 0)
        {
            runs.Add(new SearchRun(from, to, SearchRunKind.Plain));
            return runs;
        }

        // The boundaries that matter lie inside the range, since the range starts on a boundary and every cut is clamped to it. Only the
        // range is parsed, so a line split into many pieces is parsed once per piece, not once per line.
        int length = to - from;
        int[] boundaries = StringInfo.ParseCombiningCharacters(text.Substring(from, length));
        int position = 0;
        foreach (SearchMatch match in matches)
        {
            if (match.End <= from || match.Start >= to)
            {
                continue;
            }

            int start = Math.Max(position, Floor(boundaries, Math.Max(0, match.Start - from)));
            int end = Math.Min(length, Ceiling(boundaries, match.End - from, length));
            if (end <= start)
            {
                continue;
            }

            if (start > position)
            {
                runs.Add(new SearchRun(from + position, from + start, SearchRunKind.Plain));
            }

            SearchRunKind kind = isActive?.Invoke(match) == true ? SearchRunKind.Active : SearchRunKind.Match;
            runs.Add(new SearchRun(from + start, from + end, kind));
            position = end;
        }

        if (position < length)
        {
            runs.Add(new SearchRun(from + position, to, SearchRunKind.Plain));
        }

        return runs;
    }

    /// <summary>The attribute of a run: the match colors of <see cref="SearchColors"/> for a match, and <paramref name="attribute"/> for plain text.</summary>
    /// <param name="attribute">The attribute the run would have without a search.</param>
    /// <param name="kind">The run's highlight.</param>
    public static Attribute Paint(Attribute attribute, SearchRunKind kind) => kind switch
    {
        SearchRunKind.Match => SearchColors.Match(attribute),
        SearchRunKind.Active => SearchColors.Active(attribute),
        _ => attribute,
    };

    /// <summary>The start of the grapheme cluster that contains <paramref name="index"/>.</summary>
    private static int Floor(int[] boundaries, int index)
    {
        int found = Array.BinarySearch(boundaries, index);
        if (found >= 0)
        {
            return index;
        }

        int next = ~found;
        return next == 0 ? 0 : boundaries[next - 1];
    }

    /// <summary>The first grapheme cluster boundary at or after <paramref name="index"/>, or <paramref name="length"/> when there is none.</summary>
    private static int Ceiling(int[] boundaries, int index, int length)
    {
        int found = Array.BinarySearch(boundaries, index);
        if (found >= 0)
        {
            return index;
        }

        int next = ~found;
        return next == boundaries.Length ? length : boundaries[next];
    }
}
