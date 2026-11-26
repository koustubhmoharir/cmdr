using System.Collections.Immutable;
using Mdedit.Core.Render;
using Shared.Core;
using Shared.Tui;

namespace Mdedit.Tui;

/// <summary>
/// The search of the window in both modes: the search bar, its query and compiled pattern, the matches of the document, the active match,
/// and the moves between the matches. The document is an <see cref="IMatchTarget"/>: the rendered view in view mode, the source in edit
/// mode. It follows cfgedit's tree search: the same bar, messages and keys.
/// </summary>
/// <remarks>
/// <para>
/// <b>Query.</b> Each change of the bar's text or options compiles the pattern and searches the whole document. An invalid pattern is shown
/// in the bar and matches nothing. Esc clears the text, and then closes the bar; closing ends the highlights and keeps the last non-empty
/// query for <c>n</c>, <c>N</c>, F3 and Shift+F3, which reopen the bar with it. The query carries over between the modes this way: the
/// window closes the bar when the mode changes.
/// </para>
/// <para>
/// <b>Moves.</b> A move starts from the target's cursor. <c>n</c> and F3 go to the first match that starts after the cursor (or at it, unless
/// it is the active match), <c>N</c> and Shift+F3 to the last one that starts before it. The search wraps at either end and says so. The
/// active match is selected, with the cursor at its start.
/// </para>
/// <para>
/// <b>Document changes.</b> <see cref="Refresh"/> searches the document again after its text changed, and drops the active match. Nothing
/// in this part changes the document.
/// </para>
/// </remarks>
internal sealed class DocumentSearch
{
    /// <summary>The bar's status for a pattern that does not compile.</summary>
    internal const string InvalidPattern = "invalid pattern";

    /// <summary>The message of a search with no match in the document.</summary>
    internal const string NoMatches = "No matches";

    /// <summary>The message of a move that wrapped past the end (or the start, going backward).</summary>
    internal const string Wrapped = "Search wrapped";

    /// <summary>The message of a search whose pattern timed out.</summary>
    internal const string TimedOut = "Search pattern timed out";

    private readonly ISearchHost _host;
    private readonly IMatchTarget _target;
    private SearchQuery _lastQuery = new(string.Empty, false, false, false);
    private SearchPattern? _pattern;
    private ImmutableArray<SourceRange> _matches = ImmutableArray<SourceRange>.Empty;
    private int _active = -1;
    private bool _open;

    /// <summary>Creates the search of a document. The bar is hidden until a search opens.</summary>
    /// <param name="host">The window that owns the search.</param>
    /// <param name="target">The document searched: the target of the current mode.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public DocumentSearch(ISearchHost host, IMatchTarget target)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(target);

        _host = host;
        _target = target;

        Bar = new SearchBar();
        Bar.QueryChanged += OnQueryChanged;
        Bar.FocusPaneRequested += OnFocusPaneRequested;
        Bar.Closed += Close;
        Bar.FindRequested += backward => Find(next: !backward);
    }

    /// <summary>The search bar. The window places it above the message line.</summary>
    public SearchBar Bar { get; }

    /// <summary>Whether the search bar is open. A closed bar has no highlights.</summary>
    public bool IsOpen => _open;

    /// <summary>The matches of the open search, in document order, as source ranges. Empty when the bar is closed.</summary>
    public ImmutableArray<SourceRange> Matches => _matches;

    /// <summary>The index of the active match in <see cref="Matches"/>, or -1 when no match is active.</summary>
    public int ActiveIndex => _active;

    /// <summary>
    /// Opens the bar with the last query, and focuses its field with the text selected when <paramref name="focusBar"/> is true.
    /// </summary>
    /// <param name="focusBar">True to give the focus to the bar's field.</param>
    public void Open(bool focusBar)
    {
        if (!_open)
        {
            _open = true;
            _host.ShowSearchBar(true);
            Bar.SetQuery(_lastQuery);
            _pattern = Compile(_lastQuery);
            Recompute();
        }

        if (focusBar)
        {
            Bar.FocusField(selectAll: true);
        }
    }

    /// <summary>
    /// Moves to the next (or previous) match. When the bar is closed and a query was used before, the bar reopens with it, without taking
    /// the focus. Does nothing with no query; a document with no match says so.
    /// </summary>
    /// <param name="next">True for the next match, false for the previous one.</param>
    public void Find(bool next)
    {
        if (!_open)
        {
            if (_lastQuery.Text.Length == 0)
            {
                return;
            }

            Open(focusBar: false);
        }

        if (_pattern is null)
        {
            return;
        }

        if (_matches.IsEmpty)
        {
            _host.ShowMessage(NoMatches, error: false);
            return;
        }

        int index = FindIndex(next, _target.CursorOffset);
        bool wrapped = index < 0;
        if (wrapped)
        {
            index = next ? 0 : _matches.Length - 1;
        }

        _active = index;
        _target.Select(_matches[index]);
        _target.ShowMatches(_matches, _active);
        if (wrapped)
        {
            _host.ShowMessage(Wrapped, error: false);
        }
    }

    /// <summary>
    /// Closes the bar: remembers the text, keeps the last non-empty query for <c>n</c>, <c>N</c> and F3 (with the bar's options), clears the
    /// highlights and hides the bar. The focus goes back to the document when the bar had it. Does nothing when the bar is closed.
    /// </summary>
    public void Close()
    {
        if (!_open)
        {
            return;
        }

        Bar.RememberQuery();
        SearchQuery query = Bar.Query;
        _lastQuery = query.Text.Length > 0
            ? query
            : _lastQuery with { CaseSensitive = query.CaseSensitive, WholeWord = query.WholeWord, Regex = query.Regex };

        bool hadFocus = Bar.HasFieldFocus;
        _open = false;
        _pattern = null;
        _matches = ImmutableArray<SourceRange>.Empty;
        _active = -1;
        Bar.SetStatus(string.Empty);
        Apply();
        if (hadFocus)
        {
            _host.FocusDocument();
        }

        _host.ShowSearchBar(false);
    }

    /// <summary>
    /// Searches the document again, after its text changed (a render in view mode, an edit in edit mode). The active match is dropped. Does
    /// nothing when the bar is closed.
    /// </summary>
    public void Refresh()
    {
        if (_open)
        {
            Recompute();
        }
    }

    /// <summary>The index of the match to go to from a cursor offset, or -1 when the search must wrap.</summary>
    private int FindIndex(bool next, int cursor)
    {
        if (next)
        {
            for (int i = 0; i < _matches.Length; i++)
            {
                if (_matches[i].Start > cursor || (_matches[i].Start == cursor && i != _active))
                {
                    return i;
                }
            }
        }
        else
        {
            for (int i = _matches.Length - 1; i >= 0; i--)
            {
                if (_matches[i].Start < cursor)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>Every change of the bar's text or options: compiles the query and searches the document. Nothing moves.</summary>
    private void OnQueryChanged(SearchQuery query)
    {
        if (query.Text.Length > 0)
        {
            _lastQuery = query;
        }

        _pattern = Compile(query);
        bool timedOut = Recompute();
        if (timedOut)
        {
            _host.ShowMessage(TimedOut, error: true);
        }
    }

    /// <summary>Enter, Tab and Shift+Tab in the bar: the focus goes back to the document, and the search moves to the next match.</summary>
    private void OnFocusPaneRequested()
    {
        _host.FocusDocument();
        Find(next: true);
    }

    /// <summary>Compiles a query for the bar. An invalid pattern is shown in the bar; an empty query compiles to <c>null</c> without a message.</summary>
    private SearchPattern? Compile(SearchQuery query)
    {
        SearchPattern? pattern = SearchPattern.Create(query, out string? error);
        Bar.SetStatus(error is null ? string.Empty : InvalidPattern);
        return pattern;
    }

    /// <summary>
    /// Searches the document with the current pattern, and drops the active match. A null pattern leaves no matches. Returns whether the
    /// pattern timed out on the document.
    /// </summary>
    private bool Recompute()
    {
        bool timedOut = false;
        if (_pattern is null)
        {
            _matches = ImmutableArray<SourceRange>.Empty;
        }
        else
        {
            RenderSearchResult result = _target.Find(_pattern);
            _matches = result.Matches;
            timedOut = result.TimedOut;
        }

        _active = -1;
        Apply();
        return timedOut;
    }

    /// <summary>Gives the target the current matches and the active one; no highlights when the bar is closed.</summary>
    private void Apply() => _target.ShowMatches(_open ? _matches : ImmutableArray<SourceRange>.Empty, _active);
}
