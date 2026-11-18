using Shared.Core;
namespace Shared.Tui;

/// <summary>The outcome of moving to a match.</summary>
public enum SearchResult
{
    /// <summary>There is no match to move to.</summary>
    NotFound,

    /// <summary>A match was found before the end (or start, going backward).</summary>
    Found,

    /// <summary>A match was found, but only after wrapping past the end (or start, going backward).</summary>
    Wrapped,
}

/// <summary>The match count of a pane's search and the 1-based number of the active match (0 when there is no active match).</summary>
public readonly record struct SearchStatus(int Count, int Active);

/// <summary>A pane that can be searched. UI thread only.</summary>
public interface ISearchTarget
{
    /// <summary>
    /// Sets the pattern (null ends the search and removes the highlights). Keeps the position of the active match as the origin of
    /// the next Find.
    /// </summary>
    void SetSearch(SearchPattern? pattern);

    /// <summary>
    /// Moves the active match to the next (or previous) match and scrolls it into view. When <paramref name="inclusive"/> is true,
    /// a match starting exactly at the origin counts (used while typing).
    /// </summary>
    SearchResult Find(bool backward, bool inclusive);

    /// <summary>The current count and active number.</summary>
    SearchStatus SearchStatus();
}
