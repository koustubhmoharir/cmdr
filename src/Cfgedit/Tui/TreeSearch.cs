using Shared.Core;
using Shared.Tui;

namespace Cfgedit.Tui;

/// <summary>What the search needs from the window that owns it: the bar's row, the focus and the message line.</summary>
internal interface ISearchHost
{
    /// <summary>Shows or hides the search bar's row, above the detail line.</summary>
    /// <param name="open">True to show the bar.</param>
    void ShowSearchBar(bool open);

    /// <summary>Gives the focus back to the tree.</summary>
    void FocusTree();

    /// <summary>Shows a message on the detail line.</summary>
    /// <param name="text">The message.</param>
    /// <param name="error">Whether the message is an error.</param>
    void ShowMessage(string text, bool error);
}

/// <summary>
/// The search of the tree: the search bar, its query and compiled pattern, the matches of the document, the current match, and the moves
/// between the matches.
/// </summary>
/// <remarks>
/// <para>
/// <b>Query.</b> The bar's text and options. Each change compiles the pattern and searches the whole document, and the matches are
/// highlighted. An invalid pattern is shown in the bar and highlights nothing. Esc clears the text, and then closes the bar; closing ends
/// the highlights and keeps the last non-empty query for <c>n</c>, <c>N</c> and F3, which reopen the bar with it.
/// </para>
/// <para>
/// <b>Moves.</b> <c>n</c> and F3 go to the next match after the cursor's part, <c>N</c> and Shift+F3 to the previous one. Within the cursor's
/// part, the current match is the reference when it is in that part; otherwise every match of the part counts. The search wraps at either
/// end, and says so. Going to a match expands its collapsed ancestors, and moves the cursor to its part. The current match stays marked
/// until the query changes, the document changes or the bar closes.
/// </para>
/// <para>
/// <b>Document changes.</b> Any edit, undo or redo searches the new document again, so a match is never shown at a node that has changed.
/// The current match is dropped then.
/// </para>
/// </remarks>
internal sealed class TreeSearch
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
    private readonly TreeView _tree;
    private readonly EditController _controller;
    private SearchQuery _lastQuery = new(string.Empty, false, false, false);
    private SearchPattern? _pattern;
    private DocSearch? _search;
    private DocMatch? _active;
    private bool _open;

    /// <summary>Creates the search of a tree. The bar is hidden until a search opens.</summary>
    /// <param name="host">The window that owns the search.</param>
    /// <param name="tree">The tree, whose matches are highlighted and which the moves go to.</param>
    /// <param name="controller">The controller, whose document is searched.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public TreeSearch(ISearchHost host, TreeView tree, EditController controller)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(controller);

        _host = host;
        _tree = tree;
        _controller = controller;

        Bar = new SearchBar();
        Bar.QueryChanged += OnQueryChanged;
        Bar.FocusPaneRequested += OnFocusPaneRequested;
        Bar.Closed += Close;
        Bar.FindRequested += backward => Find(next: !backward);
    }

    /// <summary>The search bar. The window places it above the detail line.</summary>
    public SearchBar Bar { get; }

    /// <summary>Whether the search bar is open. A closed bar has no highlights.</summary>
    public bool IsOpen => _open;

    /// <summary>
    /// Opens the bar with the last query. Focuses its field with the text selected when <paramref name="focusBar"/> is true. The caller has
    /// closed any inline editor first, since the bar and the editor are never open together.
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
    /// the focus. Does nothing with no query or no match in the document; a document with no match says so.
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

        if (_search is null || _tree.CursorNode is not { } node)
        {
            return;
        }

        IReadOnlyList<DocMatch> matches = _search.Matches;
        if (matches.Count == 0)
        {
            _host.ShowMessage(NoMatches, error: false);
            return;
        }

        int cursor = _search.OrderOf(node.Path, _tree.CursorIsEnd, _tree.CursorPart);
        int offset = _active is { } current && !_tree.CursorIsEnd && current.Node.Path == node.Path && current.Part == _tree.CursorPart
            ? current.Start
            : -1;

        DocMatch? found = null;
        if (next)
        {
            foreach (DocMatch match in matches)
            {
                if (match.Order > cursor || (match.Order == cursor && match.Start > offset))
                {
                    found = match;
                    break;
                }
            }
        }
        else
        {
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                DocMatch match = matches[i];
                if (match.Order < cursor || (match.Order == cursor && match.Start < offset))
                {
                    found = match;
                    break;
                }
            }
        }

        bool wrapped = found is null;
        DocMatch target = found ?? (next ? matches[0] : matches[^1]);
        _active = target;
        _tree.RevealPart(target.Node.Path, target.Part);
        Apply();
        if (wrapped)
        {
            _host.ShowMessage(Wrapped, error: false);
        }
    }

    /// <summary>
    /// Closes the bar: remembers the text, keeps the last non-empty query for <c>n</c>, <c>N</c> and F3 (with the bar's options), clears the
    /// highlights and hides the bar. The focus goes back to the tree when the bar had it. Does nothing when the bar is closed.
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
        _search = null;
        _active = null;
        Bar.SetStatus(string.Empty);
        Apply();
        if (hadFocus)
        {
            _host.FocusTree();
        }

        _host.ShowSearchBar(false);
    }

    /// <summary>
    /// Searches the document again, after an edit, an undo, a redo or a reload. The current match is dropped, since its node may have changed.
    /// Does nothing when the bar is closed.
    /// </summary>
    public void OnDocumentChanged()
    {
        if (_open)
        {
            Recompute();
        }
    }

    /// <summary>Every change of the bar's text or options: compiles the query and searches the document. Nothing moves.</summary>
    private void OnQueryChanged(SearchQuery query)
    {
        if (query.Text.Length > 0)
        {
            _lastQuery = query;
        }

        _pattern = Compile(query);
        Recompute();
        if (_search is { TimedOut: true })
        {
            _host.ShowMessage(TimedOut, error: true);
        }
    }

    /// <summary>Enter, Tab and Shift+Tab in the bar: the focus goes back to the tree, and the search moves to the next match.</summary>
    private void OnFocusPaneRequested()
    {
        _host.FocusTree();
        Find(next: true);
    }

    /// <summary>Compiles a query for the bar. An invalid pattern is shown in the bar; an empty query compiles to <c>null</c> without a message.</summary>
    private SearchPattern? Compile(SearchQuery query)
    {
        SearchPattern? pattern = SearchPattern.Create(query, out string? error);
        Bar.SetStatus(error is null ? string.Empty : InvalidPattern);
        return pattern;
    }

    /// <summary>Searches the document with the current pattern, and drops the current match. A null pattern leaves no matches.</summary>
    private void Recompute()
    {
        _search = _pattern is null ? null : DocSearch.Build(_controller.Document, _pattern);
        _active = null;
        Apply();
    }

    /// <summary>Gives the tree the current matches and the current match, or no highlights when there is no search.</summary>
    private void Apply()
    {
        if (_search is not { } search)
        {
            _tree.SetHighlights(null);
            return;
        }

        DocMatch? active = _active;
        _tree.SetHighlights((node, part) => search.MarksOf(node, part, active));
    }
}
