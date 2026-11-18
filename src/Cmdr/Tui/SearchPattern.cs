using System.Text.RegularExpressions;

namespace Cmdr.Tui;

/// <summary>The options of a search: the text typed and the three toggles.</summary>
internal readonly record struct SearchQuery(string Text, bool CaseSensitive, bool WholeWord, bool Regex);

/// <summary>One match: offset and length in UTF-16 units of the searched string. Length is always &gt; 0.</summary>
internal readonly record struct SearchMatch(int Start, int Length)
{
    /// <summary>The offset just after the last unit of the match.</summary>
    public int End => Start + Length;
}

/// <summary>
/// The matcher of a search box. Pure: it works on plain text and holds no UI types. Without regex the text is matched
/// literally, with no timeout, because an escaped literal cannot backtrack. Whole word requires that no word character
/// (<c>\w</c>) touches either end of a match. A regex is run with the linear NonBacktracking engine when it supports the
/// pattern. Otherwise (lookarounds, backreferences, atomic groups, and so on, which includes every whole-word regex) it
/// runs on the backtracking engine with a 1 second timeout. A timeout sets <see cref="TimedOut"/>, and the pattern then
/// finds nothing, so a pathological regex cannot freeze the UI line after line.
/// </summary>
internal sealed class SearchPattern
{
    private static readonly TimeSpan FallbackTimeout = TimeSpan.FromSeconds(1);

    private readonly Regex _regex;

    private SearchPattern(SearchQuery query, Regex regex)
    {
        Query = query;
        _regex = regex;
    }

    /// <summary>The query this pattern was created from.</summary>
    public SearchQuery Query { get; }

    /// <summary>
    /// True after the backtracking engine timed out on this pattern. From then on <see cref="Matches"/> returns an empty
    /// list and <see cref="Count"/> returns 0 for every text. The pattern is abandoned rather than returning partial results.
    /// </summary>
    public bool TimedOut { get; private set; }

    /// <summary>
    /// Compiles the query. Returns null with <paramref name="error"/> null when the text is empty (there is no search),
    /// and null with <paramref name="error"/> "invalid pattern" when the regex does not compile. A regex that is valid but
    /// needs the backtracking engine is not an error.
    /// </summary>
    public static SearchPattern? Create(SearchQuery query, out string? error)
    {
        ArgumentNullException.ThrowIfNull(query.Text);

        error = null;
        if (query.Text.Length == 0)
        {
            return null;
        }

        RegexOptions options = RegexOptions.CultureInvariant;
        if (!query.CaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            if (query.Regex && query.WholeWord)
            {
                // The wrapping below would make some broken patterns compile, such as "a)(b", so the raw text is checked first.
                _ = new Regex(query.Text, options);
            }

            string pattern = query.Regex ? query.Text : Regex.Escape(query.Text);
            if (query.WholeWord)
            {
                pattern = @"(?<!\w)(?:" + pattern + @")(?!\w)";
            }

            return new SearchPattern(query, BuildRegex(query.Regex, pattern, options));
        }
        catch (ArgumentException)
        {
            error = "invalid pattern";
            return null;
        }
    }

    /// <summary>
    /// The non-overlapping matches, left to right. Empty matches are skipped. Returns an empty list when the pattern has
    /// timed out (see <see cref="TimedOut"/>).
    /// </summary>
    public IReadOnlyList<SearchMatch> Matches(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var matches = new List<SearchMatch>();
        Scan(text, matches);
        return matches;
    }

    /// <summary>
    /// The number of matches that <see cref="Matches"/> returns for the same text, without building the list. Returns 0
    /// when the pattern has timed out.
    /// </summary>
    public int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Scan(text, null);
    }

    /// <summary>
    /// Builds the engine for the pattern. A literal has no timeout. A regex prefers NonBacktracking and falls back to the
    /// backtracking engine with a timeout when NonBacktracking does not support it.
    /// </summary>
    private static Regex BuildRegex(bool regex, string pattern, RegexOptions options)
    {
        if (!regex)
        {
            return new Regex(pattern, options);
        }

        try
        {
            return new Regex(pattern, options | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            return new Regex(pattern, options, FallbackTimeout);
        }
    }

    /// <summary>
    /// Walks the matches and adds them to <paramref name="collected"/> when it is given. Returns how many there are. A
    /// timeout marks the pattern as timed out, clears <paramref name="collected"/>, and returns 0.
    /// </summary>
    private int Scan(string text, List<SearchMatch>? collected)
    {
        if (TimedOut)
        {
            return 0;
        }

        int count = 0;
        try
        {
            for (Match match = _regex.Match(text); match.Success; match = match.NextMatch())
            {
                if (match.Length == 0)
                {
                    continue;
                }

                count++;
                collected?.Add(new SearchMatch(match.Index, match.Length));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            TimedOut = true;
            collected?.Clear();
            return 0;
        }

        return count;
    }
}
