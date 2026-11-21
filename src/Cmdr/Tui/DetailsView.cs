using System.Text;
using Cmdr.Core.Session;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// The details strip for the focused row: a command shows its command line, working directory, environment (with the environment
/// script values that are known), pid and log, and description; a group shows its mode, working directory, environment, environment
/// script status and description; a file row shows its path and settings, and its script status (see <see cref="DetailsText"/>).
/// The lines are wrapped to the width and scroll when they do not fit. The process state is read at draw time. UI thread only.
/// </summary>
/// <remarks>
/// The text can be selected with the mouse: a press starts a character selection, a drag extends it (dragging past the top or
/// bottom scrolls), and <c>c</c> copies it. The selection is kept as logical positions (see <see cref="TextSelection"/>), so
/// it survives redraws and scrolling; it is cleared by Esc and when another row is shown.
/// The text can also be searched (see <see cref="ISearchTarget"/>): the matches are highlighted in the logical text, so a match
/// that wraps is highlighted on each of its rows.
/// </remarks>
internal sealed class DetailsView : View, ISearchTarget
{
    private const int WheelLines = 3;

    /// <summary>The longest selected text, in UTF-16 units, that seeds a search (see <see cref="SelectionSeed"/>).</summary>
    private const int SeedMaxLength = 200;

    private readonly MainController _controller;
    private ListRow? _row;
    private string _configDir = string.Empty;
    private int _offset;

    private SelectionState? _selection;
    private bool _dragging;

    // The search: its pattern (null when there is none) and the active match, which is the origin of the next Find. Positions are
    // logical, as the selection's are (see TextSelection), so they are kept across wrapping and scrolling.
    private SearchPattern? _search;
    private ActiveMatch? _active;

    /// <summary>
    /// The selection between two cells (see <see cref="TextSelection.CellAt"/>): the cell the press was on, and the cell under
    /// the pointer. It selects nothing until a drag has moved the active end (<see cref="Dragged"/>), so a press and release alone
    /// leave none.
    /// </summary>
    private readonly record struct SelectionState(CellPoints Anchor, CellPoints Active, bool Dragged);

    /// <summary>A cell of the logical text: the point before it and the point after it (see <see cref="TextSelection.CellAt"/>).</summary>
    private readonly record struct CellPoints(LogicalPoint Before, LogicalPoint After);

    /// <summary>A match of the search: its logical line, and its range in that line's text. The active match is the origin of the next Find.</summary>
    private readonly record struct ActiveMatch(int Line, int Start, int Length);

    /// <summary>The highlight of one character: none, a match, or the active match.</summary>
    private enum MatchState
    {
        /// <summary>The character is not in a match.</summary>
        None,

        /// <summary>The character is in a match that is not the active one.</summary>
        Match,

        /// <summary>The character is in the active match.</summary>
        Active,
    }

    /// <summary>The highlight of each character of the logical lines in one draw. A line with no match has a null entry.</summary>
    private sealed record SearchHighlights(MatchState[]?[] Lines);

    /// <summary>The wrapped rows of the content, and the logical text of each line.</summary>
    private sealed record DetailsLayout(IReadOnlyList<DetailsRow> Rows, IReadOnlyList<string> Texts, IReadOnlyList<SelectionRow> SelectionRows);

    public DetailsView(MainController controller)
    {
        _controller = controller;
        CanFocus = true;
        TabStop = TabBehavior.TabStop;
    }

    /// <summary>The scroll position, clamped to the current content and height.</summary>
    // The layout of the last draw and the width it was made for; cleared when a different row is shown.
    private (int Width, DetailsLayout Layout)? _drawn;

    public DetailsScroll Scroll
    {
        get
        {
            int height = Math.Max(0, Viewport.Height);
            int total = DrawnOrArranged(Viewport.Width).Rows.Count;
            return new DetailsScroll(DetailsText.ClampOffset(_offset, total, height), height, total);
        }
    }

    /// <summary>
    /// Shows the row. <paramref name="configDir"/> is where relative working directories resolve. The scroll position goes back
    /// to the top when the item changes, and the selection, the active match and any drag end with it; the search pattern stays.
    /// Showing the same item again keeps them all.
    /// </summary>
    public void Show(ListRow? row, string configDir)
    {
        if (!ReferenceEquals(KeyOf(_row), KeyOf(row)))
        {
            _offset = 0;
            _selection = null;
            _active = null;
            StopDrag();
        }

        _row = row;
        _configDir = configDir;
        _drawn = null;
        SetNeedsDraw();
    }

    /// <summary>
    /// Sets the search pattern. Null ends the search: the highlights go and the active match is dropped. A pattern keeps the active
    /// match as the origin of the next <see cref="Find"/>.
    /// </summary>
    public void SetSearch(SearchPattern? pattern)
    {
        _search = pattern;
        if (pattern is null)
        {
            _active = null;
        }

        SetNeedsDraw();
    }

    /// <summary>
    /// Moves to the next (or previous) match after the active match, or after the top visible row (the end of the last visible row
    /// going backward) when there is none, and scrolls it into view. Past the end (or start) it wraps around the lines and returns
    /// <see cref="SearchResult.Wrapped"/>. Changes nothing when there is no match.
    /// </summary>
    public SearchResult Find(bool backward, bool inclusive)
    {
        SearchPattern? pattern = _search;
        DetailsLayout layout = Arrange(Viewport.Width);
        if (pattern is null || layout.Texts.Count == 0)
        {
            return SearchResult.NotFound;
        }

        int last = layout.Texts.Count - 1;
        IReadOnlyList<IReadOnlyList<SearchMatch>> matches = layout.Texts.Select(text => pattern.Matches(text)).ToList();
        int height = Math.Max(0, Viewport.Height);
        int top = DetailsText.ClampOffset(_offset, layout.Rows.Count, height);

        LogicalPoint origin;
        bool originInclusive;
        if (_active is { } active && active.Line <= last)
        {
            origin = new LogicalPoint(active.Line, active.Start);
            originInclusive = inclusive;
        }
        else
        {
            // Going forward, the top row's own match counts, so the search from the top is inclusive.
            origin = ViewOrigin(layout, top, height, backward);
            originInclusive = inclusive || !backward;
        }

        // Positions compare by line, then by start. Going backward the matches before the origin come first, going forward the ones after it.
        bool Past(int line, int start)
        {
            int order = line != origin.Line ? line.CompareTo(origin.Line) : start.CompareTo(origin.Offset);
            bool inOrder = backward ? order < 0 : order > 0;
            return inOrder || originInclusive && order == 0;
        }

        bool AnyMatch(int line, int start) => true;

        // The first match past the origin, or else, after wrapping, the first match from the other end up to the origin.
        ActiveMatch? match = backward
            ? FirstMatch(matches, origin.Line, 0, backward: true, Past)
            : FirstMatch(matches, origin.Line, last, backward: false, Past);
        bool wrapped = match is null;
        match ??= backward
            ? FirstMatch(matches, last, origin.Line, backward: true, AnyMatch)
            : FirstMatch(matches, 0, origin.Line, backward: false, AnyMatch);
        if (match is not { } target)
        {
            return SearchResult.NotFound;
        }

        _active = target;
        int row = MatchRow(layout.Rows, target);
        ScrollTo(TopShowing(top, row, Math.Max(1, height)));
        SetNeedsDraw();
        return wrapped ? SearchResult.Wrapped : SearchResult.Found;
    }

    /// <summary>
    /// The number of matches in the lines of the text, and the 1-based number of the active match among them (0 when there is no
    /// active match, or it is no longer a match of the pattern).
    /// </summary>
    public SearchStatus SearchStatus()
    {
        SearchPattern? pattern = _search;
        if (pattern is null)
        {
            return new SearchStatus(0, 0);
        }

        IReadOnlyList<string> texts = DrawnOrArranged(Viewport.Width).Texts;
        int count = 0;
        int active = 0;
        for (int line = 0; line < texts.Count; line++)
        {
            IReadOnlyList<SearchMatch> matches = pattern.Matches(texts[line]);
            if (_active is { } current && current.Line == line)
            {
                int index = IndexOfMatch(matches, current);
                if (index >= 0)
                {
                    active = count + index + 1;
                }
            }

            count += matches.Count;
        }

        return new SearchStatus(count, active);
    }

    /// <summary>
    /// The selected text a search starts from: a non-empty selection within one logical line, of at most 200 UTF-16 units. The seed
    /// is the text, the logical line, and the offset of the text in the line's text, which are the units of the matches. Null for
    /// a selection that spans lines, an empty one, one that is too long, or no selection. The text is the one a copy takes.
    /// </summary>
    internal SearchSeed? SelectionSeed()
    {
        if (_selection is null)
        {
            return null;
        }

        IReadOnlyList<string> texts = DetailsText.LogicalTexts(Lines());
        if (SelectedRange(texts) is not { } range || range.Start.Line != range.End.Line)
        {
            return null;
        }

        string text = TextSelection.Extract(texts, range);
        if (text.Length > SeedMaxLength)
        {
            return null;
        }

        return new SearchSeed(text, range.Start.Line, range.Start.Offset);
    }

    /// <summary>
    /// Makes the seed's text the active match, the origin of the next <see cref="Find"/>, so that a Find with <c>inclusive</c> set
    /// takes the selected occurrence first. Call after <see cref="SetSearch"/>. The text is highlighted only when it is a match.
    /// </summary>
    internal void ActivateSeed(SearchSeed seed) =>
        _active = new ActiveMatch((int)seed.Line, seed.Start, seed.Text.Length);

    protected override bool OnKeyDown(Key key)
    {
        if (_controller.OnGlobalKey(key))
        {
            return true;
        }

        if (key == Key.Tab || key == Key.Tab.WithShift)
        {
            _controller.CycleFocusPane(backward: key == Key.Tab.WithShift);
            return true;
        }

        // Esc clears the selection first. With no selection it closes the search (see MainController.OnPaneEscape).
        // It never reaches the application.
        if (key == Key.Esc)
        {
            return HasSelection ? ClearSelection() : _controller.OnPaneEscape();
        }

        if (key == Key.C)
        {
            return CopySelectionOrCommandLine();
        }

        DetailsScroll scroll = Scroll;
        int page = Math.Max(1, scroll.Height);
        if (key == Key.CursorUp)
        {
            return ScrollTo(scroll.Offset - 1);
        }

        if (key == Key.CursorDown)
        {
            return ScrollTo(scroll.Offset + 1);
        }

        if (key == Key.PageUp)
        {
            return ScrollTo(scroll.Offset - page);
        }

        if (key == Key.PageDown)
        {
            return ScrollTo(scroll.Offset + page);
        }

        if (key == Key.Home)
        {
            return ScrollTo(0);
        }

        if (key == Key.End)
        {
            return ScrollTo(scroll.Total);
        }

        // The command keys of the list act on the shown command here too.
        if (_controller.OnShownCommandKey(key))
        {
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            return ScrollTo(Scroll.Offset - WheelLines);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            return ScrollTo(Scroll.Offset + WheelLines);
        }

        if (_dragging)
        {
            // The grab routes every event here, including those outside the view, until the button is released.
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased) || mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                EndDrag();
            }
            else
            {
                DragTo(mouse);
            }

            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            return BeginDrag(mouse);
        }

        return base.OnMouseEvent(mouse);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        DetailsLayout layout = Arrange(width);
        _drawn = (width, layout);
        _offset = DetailsText.ClampOffset(_offset, layout.Rows.Count, height);
        LogicalRange? selection = SelectedRange(layout.Texts);
        SearchHighlights? highlights = _search is { } pattern ? Highlights(pattern, layout.Texts, _active) : null;

        for (int row = 0; row < height; row++)
        {
            int index = _offset + row;
            Move(0, row);
            if (index < layout.Rows.Count)
            {
                DrawRow(layout.Rows[index], width, normal, selection, highlights);
            }
            else
            {
                SetAttribute(normal);
                AddStr(new string(' ', width));
            }
        }

        return true;
    }

    /// <summary>
    /// Draws one wrapped row: each span in its own attribute, then the rest of the width in the normal attribute, so the row is
    /// exactly <paramref name="width"/> columns. The selected characters are drawn in reverse video, and the characters of a search
    /// match in the match colors (the active match in its own), which win over the reverse video. A standalone zero-width
    /// grapheme is not drawn, because the driver advances a cell for it while the layout counts none (see <see cref="Drawn"/>).
    /// </summary>
    private void DrawRow(DetailsRow row, int width, Attribute normal, LogicalRange? selection, SearchHighlights? highlights)
    {
        if (TextWidth.Columns(row.Line.Text) > width)
        {
            // Only a single character wider than the width gets here; it is fitted like any other text.
            SetAttribute(normal);
            AddStr(Drawn(TextWidth.Fit(row.Line.Text, width)));
            return;
        }

        bool[] hidden = ZeroWidthUnits(row.Line.Text);
        int used = 0;
        int offset = row.Start;
        int position = 0;
        foreach (DetailsSpan span in row.Line.Spans)
        {
            Attribute attr = AttributeFor(span.Style, normal);
            string text = span.Text;
            int i = 0;
            while (i < text.Length)
            {
                // A piece is a run of characters with the same selection and highlight state, without the hidden ones.
                bool selected = IsSelected(selection, row.LogicalLine, offset + i);
                MatchState state = StateAt(highlights, row.LogicalLine, offset + i);
                var piece = new StringBuilder();
                int j = i;
                while (j < text.Length
                    && IsSelected(selection, row.LogicalLine, offset + j) == selected
                    && StateAt(highlights, row.LogicalLine, offset + j) == state)
                {
                    int length = TextSelection.RuneAt(text, j).Length;
                    if (!hidden[position + j])
                    {
                        piece.Append(text, j, length);
                    }

                    j += length;
                }

                if (piece.Length > 0)
                {
                    string drawn = piece.ToString();
                    SetAttribute(Highlighted(selected ? Invert(attr) : attr, state));
                    AddStr(drawn);
                    used += TextWidth.Columns(drawn);
                }

                i = j;
            }

            offset += text.Length;
            position += text.Length;
        }

        SetAttribute(normal);
        AddStr(new string(' ', Math.Max(0, width - used)));
    }

    /// <summary>
    /// The matches of the pattern in each logical line, with the active match marked on its line, computed once per draw. A line with
    /// no match has no entry. The active match is marked only when an identical match exists.
    /// </summary>
    private static SearchHighlights Highlights(SearchPattern pattern, IReadOnlyList<string> texts, ActiveMatch? active)
    {
        var lines = new MatchState[]?[texts.Count];
        for (int line = 0; line < texts.Count; line++)
        {
            IReadOnlyList<SearchMatch> matches = pattern.Matches(texts[line]);
            if (matches.Count == 0)
            {
                continue;
            }

            var states = new MatchState[texts[line].Length];
            foreach (SearchMatch match in matches)
            {
                Array.Fill(states, MatchState.Match, match.Start, match.Length);
            }

            if (active is { } current && current.Line == line && IndexOfMatch(matches, current) >= 0)
            {
                Array.Fill(states, MatchState.Active, current.Start, current.Length);
            }

            lines[line] = states;
        }

        return new SearchHighlights(lines);
    }

    /// <summary>The highlight of the character at <paramref name="offset"/> of logical line <paramref name="line"/>, or none without a search.</summary>
    private static MatchState StateAt(SearchHighlights? highlights, int line, int offset) =>
        highlights?.Lines[line] is { } states && offset < states.Length ? states[offset] : MatchState.None;

    /// <summary>The attribute of a character in a search highlight: the match color, or the active color. <paramref name="attr"/> otherwise.</summary>
    private static Attribute Highlighted(Attribute attr, MatchState state) => state switch
    {
        MatchState.Active => SearchColors.Active(attr),
        MatchState.Match => SearchColors.Match(attr),
        _ => attr,
    };

    /// <summary>The index of the match that <paramref name="active"/> names among <paramref name="matches"/> (the same start and length), or -1.</summary>
    private static int IndexOfMatch(IReadOnlyList<SearchMatch> matches, ActiveMatch active)
    {
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i].Start == active.Start && matches[i].Length == active.Length)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The first match, in search order, on the lines from <paramref name="from"/> to <paramref name="to"/> that <paramref name="accept"/>
    /// takes. Going backward the lines and the matches on each line run downward, otherwise upward. Null when none is taken.
    /// </summary>
    private static ActiveMatch? FirstMatch(
        IReadOnlyList<IReadOnlyList<SearchMatch>> matches,
        int from,
        int to,
        bool backward,
        Func<int, int, bool> accept)
    {
        int step = backward ? -1 : 1;
        for (int line = from; line != to + step; line += step)
        {
            IReadOnlyList<SearchMatch> lineMatches = matches[line];
            for (int i = 0; i < lineMatches.Count; i++)
            {
                SearchMatch match = lineMatches[backward ? lineMatches.Count - 1 - i : i];
                if (accept(line, match.Start))
                {
                    return new ActiveMatch(line, match.Start, match.Length);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The origin of a Find without an active match: the start of the top visible row going forward, or the end of the last visible
    /// row going backward. The start of the first line, or the end of the last line, when no row is visible.
    /// </summary>
    private static LogicalPoint ViewOrigin(DetailsLayout layout, int top, int height, bool backward)
    {
        int visible = Math.Min(height, layout.Rows.Count - top);
        if (visible <= 0)
        {
            int last = layout.Texts.Count - 1;
            return backward ? new LogicalPoint(last, layout.Texts[last].Length) : new LogicalPoint(0, 0);
        }

        DetailsRow row = backward ? layout.Rows[top + visible - 1] : layout.Rows[top];
        return new LogicalPoint(row.LogicalLine, backward ? row.End : row.Start);
    }

    /// <summary>
    /// The index of the row that holds the start of the match: the row of its line whose range contains the start, or else the last
    /// row of that line (a start on a space that wrapping dropped).
    /// </summary>
    private static int MatchRow(IReadOnlyList<DetailsRow> rows, ActiveMatch match)
    {
        int found = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].LogicalLine != match.Line)
            {
                continue;
            }

            if (match.Start >= rows[i].Start && match.Start < rows[i].End)
            {
                return i;
            }

            found = i;
        }

        return found;
    }

    /// <summary>
    /// The top row after scrolling so that <paramref name="row"/> is shown: <paramref name="top"/> when it already is, the row itself when
    /// it is above the view, and the row that makes it the last visible one when it is below.
    /// </summary>
    private static int TopShowing(int top, int row, int height)
    {
        if (row < top)
        {
            return row;
        }

        return row >= top + height ? row - height + 1 : top;
    }

    /// <summary>
    /// The UTF-16 units of <paramref name="text"/> that belong to a standalone zero-width grapheme. Such a grapheme is kept in the
    /// logical text (selection and copy use it) but is not drawn, as the output pane does (see <see cref="OutputWrap"/>). A
    /// combining mark that is part of a wider grapheme is drawn with it.
    /// </summary>
    private static bool[] ZeroWidthUnits(string text)
    {
        var hidden = new bool[text.Length];
        foreach ((int offset, int length, int width) in TextWidth.Graphemes(text))
        {
            if (width == 0)
            {
                Array.Fill(hidden, true, offset, length);
            }
        }

        return hidden;
    }

    /// <summary>The text with its standalone zero-width graphemes left out, as it is drawn.</summary>
    private static string Drawn(string text)
    {
        bool[] hidden = ZeroWidthUnits(text);
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (!hidden[i])
            {
                sb.Append(text[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The attribute of a span: a 16-color name, so the colors follow the terminal theme, on the normal background. The bright
    /// names are used where the normal ones have poor contrast on dark backgrounds.
    /// </summary>
    internal static Attribute AttributeFor(DetailsStyle style, Attribute normal)
    {
        Color fg = normal.Foreground;
        Color bg = normal.Background;
        return style switch
        {
            DetailsStyle.Prompt => new Attribute(fg, bg, TextStyle.Faint),
            DetailsStyle.Command => new Attribute(ColorName16.BrightGreen, bg, TextStyle.None),
            DetailsStyle.Label => new Attribute(fg, bg, TextStyle.Faint),
            DetailsStyle.Path => new Attribute(ColorName16.BrightCyan, bg, TextStyle.None),
            DetailsStyle.EnvName => new Attribute(ColorName16.BrightMagenta, bg, TextStyle.None),
            DetailsStyle.EnvValue => new Attribute(fg, bg, TextStyle.None),
            DetailsStyle.Unset => new Attribute(ColorName16.BrightRed, bg, TextStyle.None),
            DetailsStyle.Faint => new Attribute(fg, bg, TextStyle.Faint),
            _ => normal,
        };
    }

    /// <summary>Selected cells use the terminal's reverse video rather than swapped colors, as the output view does.</summary>
    private static Attribute Invert(Attribute attr) => new(attr.Foreground, attr.Background, attr.Style | TextStyle.Reverse);

    private static bool IsSelected(LogicalRange? selection, int line, int offset) =>
        selection is { } range && range.Covers(line, offset);

    private bool ScrollTo(int offset)
    {
        DetailsScroll scroll = Scroll;
        _offset = DetailsText.ClampOffset(offset, scroll.Total, scroll.Height);
        SetNeedsDraw();
        _controller.OnDetailsScrolled();
        return true;
    }

    /// <summary>
    /// Left press: focuses the view and starts a character selection at the cell under the pointer. The mouse is grabbed, so
    /// the drag keeps reporting when the pointer leaves the view.
    /// </summary>
    private bool BeginDrag(Mouse mouse)
    {
        SetFocus();
        DetailsLayout layout = Arrange(Viewport.Width);
        if (layout.Rows.Count == 0)
        {
            return true;
        }

        (LogicalPoint before, LogicalPoint after) = TextSelection.CellAt(
            layout.SelectionRows,
            layout.Texts,
            Scroll.Offset + (mouse.Position?.Y ?? 0),
            mouse.Position?.X ?? 0);
        var cell = new CellPoints(before, after);
        _selection = new SelectionState(cell, cell, Dragged: false);
        _dragging = true;
        App?.Mouse.GrabMouse(this);
        SetNeedsDraw();
        return true;
    }

    /// <summary>
    /// Drag: extends the selection to the cell under the pointer. Above or below the view, the content scrolls by one row per
    /// motion event and the pointer counts as being on the first or last visible row.
    /// </summary>
    private void DragTo(Mouse mouse)
    {
        if (_selection is null)
        {
            return;
        }

        int height = Math.Max(1, Viewport.Height);
        int row = mouse.Position?.Y ?? 0;
        int column = mouse.Position?.X ?? 0;
        if (row < 0)
        {
            ScrollTo(Scroll.Offset - 1);
            row = 0;
        }
        else if (row >= height)
        {
            ScrollTo(Scroll.Offset + 1);
            row = height - 1;
        }

        DetailsLayout layout = Arrange(Viewport.Width);
        if (layout.Rows.Count == 0)
        {
            return;
        }

        (LogicalPoint before, LogicalPoint after) = TextSelection.CellAt(layout.SelectionRows, layout.Texts, Scroll.Offset + row, column);
        _selection = _selection.Value with { Active = new CellPoints(before, after), Dragged = true };
        SetNeedsDraw();
    }

    /// <summary>Left release: releases the grab. A press without a drag leaves no selection.</summary>
    private void EndDrag()
    {
        StopDrag();
        if (_selection is { Dragged: false })
        {
            _selection = null;
        }

        SetNeedsDraw();
    }

    private void StopDrag()
    {
        if (_dragging)
        {
            _dragging = false;
            App?.Mouse.UngrabMouse();
        }
    }

    /// <summary>True when the view has a non-empty selection, which c copies. Read every tick: it returns at once without a selection.</summary>
    internal bool HasSelection => _selection is not null && SelectedRange(DrawnOrArranged(Viewport.Width).Texts) is not null;

    private bool ClearSelection()
    {
        if (_selection is not null)
        {
            _selection = null;
            SetNeedsDraw();
        }

        return true;
    }

    /// <summary>
    /// The selection on the current text, clamped to it: null when there is none or it is empty. The text can change between
    /// redraws (running times, a changed environment), so the stored positions are only trusted after clamping.
    /// </summary>
    private LogicalRange? SelectedRange(IReadOnlyList<string> texts)
    {
        if (_selection is not { } s)
        {
            return null;
        }

        if (!s.Dragged)
        {
            return null;
        }

        LogicalRange cells = CellRange(s);
        LogicalRange range = TextSelection.Clamp(cells.Start, cells.End, texts);
        return range.IsEmpty ? null : range;
    }

    /// <summary>
    /// The characters from the anchor cell to the active cell, both cells included: from the start of the earlier cell to the end of
    /// the later one. A drag forwards ends after the pointer's cell, and a drag backwards starts at it.
    /// </summary>
    private static LogicalRange CellRange(SelectionState s) =>
        s.Active.Before.CompareTo(s.Anchor.Before) >= 0
            ? new LogicalRange(s.Anchor.Before, s.Active.After)
            : new LogicalRange(s.Active.Before, s.Anchor.After);

    /// <summary>
    /// Copies the selection. Without one, copies the command line of a command row, as the command list does (a group row
    /// copies nothing). The text goes to the controller, which writes it to the clipboard and reports the result.
    /// </summary>
    private bool CopySelectionOrCommandLine()
    {
        IReadOnlyList<string> texts = DetailsText.LogicalTexts(Lines());
        if (SelectedRange(texts) is { } range)
        {
            string text = TextSelection.Extract(texts, range);
            _controller.CopyToClipboard(text, $"Copied {CharacterCount(text)} characters");
            return true;
        }

        _controller.CopyCommandLine(_row);
        return true;
    }

    private static int CharacterCount(string text) => text.EnumerateRunes().Count();

    /// <summary>The rows of the content wrapped to <paramref name="width"/>, with the logical text. Empty for a non-positive width.</summary>
    /// <summary>
    /// The layout last drawn at <paramref name="width"/>, else a fresh one. For the frame title, read every 100 ms: it describes
    /// what is on screen, and every change to the content (a different row, an edit, a state change) redraws the view.
    /// Building a layout copies the process environment and wraps every line, which is too much to repeat on every tick.
    /// </summary>
    private DetailsLayout DrawnOrArranged(int width) =>
        _drawn is { } drawn && drawn.Width == width ? drawn.Layout : Arrange(width);

    private DetailsLayout Arrange(int width)
    {
        if (width <= 0)
        {
            return new DetailsLayout(Array.Empty<DetailsRow>(), Array.Empty<string>(), Array.Empty<SelectionRow>());
        }

        IReadOnlyList<DetailsLine> lines = Lines();
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, width);
        return new DetailsLayout(rows, DetailsText.LogicalTexts(lines), DetailsText.SelectionRows(rows));
    }

    private IReadOnlyList<DetailsLine> Lines() => _row switch
    {
        CommandRow command => DetailsText.CommandLines(
            command.Entry,
            _configDir,
            OperatingSystem.IsWindows(),
            EffectiveEnv.Process(),
            Path.PathSeparator,
            _controller.KnownEnvironmentOf(command.Entry),
            _controller.EnvScriptsNotRunFor(command.Entry),
            _controller.SecretNamesOf(command.Entry)),
        GroupRow group => DetailsText.GroupLines(
            group.Group,
            _configDir,
            _controller.LastEnvScriptRunOf(group.Group),
            _controller.SecretNamesOf(group.Group),
            _controller.KnownGroupEnvironmentOf(group.Group)),
        FileRow file => DetailsText.FileLines(
            file.Source,
            EffectiveEnv.Process(),
            OperatingSystem.IsWindows(),
            Path.PathSeparator,
            _controller.LastEnvScriptRunOf(file.Source),
            _controller.SecretNamesOf(file.Source)),
        _ => Array.Empty<DetailsLine>(),
    };

    /// <summary>The item a row shows. Rows are rebuilt on every structure change, so the item is compared, not the row.</summary>
    private static object? KeyOf(ListRow? row) => row switch
    {
        CommandRow command => command.Entry,
        GroupRow group => group.Group,
        FileRow file => file.Source,
        _ => null,
    };
}
