using System.Globalization;
using System.Text;
using Logvu.Core.Config;
using Logvu.Core.Detail;
using Logvu.Core.Store;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tui;

/// <summary>
/// The full-screen detail of one event: its <see cref="DetailDocument"/> wrapped to the width, with a cursor on one logical line. It draws
/// the rows that are visible and navigates the document. It owns the expansion state of the event, which is discarded with the view.
/// </summary>
/// <remarks>
/// <para>
/// The viewport is a position in the wrapped document: the top line and the row of that line where the view starts. Rows are computed
/// lazily (see <see cref="DetailWrap"/>), per line and width, and cached until the document or the width changes. Moving down needs only
/// the rows being shown; moving up or scrolling to a position above needs the row counts of the lines above, which are computed once.
/// </para>
/// <para>
/// The cursor is always on a logical line. Its first row is kept in view when the cursor moves; a line taller than the view shows from
/// its first row. A click selects the line of the row it is on, without moving the viewport.
/// </para>
/// </remarks>
internal sealed class DetailView : View
{
    /// <summary>The number of rows the wheel moves the viewport by, per event.</summary>
    internal const int WheelRows = 3;

    private readonly Dictionary<int, DetailWrap> _wraps = [];
    private EventDetail? _detail;
    private LogvuConfig? _config;
    private DetailDocument? _document;
    private int _wrapWidth = -1;
    private int _cursor;
    private int _topLine;
    private int _topRow;
    private string? _placeholder;
    private SearchPattern? _search;

    /// <summary>Creates the view. It draws its placeholder until a detail is loaded with <see cref="Load"/>.</summary>
    public DetailView()
    {
        CanFocus = true;
    }

    /// <summary>Raised when the user asks to leave the detail (Esc). The owner removes the view.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised after the document is rebuilt: a fold or node was toggled, the raw view changed, or a detail was loaded.</summary>
    public event Action? DocumentChanged;

    /// <summary>
    /// Raised by <c>f</c> with the line under the cursor. The owner decides what the line filters on; the view does not judge the line.
    /// </summary>
    public event Action<DetailLine>? FilterRequested;

    /// <summary>The detail shown, or <c>null</c> until one is loaded.</summary>
    public EventDetail? Detail => _detail;

    /// <summary>The document shown under the view's current options, or <c>null</c> until a detail is loaded.</summary>
    public DetailDocument? Document => _document;

    /// <summary>The index of the cursor's line in <see cref="Document"/>'s visible lines.</summary>
    internal int CursorLine => _cursor;

    /// <summary>
    /// The search whose matches are highlighted in the lines. Null for none. The matches are those of each full line's text, so a match
    /// that wraps is drawn across its rows.
    /// </summary>
    public SearchPattern? Search
    {
        get => _search;
        set
        {
            _search = value;
            SetNeedsDraw();
        }
    }

    /// <summary>The match the search last moved to, drawn in the active colors, or <c>null</c> for none.</summary>
    internal DetailHit? ActiveHit { get; private set; }

    /// <summary>
    /// The text shown on the first row while no detail is loaded, for example "Loading…". Null for none.
    /// </summary>
    public string? Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value;
            SetNeedsDraw();
        }
    }

    /// <summary>
    /// Shows a detail with every node and fold in its default state, in the structured view, with the cursor on the first line.
    /// </summary>
    /// <param name="detail">The detail to show.</param>
    /// <param name="config">The configuration: the highlight rules, the framework prefixes and the styles.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public void Load(EventDetail detail, LogvuConfig config)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(config);

        _detail = detail;
        _config = config;
        ActiveHit = null;
        Rebuild(DetailOptions.Default, resetPosition: true);
    }

    /// <summary>
    /// Scrolls to a match and moves the cursor onto its line, revealing the line first when a collapsed node or fold hides it. The match
    /// becomes the active one. Does nothing while no detail is loaded.
    /// </summary>
    /// <param name="hit">The match, with the index of its line among the full lines of <see cref="Document"/>.</param>
    internal void ShowHit(DetailHit hit)
    {
        if (_document is null)
        {
            return;
        }

        if (_document.VisibleIndex(hit.Line) < 0)
        {
            Rebuild(_document.ExpandToReveal(hit.Line), resetPosition: false);
        }

        _cursor = _document!.VisibleIndex(hit.Line);
        ActiveHit = hit;
        EnsureCursorVisible();
        SetNeedsDraw();
    }

    /// <summary>Forgets the active match, so that no match is drawn in the active colors.</summary>
    internal void ClearActiveHit()
    {
        ActiveHit = null;
        SetNeedsDraw();
    }

    /// <summary>
    /// Handles a detail key. Returns <c>false</c> for a key the detail does not bind, so it reaches the window. Esc always asks to close.
    /// </summary>
    /// <param name="key">The key.</param>
    internal bool HandleKey(Key key)
    {
        if (key == Key.Esc)
        {
            CloseRequested?.Invoke();
            return true;
        }

        if (_document is null)
        {
            return false;
        }

        if (key == Key.CursorUp)
        {
            MoveCursor(_cursor - 1);
        }
        else if (key == Key.CursorDown)
        {
            MoveCursor(_cursor + 1);
        }
        else if (key == Key.PageUp)
        {
            PageCursor(-1);
        }
        else if (key == Key.PageDown)
        {
            PageCursor(1);
        }
        else if (key == Key.Home)
        {
            MoveCursor(0);
        }
        else if (key == Key.End)
        {
            MoveCursor(LineCount - 1);
        }
        else if (key == Key.Enter || key == Key.Space)
        {
            ToggleCursorNode();
        }
        else if (key == Key.CursorLeft)
        {
            CollapseCursorNode();
        }
        else if (key == Key.CursorRight)
        {
            ExpandCursorNode();
        }
        else if (key == Key.Z)
        {
            Rebuild(_document.ToggleAllFolds(), resetPosition: false);
        }
        else if (key == Key.R)
        {
            Rebuild(_document.Options with { Raw = !_document.Options.Raw }, resetPosition: true);
        }
        else if (key == Key.F)
        {
            if (_cursor < _document.Lines.Count)
            {
                FilterRequested?.Invoke(_document.Lines[_cursor]);
            }
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles a mouse event. A wheel event moves the viewport by <see cref="WheelRows"/> rows. A left press selects the line of the row
    /// it is on, without moving the viewport.
    /// </summary>
    /// <param name="mouse">The mouse event.</param>
    /// <returns><c>true</c> when the event was a wheel or press event over the document.</returns>
    internal bool HandleMouse(Mouse mouse)
    {
        if (_document is null)
        {
            return false;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            ScrollBy(-WheelRows);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            ScrollBy(WheelRows);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            int row = mouse.Position?.Y ?? 0;
            if (Forward(ViewTop, row) is { } target)
            {
                _cursor = target.Line;
                SetNeedsDraw();
            }

            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key) => HandleKey(key) || base.OnKeyDown(key);

    /// <inheritdoc/>
    protected override bool OnMouseEvent(Mouse mouse) => HandleMouse(mouse) || base.OnMouseEvent(mouse);

    /// <inheritdoc/>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        // The row a position names depends on the width, so a resize moves the viewport back to the top row of its line.
        if (WrapWidth != _wrapWidth && _wrapWidth != -1)
        {
            _topRow = 0;
        }

        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        Attribute focus = GetAttributeForRole(VisualRole.Focus);
        if (_document is null || _config is null)
        {
            DrawBlank(0, height, width, normal);
            if (_placeholder is { } text && height > 0)
            {
                Move(0, 0);
                SetAttribute(normal);
                AddStr(TextWidth.Fit(text, width));
            }

            return true;
        }

        ViewTop = Clamp(ViewTop);
        int screen = 0;
        int line = _topLine;
        int row = _topRow;
        while (screen < height && line < LineCount)
        {
            IReadOnlyList<SearchMatch>? matches = _search?.Matches(_document.Lines[line].Text);
            int activeStart = ActiveHit is { } hit && hit.Line == _document.FullIndex(line) ? hit.Start : -1;
            foreach (WrapRow wrapRow in Wrap(line).Rows(row, height - screen))
            {
                DrawRow(screen, line, wrapRow, width, normal, focus, matches, activeStart);
                screen++;
            }

            line++;
            row = 0;
        }

        DrawBlank(screen, height, width, normal);
        return true;
    }

    /// <summary>The width the rows are wrapped to: the viewport's width, at least 1.</summary>
    private int WrapWidth => Math.Max(1, Viewport.Width);

    /// <summary>The viewport's height, at least 1.</summary>
    private int ViewportRows => Math.Max(1, Viewport.Height);

    /// <summary>The number of visible lines of the document, or 0 before one is loaded.</summary>
    private int LineCount => _document?.Lines.Count ?? 0;

    /// <summary>The top of the viewport, as a position in the wrapped document.</summary>
    private Pos ViewTop
    {
        get => new(_topLine, _topRow);
        set
        {
            _topLine = value.Line;
            _topRow = value.Row;
        }
    }

    /// <summary>
    /// Rebuilds the document with new options. The cursor and the viewport stay on the lines they were on, or on the nearest line above
    /// that is still shown (a line inside a collapsed fold shows as the fold's marker). With <paramref name="resetPosition"/> they go to
    /// the top.
    /// </summary>
    private void Rebuild(DetailOptions options, bool resetPosition)
    {
        if (_detail is null || _config is null)
        {
            return;
        }

        DetailDocument? previous = _document;
        DetailDocument document = DetailDocument.Build(_detail, _config, options);

        // The full lines of the raw view are not the structured ones, so the active match of one does not name a line of the other.
        if (previous is not null && previous.Options.Raw != options.Raw)
        {
            ActiveHit = null;
        }

        int cursor = 0;
        Pos top = new(0, 0);
        if (!resetPosition && previous is { Lines.Count: > 0 })
        {
            int fullCursor = previous.FullIndex(_cursor);
            int fullTop = previous.FullIndex(_topLine);
            int topVisible = document.VisibleIndex(fullTop);
            top = topVisible >= 0 ? new Pos(topVisible, _topRow) : new Pos(VisibleAtOrAbove(document, fullTop), 0);
            cursor = VisibleAtOrAbove(document, fullCursor);
        }

        _document = document;
        _wraps.Clear();
        _cursor = cursor;
        ViewTop = top;
        EnsureCursorVisible();
        DocumentChanged?.Invoke();
        SetNeedsDraw();
    }

    /// <summary>The visible index of a full-document line, or of the nearest line above it that is shown.</summary>
    private static int VisibleAtOrAbove(DetailDocument document, int fullIndex)
    {
        int full = fullIndex;
        while (full > 0 && document.VisibleIndex(full) < 0)
        {
            full--;
        }

        return Math.Max(0, document.VisibleIndex(full));
    }

    /// <summary>The wrapping of a visible line at the current width, cached until the document or the width changes.</summary>
    private DetailWrap Wrap(int line)
    {
        int width = WrapWidth;
        if (width != _wrapWidth)
        {
            _wraps.Clear();
            _wrapWidth = width;
        }

        if (!_wraps.TryGetValue(line, out DetailWrap? wrap))
        {
            wrap = new DetailWrap(_document!.Lines[line], width);
            _wraps[line] = wrap;
        }

        return wrap;
    }

    /// <summary>The position <paramref name="rows"/> rows after <paramref name="from"/>, or <c>null</c> when that is past the last row.</summary>
    private Pos? Forward(Pos from, int rows)
    {
        int line = from.Line;
        int row = from.Row;
        int remaining = rows;
        while (line < LineCount)
        {
            int available = Wrap(line).Rows(row, remaining + 1).Count;
            if (available == remaining + 1)
            {
                return new Pos(line, row + remaining);
            }

            // The line ends after `available` rows from `row`: that many steps reach the next line's first row.
            remaining -= available;
            line++;
            row = 0;
        }

        return null;
    }

    /// <summary>The position <paramref name="rows"/> rows before <paramref name="from"/>, or the document's first row when that is before it.</summary>
    private Pos Backward(Pos from, int rows)
    {
        int line = from.Line;
        int row = from.Row;
        int remaining = rows;
        while (true)
        {
            if (remaining <= row)
            {
                return new Pos(line, row - remaining);
            }

            // From row 0 of this line, one more step reaches the last row of the line above.
            remaining -= row + 1;
            line--;
            if (line < 0)
            {
                return new Pos(0, 0);
            }

            row = Wrap(line).RowCount - 1;
        }
    }

    /// <summary>The last position at which the viewport can start: the document's last row is on the viewport's bottom row.</summary>
    private Pos MaxTop()
    {
        if (LineCount == 0)
        {
            return new Pos(0, 0);
        }

        int last = LineCount - 1;
        return Backward(new Pos(last, Wrap(last).RowCount - 1), ViewportRows - 1);
    }

    /// <summary>
    /// The top clamped to the range a viewport can start at. A top with a full viewport of rows after it is kept as it is, so the rows
    /// are measured only as far as the viewport shows them; the document's end is measured only when a viewport would pass it.
    /// </summary>
    private Pos Clamp(Pos top)
    {
        if (LineCount == 0 || Forward(top, ViewportRows - 1) is not null)
        {
            return top;
        }

        Pos max = MaxTop();
        return Compare(top, max) > 0 ? max : top;
    }

    private static int Compare(Pos a, Pos b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Row.CompareTo(b.Row);

    /// <summary>
    /// Moves the cursor to a visible line, and scrolls so that the line's first row is in view: up to the top when it is above the
    /// viewport, and so that it ends the viewport when it is below.
    /// </summary>
    private void MoveCursor(int line)
    {
        if (LineCount == 0)
        {
            return;
        }

        _cursor = Math.Clamp(line, 0, LineCount - 1);
        EnsureCursorVisible();
        SetNeedsDraw();
    }

    private void EnsureCursorVisible()
    {
        if (LineCount == 0)
        {
            return;
        }

        var head = new Pos(_cursor, 0);
        Pos top = ViewTop;
        if (Compare(top, head) > 0)
        {
            top = head;
        }
        else
        {
            // The viewport must start at or after the position that puts the cursor's rows on its last row. A line taller than the
            // viewport is measured only as far as the viewport: its first row is then the one kept on the bottom.
            int shown = Wrap(_cursor).Rows(0, ViewportRows).Count;
            Pos bottom = shown >= ViewportRows ? head : new Pos(_cursor, shown - 1);
            Pos floor = Backward(bottom, ViewportRows - 1);
            if (Compare(top, floor) < 0)
            {
                top = floor;
            }
        }

        ViewTop = Clamp(top);
    }

    /// <summary>Moves the cursor by a viewport of rows, or by one line when the viewport is taller than the cursor's line.</summary>
    private void PageCursor(int direction)
    {
        var from = new Pos(_cursor, 0);
        Pos? moved = direction > 0 ? Forward(from, ViewportRows) : Backward(from, ViewportRows);
        int line = moved?.Line ?? (direction > 0 ? LineCount - 1 : 0);
        if (line == _cursor)
        {
            line = Math.Clamp(_cursor + direction, 0, LineCount - 1);
        }

        MoveCursor(line);
    }

    /// <summary>Scrolls the viewport by rows without moving the cursor, and stops at the document's end.</summary>
    private void ScrollBy(int rows)
    {
        Pos? target = rows >= 0 ? Forward(ViewTop, rows) : Backward(ViewTop, -rows);
        ViewTop = Clamp(target ?? MaxTop());
        SetNeedsDraw();
    }

    private string? CursorNodeId() => _document is { } document && _cursor < document.Lines.Count ? document.Lines[_cursor].NodeId : null;

    private void ToggleCursorNode()
    {
        if (CursorNodeId() is { } id)
        {
            Rebuild(_document!.Toggle(id), resetPosition: false);
        }
    }

    private void CollapseCursorNode()
    {
        if (CursorNodeId() is { } id && _document!.IsExpanded(id))
        {
            Rebuild(_document.Toggle(id), resetPosition: false);
        }
    }

    private void ExpandCursorNode()
    {
        if (CursorNodeId() is { } id && !_document!.IsExpanded(id))
        {
            Rebuild(_document.Toggle(id), resetPosition: false);
        }
    }

    /// <summary>
    /// Draws one wrapped row: the indent in the line's attribute, then each span in its own attribute, then the padding to the width. The
    /// search matches of the line are drawn in the match colors over the spans, and the active one in the active colors.
    /// </summary>
    private void DrawRow(
        int screen,
        int lineIndex,
        WrapRow row,
        int width,
        Attribute normal,
        Attribute focus,
        IReadOnlyList<SearchMatch>? matches,
        int activeStart)
    {
        DetailLine line = _document!.Lines[lineIndex];
        bool selected = lineIndex == _cursor;
        Attribute baseline = selected ? focus : normal;

        Move(0, screen);
        SetAttribute(baseline);
        int used = 0;
        if (row.Indent > 0)
        {
            AddStr(new string(' ', row.Indent));
            used = row.Indent;
        }

        foreach (Piece piece in Pieces(line, row, normal, matches, activeStart))
        {
            Attribute attribute = selected ? DetailColors.Selected(piece.Attribute, focus) : piece.Attribute;
            SetAttribute(SearchHighlight.Paint(attribute, piece.Kind));
            AddStr(piece.Text);
            used += piece.Columns;
        }

        if (used < width)
        {
            SetAttribute(baseline);
            AddStr(new string(' ', width - used));
        }
    }

    /// <summary>
    /// The text of a row in runs of one attribute: the spans that overlap the row in their attributes, the rest in the normal one, each cut
    /// where a search match starts or ends. Zero-width graphemes are not drawn, and a control character is drawn as a space, as the
    /// grapheme widths count them.
    /// </summary>
    private List<Piece> Pieces(DetailLine line, WrapRow row, Attribute normal, IReadOnlyList<SearchMatch>? matches, int activeStart)
    {
        var pieces = new List<Piece>();
        int start = row.Start;
        int end = row.Start + row.Length;
        int position = start;

        void Add(int from, int to, Attribute attribute)
        {
            foreach (SearchRun run in SearchHighlight.Split(line.Text, from, to, matches, match => match.Start == activeStart))
            {
                AddPiece(pieces, line.Text, run.Start, run.End, attribute, run.Kind);
            }
        }

        foreach (StyledSpan span in line.Spans)
        {
            int from = Math.Max(span.Start, start);
            int to = Math.Min(span.Start + span.Length, end);
            if (from >= to)
            {
                continue;
            }

            if (from > position)
            {
                Add(position, from, normal);
            }

            Add(from, to, DetailColors.For(span, normal, _config!));
            position = to;
        }

        if (position < end)
        {
            Add(position, end, normal);
        }

        return pieces;
    }

    private static void AddPiece(List<Piece> pieces, string text, int from, int to, Attribute attribute, SearchRunKind kind)
    {
        ReadOnlySpan<char> slice = text.AsSpan(from, to - from);
        var drawn = new StringBuilder(slice.Length);
        int columns = 0;
        int i = 0;
        while (i < slice.Length)
        {
            int length = StringInfo.GetNextTextElementLength(slice[i..]);
            ReadOnlySpan<char> grapheme = slice.Slice(i, length);
            int width = TextWidth.WidthOf(grapheme);
            if (width > 0)
            {
                if (char.IsControl(grapheme[0]))
                {
                    drawn.Append(' ');
                }
                else
                {
                    drawn.Append(grapheme);
                }

                columns += width;
            }

            i += length;
        }

        if (drawn.Length > 0)
        {
            pieces.Add(new Piece(drawn.ToString(), attribute, columns, kind));
        }
    }

    /// <summary>Fills rows <paramref name="from"/> to <paramref name="to"/> (exclusive) with blanks in the normal attribute.</summary>
    private void DrawBlank(int from, int to, int width, Attribute normal)
    {
        for (int row = from; row < to; row++)
        {
            Move(0, row);
            SetAttribute(normal);
            AddStr(new string(' ', width));
        }
    }

    /// <summary>A run of text of one attribute, with its width in columns and its search highlight.</summary>
    private readonly record struct Piece(string Text, Attribute Attribute, int Columns, SearchRunKind Kind);

    /// <summary>A position in the wrapped document: a visible line and a row of that line.</summary>
    private readonly record struct Pos(int Line, int Row);
}
