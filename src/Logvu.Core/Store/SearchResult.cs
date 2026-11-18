namespace Logvu.Core.Store;

/// <summary>
/// The outcome of a search: the first matching event in the search's direction, or the boundary when history holds no further match.
/// </summary>
public sealed class SearchResult
{
    private SearchResult(EventRow? row, IReadOnlyList<MemberReadError> errors, bool timedOut = false)
    {
        Row = row;
        Errors = errors;
        IsTimedOut = timedOut;
    }

    /// <summary>The boundary of a search that found no further match. Its <see cref="Row"/> is <c>null</c>.</summary>
    public static SearchResult Boundary { get; } = new(null, []);

    /// <summary>
    /// A search abandoned because its regular expression timed out on an event (the pattern is then dropped, see
    /// <c>SearchPattern.TimedOut</c>). Its <see cref="Row"/> is <c>null</c>; it is not a boundary: matches may exist further on.
    /// </summary>
    public static SearchResult TimedOut { get; } = new(null, [], timedOut: true);

    /// <summary>Whether the search was abandoned because its pattern timed out.</summary>
    public bool IsTimedOut { get; }

    /// <summary>The row of the first match, or <c>null</c> at the boundary.</summary>
    public EventRow? Row { get; }

    /// <summary>Whether the search found a match.</summary>
    public bool IsFound => Row is not null;

    /// <summary>The members that could not be read during the search. The search went on past them.</summary>
    public IReadOnlyList<MemberReadError> Errors { get; }

    /// <summary>A result that found the match.</summary>
    /// <param name="row">The row of the match.</param>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is <c>null</c>.</exception>
    public static SearchResult Found(EventRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new SearchResult(row, []);
    }

    /// <summary>The same result, with the members that could not be read during the search.</summary>
    /// <param name="errors">The read errors of the search.</param>
    internal SearchResult WithErrors(IReadOnlyList<MemberReadError> errors) => new(Row, errors, IsTimedOut);
}
