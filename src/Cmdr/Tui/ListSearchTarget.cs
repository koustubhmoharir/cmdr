using Cmdr.Core.Session;
using Shared.Core;
using Shared.Tui;

namespace Cmdr.Tui;

/// <summary>What the list search needs from the controller. UI thread only.</summary>
internal interface IListSearchHost
{
    /// <summary>Every row of the session, including those inside collapsed groups, in list order.</summary>
    IReadOnlyList<ListRow> AllRows { get; }

    /// <summary>The key (see <see cref="TreeRows.KeyOf"/>) of the focused row, or null when no row is focused.</summary>
    object? FocusedKey { get; }

    /// <summary>Expands the collapsed files and groups that hide the row with the key, and focuses that row.</summary>
    void Reveal(object key);

    /// <summary>Redraws the command list.</summary>
    void RedrawList();
}

/// <summary>
/// The search of the command list. The searched text of each row is its name (a group or a command), or the display path of
/// its file (a file row), and every row of the session is searched, including those hidden in collapsed files and groups. Going
/// to a match reveals it through the host. Matches are highlighted by the list source. UI thread only.
/// </summary>
internal sealed class ListSearchTarget : ISearchTarget
{
    private readonly IListSearchHost _host;
    private readonly CommandListSource _source;
    private SearchPattern? _search;
    private (object Key, int Start, int Length)? _active;

    /// <param name="host">The controller, which supplies the rows and reveals the matches.</param>
    /// <param name="source">The list source, which draws the highlights.</param>
    public ListSearchTarget(IListSearchHost host, CommandListSource source)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    /// Sets the pattern and redraws. Null ends the search: the highlights go and the active match is dropped. A pattern keeps the
    /// active match as the origin of the next <see cref="Find"/>.
    /// </summary>
    public void SetSearch(SearchPattern? pattern)
    {
        _search = pattern;
        _source.Search = pattern;
        if (pattern is null)
        {
            _active = null;
            _source.ActiveMatch = null;
        }

        _host.RedrawList();
    }

    /// <summary>
    /// Moves to the next (or previous) match after the origin and reveals it. The origin is the active match when its item is still
    /// in the list, otherwise the focused row (the top of it going forward, the end of it going backward), otherwise the start (or
    /// end) of the list. Past the end (or start) it wraps around the list and returns <see cref="SearchResult.Wrapped"/>. Changes
    /// nothing when there is no match.
    /// </summary>
    public SearchResult Find(bool backward, bool inclusive)
    {
        SearchPattern? pattern = _search;
        IReadOnlyList<ListRow> rows = _host.AllRows;
        if (pattern is null || rows.Count == 0)
        {
            return SearchResult.NotFound;
        }

        int last = rows.Count - 1;
        IReadOnlyList<IReadOnlyList<SearchMatch>> matches = rows.Select(row => pattern.Matches(TreeRows.SearchTextOf(row))).ToList();

        int originRow;
        int originStart;
        bool originInclusive;
        int activeRow = _active is { } active ? IndexOfKey(rows, active.Key) : -1;
        int focusRow = _host.FocusedKey is { } focused ? IndexOfKey(rows, focused) : -1;
        if (_active is { } current && activeRow >= 0)
        {
            originRow = activeRow;
            originStart = current.Start;
            originInclusive = inclusive;
        }
        else if (focusRow >= 0)
        {
            // Going forward, the focused row's own match counts, so the search from the row is inclusive.
            originRow = focusRow;
            originStart = backward ? int.MaxValue : 0;
            originInclusive = !backward;
        }
        else
        {
            originRow = backward ? last : 0;
            originStart = backward ? int.MaxValue : 0;
            originInclusive = !backward;
        }

        // Positions compare by row, then by start. Going backward the matches before the origin come first, going forward the ones after it.
        bool Past(int row, int start)
        {
            int order = row != originRow ? row.CompareTo(originRow) : start.CompareTo(originStart);
            bool inOrder = backward ? order < 0 : order > 0;
            return inOrder || originInclusive && order == 0;
        }

        // The first match past the origin, or else, after wrapping, the first match from the other end up to and including the origin row.
        (int Row, SearchMatch Match)? found = backward
            ? FirstMatch(matches, originRow, 0, backward: true, Past)
            : FirstMatch(matches, originRow, last, backward: false, Past);
        bool wrapped = found is null;
        found ??= backward
            ? FirstMatch(matches, last, originRow, backward: true, (_, _) => true)
            : FirstMatch(matches, 0, originRow, backward: false, (_, _) => true);
        if (found is not { } target)
        {
            return SearchResult.NotFound;
        }

        object key = TreeRows.KeyOf(rows[target.Row]);
        _active = (key, target.Match.Start, target.Match.Length);
        _source.ActiveMatch = _active;
        _host.Reveal(key);
        _host.RedrawList();
        return wrapped ? SearchResult.Wrapped : SearchResult.Found;
    }

    /// <summary>
    /// The number of matches in the names of all rows, and the 1-based number of the active match among them in list order (0 when
    /// there is no active match, or no identical match exists).
    /// </summary>
    public SearchStatus SearchStatus()
    {
        SearchPattern? pattern = _search;
        if (pattern is null)
        {
            return new SearchStatus(0, 0);
        }

        int count = 0;
        int active = 0;
        foreach (ListRow row in _host.AllRows)
        {
            object key = TreeRows.KeyOf(row);
            IReadOnlyList<SearchMatch> matches = pattern.Matches(TreeRows.SearchTextOf(row));
            if (active == 0 && _active is { } current && ReferenceEquals(current.Key, key))
            {
                int index = IndexOfMatch(matches, current.Start, current.Length);
                if (index >= 0)
                {
                    active = count + index + 1;
                }
            }

            count += matches.Count;
        }

        return new SearchStatus(count, active);
    }

    /// <summary>The index of the row with the given key (compared by reference, see <see cref="TreeRows.KeyOf"/>), or -1.</summary>
    private static int IndexOfKey(IReadOnlyList<ListRow> rows, object key)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(TreeRows.KeyOf(rows[i]), key))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The index of the match with the given start and length among the matches, or -1.</summary>
    private static int IndexOfMatch(IReadOnlyList<SearchMatch> matches, int start, int length)
    {
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i].Start == start && matches[i].Length == length)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The first match, in search order, on the rows from <paramref name="from"/> to <paramref name="to"/> (both inclusive) that
    /// <paramref name="accept"/> takes. Going backward the rows and the matches of each row run downward, otherwise upward. Null when none is taken.
    /// </summary>
    private static (int Row, SearchMatch Match)? FirstMatch(
        IReadOnlyList<IReadOnlyList<SearchMatch>> matches,
        int from,
        int to,
        bool backward,
        Func<int, int, bool> accept)
    {
        int step = backward ? -1 : 1;
        for (int row = from; backward ? row >= to : row <= to; row += step)
        {
            IReadOnlyList<SearchMatch> rowMatches = matches[row];
            for (int i = 0; i < rowMatches.Count; i++)
            {
                SearchMatch match = rowMatches[backward ? rowMatches.Count - 1 - i : i];
                if (accept(row, match.Start))
                {
                    return (row, match);
                }
            }
        }

        return null;
    }
}
