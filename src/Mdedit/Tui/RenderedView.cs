using System.Collections.Immutable;
using System.Text;
using Mdedit.Core.Render;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Cell = Mdedit.Core.Render.Cell;
using Point = System.Drawing.Point;

namespace Mdedit.Tui;

/// <summary>
/// The rendered document in a view: the display lines of its layout drawn from a top line, with the caret on a caret stop. A caret stop is
/// a mapped cell, or the end position of a block (after its last character). The caret moves over stops only (see
/// <see cref="HandleKey"/>), keeps its source offset across a width change, and is shown as the terminal cursor. The wheel scrolls
/// without moving the cursor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cursor.</b> The cursor is a line, the index of a cell in that line (the index after the last cell is the end position of a
/// block), the screen column and the source offset. The caret is a bar before a character, so a selection is the half-open range from
/// the anchor to the caret. Up and Down keep a goal column, which only the horizontal moves, Home, End and a click change.
/// </para>
/// <para>
/// <b>Scrolling.</b> Keys keep the cursor on screen by the least scrolling. The wheel scrolls <see cref="WheelLines"/> lines.
/// </para>
/// <para>
/// <b>Reflow.</b> On a width change the layout is rebuilt. The anchor is the cursor's offset when the cursor is on screen, and the
/// first mapped cell of the top line otherwise. The anchor keeps its screen row (the cursor's row, or the top row), so the cursor's
/// offset is the same after the change and its line stays where it was on screen.
/// </para>
/// <para>
/// <b>Folds and tables.</b> The fold state and its keys are in the folding part of the class, and the horizontal offsets of the wide
/// blocks (tables) in the scrolling part. Screen columns of a scrolled block are converted to layout columns through its offset.
/// </para>
/// <para>
/// <b>Selection.</b> A selection is held as offsets: an anchor (set by a Shift movement, or by the first motion of a drag), which
/// the cursor's offset ends, or an exact range (set by <see cref="SelectRange"/>, for a search match or a word). A plain movement, a
/// press or a right-click outside the selection clears it. A drag scrolls one line per motion event while the mouse is above or below
/// the view.
/// </para>
/// </remarks>
internal sealed partial class RenderedView : View
{
    /// <summary>The display lines the wheel scrolls per event.</summary>
    internal const int WheelLines = 3;

    private RenderDocument _document;
    private readonly LayoutCache _cache = new();
    private DocumentLayout? _layout;
    private int _top;
    private int _line;
    private int _cell = -1;
    private int _column;
    private int _offset;
    private int _goal;
    private int? _anchor;
    private SourceRange? _range;
    private int _press;
    private bool _dragging;
    private ImmutableArray<SourceRange> _matches = ImmutableArray<SourceRange>.Empty;
    private int _active = -1;

    /// <summary>Creates the view over a document. It lays out at the first width it is given.</summary>
    /// <param name="document">The document to show.</param>
    /// <param name="tracker">
    /// Creates the positions that the fold state and the table scroll offsets are kept as, so that they follow the text when a new document
    /// is set (see <see cref="SetDocument"/>). Null keeps them at their offsets, for a text that is not edited.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <c>null</c>.</exception>
    public RenderedView(RenderDocument document, IOffsetTracker? tracker = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _tracker = tracker ?? FixedOffsetTracker.Instance;
        CanFocus = true;
    }

    /// <summary>Raised after the cursor's source offset or the top line changes, so that the message line can follow.</summary>
    public event Action? PositionChanged;

    /// <summary>Raised when a key reaches the view, before it is handled, so that the window can clear a transient message.</summary>
    public event Action? KeyPressed;

    /// <summary>
    /// Raised when a key that is not a text edit (see <see cref="IsEditKey"/>) reaches the view, or the mouse places the cursor, before the
    /// view handles it. The window ends the undo run of typed text then, since the cursor may move or a command may run.
    /// </summary>
    public event Action? CommandPressed;

    /// <summary>
    /// Raised by a right-click on the view, with the mouse's screen position, so that the window can open its context menu there. The
    /// cursor and the selection are settled before it is raised (a right-click outside the selection places the cursor).
    /// </summary>
    public event Action<Point>? ContextMenuRequested;

    /// <summary>
    /// Raised with the text of a paste that reaches the focused view (a bracketed paste from the terminal), sanitized as the view sanitizes it. The
    /// window writes the text as Markdown source (see <c>MainWindow.ViewPaste.cs</c>).
    /// </summary>
    public event Action<string>? PasteRequested;

    /// <summary>The document shown.</summary>
    public RenderDocument Document => _document;

    /// <summary>
    /// The selected source range, or null when nothing is selected: the exact range of <see cref="SelectRange"/>, or else the half-open
    /// range from the anchor to the cursor (null when they are the same offset).
    /// </summary>
    public SourceRange? SelectionRange
    {
        get
        {
            if (_range is { } range)
            {
                return range;
            }

            if (_anchor is not { } anchor || !HasCursor)
            {
                return null;
            }

            // The caret is a bar before a character, so the selection is the half-open range between the anchor and the caret.
            int start = Math.Min(anchor, _offset);
            int end = Math.Max(anchor, _offset);
            return start == end ? null : new SourceRange(start, end);
        }
    }

    /// <summary>
    /// The offset that the selection starts from, the end that is not the cursor; null when nothing is selected. With the cursor it gives
    /// the selection exactly (see <see cref="PlaceCaret"/>).
    /// </summary>
    public int? SelectionAnchor => SelectionRange is { } range ? _offset == range.End ? range.Start : range.End : null;

    /// <summary>The screen position of the cursor, for a menu opened from the keyboard: on the virtual line when the view shows one.</summary>
    public Point CursorScreenPosition => ViewportToScreen(new Point(VisibleColumn, CursorRow));

    /// <summary>
    /// The cursor's screen column, kept inside the viewport: an end position on a full line sits on the last column. The inline pending insertion
    /// (see <see cref="Pending"/>) is drawn before the cursor, so the cursor is after it. On a virtual line the cursor is after the gutter and the
    /// pending's display text.
    /// </summary>
    private int VisibleColumn
    {
        get
        {
            int column = HasVirtualLine ? GutterWidth + VirtualText.Length : CaretScreenColumn() + PendingWidth;
            return Math.Min(column, Math.Max(0, Viewport.Width - 1));
        }
    }

    /// <summary>
    /// True when the document has a caret stop to rest the cursor on. A document with none (empty, or only rules) has no cursor.
    /// </summary>
    public bool HasCursor => _cell >= 0;

    /// <summary>The absolute source offset of the cursor's cell, or 0 when there is no cursor.</summary>
    public int CursorOffset => _offset;

    /// <summary>
    /// Whether the cursor is at the edge of its display line: on the line's first mapped cell (when <paramref name="start"/> is set), or at its end
    /// position. The window takes an edit there as one that may join or unwrap blocks (see <c>MainWindow.DeleteInView</c>).
    /// </summary>
    /// <param name="start">True for the line's start, false for its end.</param>
    public bool CursorAtLineEdge(bool start) =>
        HasCursor && (start ? _cell == EdgeMappedCell(_line, first: true) : _cell == Built.Line(_line).Cells.Length);

    /// <summary>The display line of the cursor.</summary>
    public int CursorLine => _line;

    /// <summary>The screen column of the cursor's cell, within the layout.</summary>
    public int CursorColumn => _column;

    /// <summary>The first display line in the viewport.</summary>
    public int TopLine => _top;

    /// <summary>The number of display lines of the layout, separators included; 0 before the first layout.</summary>
    public int LineCount => _layout?.LineCount ?? 0;

    /// <summary>The number of display lines the viewport shows.</summary>
    public int PageLines => ViewportHeight;

    /// <summary>
    /// The absolute source offset of the top line's first mapped cell, or the next mapped cell after the line when it has none. It is
    /// 0 before the first layout.
    /// </summary>
    public int TopOffset => _layout is { LineCount: > 0 } layout ? layout.OffsetAt(_top, 0) : 0;

    /// <summary>
    /// Handles a key on the cursor: the movements of <see cref="HandleKey"/> are the arrows, Home and End (with Ctrl for the document's
    /// ends) and PgUp and PgDn, and the folding keys. Other keys are not handled, so the window sees them; a key that is not a text edit
    /// raises <see cref="CommandPressed"/> first.
    /// </summary>
    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        KeyPressed?.Invoke();
        if (!IsEditKey(key))
        {
            CommandPressed?.Invoke();
        }

        return HandleKey(key) || base.OnKeyDown(key);
    }

    /// <inheritdoc/>
    protected override bool OnMouseEvent(Mouse mouse) => HandleMouse(mouse) || base.OnMouseEvent(mouse);

    /// <summary>
    /// Takes a paste that the focused view receives: the text is handed to <see cref="PasteRequested"/>, and the paste is consumed. The base class
    /// has already sanitized the text (see <c>OnSanitizingPaste</c>).
    /// </summary>
    /// <param name="text">The sanitized text.</param>
    protected override bool OnPaste(string text)
    {
        PasteRequested?.Invoke(text);
        return true;
    }

    /// <summary>The text of a paste as the view sanitizes it (control characters removed), for a paste the window reads from the clipboard.</summary>
    /// <param name="raw">The text as read.</param>
    internal string SanitizePaste(string raw) => OnSanitizingPaste(raw);

    /// <summary>The viewport's width changed: the layout is rebuilt for it, and the cursor and top line are placed (see the remarks).</summary>
    /// <param name="e">The change.</param>
    protected override void OnViewportChanged(DrawEventArgs e)
    {
        base.OnViewportChanged(e);
        SyncLayout();
    }

    /// <inheritdoc/>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        SyncLayout();
        if (_layout is null)
        {
            DrawBlank(0, height, width, normal);
            return true;
        }

        ClampTop();
        int y = 0;
        for (; y < height; y++)
        {
            if (HasVirtualLine && y == VirtualRow)
            {
                DrawVirtualLine(y, width, normal);
                continue;
            }

            int line = LineAtRow(y);
            if (line >= _layout.LineCount)
            {
                break;
            }

            DrawLine(y, line, width, normal);
        }

        DrawBlank(y, height, width, normal);
        UpdateCursor();
        return true;
    }

    /// <summary>
    /// Handles a key: a movement of the cursor or a scroll. Returns false, and does nothing, for any other key or when the document has
    /// no cursor.
    /// </summary>
    /// <param name="key">The key.</param>
    internal bool HandleKey(Key key)
    {
        if (!ApplyKey(key))
        {
            return false;
        }

        // A move or a fold change drops the pending insertion, which is written by the next typed character only.
        SetPending(null);
        // A move can scroll, and later the selection and highlights follow the cursor, so the content is drawn again.
        SetNeedsDraw();
        return true;
    }

    /// <summary>
    /// Applies a key to the view state. Returns true when it was handled. Shift with an arrow, Home, End, PgUp or PgDn extends the
    /// selection by the same movement; any other movement clears it.
    /// </summary>
    private bool ApplyKey(Key key)
    {
        if (!SyncLayout())
        {
            return false;
        }

        if (FoldKey(key))
        {
            return true;
        }

        if (!HasCursor)
        {
            return false;
        }

        bool extend = key.IsShift && IsExtendable(key.NoShift);
        Key move = extend ? key.NoShift : key;
        if (!IsMovement(move))
        {
            return false;
        }

        int start = _offset;
        if (!extend)
        {
            _anchor = null;
        }

        _range = null;

        if (move == Key.CursorLeft)
        {
            MoveHorizontal(-1);
        }
        else if (move == Key.CursorRight)
        {
            MoveHorizontal(1);
        }
        else if (move == Key.CursorLeft.WithCtrl)
        {
            MoveWord(forward: false);
        }
        else if (move == Key.CursorRight.WithCtrl)
        {
            MoveWord(forward: true);
        }
        else if (move == Key.CursorUp)
        {
            MoveVertical(-1);
        }
        else if (move == Key.CursorDown)
        {
            MoveVertical(1);
        }
        else if (move == Key.Home.WithCtrl)
        {
            MoveTo(FirstMapped());
        }
        else if (move == Key.End.WithCtrl)
        {
            MoveTo(LastStop());
        }
        else if (move == Key.Home)
        {
            MoveToLineEnd(first: true);
        }
        else if (move == Key.End)
        {
            MoveToLineEnd(first: false);
        }
        else if (move == Key.PageUp)
        {
            MovePage(-1);
        }
        else
        {
            MovePage(1);
        }

        if (extend && _anchor is null && _offset != start)
        {
            _anchor = start;
        }

        return true;
    }

    /// <summary>The keys that a Shift extends the selection with: the arrows, Ctrl+Left and Ctrl+Right, Home, End, PgUp and PgDn.</summary>
    private static bool IsExtendable(Key key) =>
        key == Key.CursorLeft || key == Key.CursorRight || key == Key.CursorUp || key == Key.CursorDown
        || key == Key.CursorLeft.WithCtrl || key == Key.CursorRight.WithCtrl
        || key == Key.Home || key == Key.End || key == Key.PageUp || key == Key.PageDown;

    /// <summary>The keys that move the cursor: the extendable keys and the document's ends (Ctrl+Home and Ctrl+End).</summary>
    private static bool IsMovement(Key key) => IsExtendable(key) || key == Key.Home.WithCtrl || key == Key.End.WithCtrl;

    /// <summary>
    /// Handles a mouse event: the wheel scrolls without moving the cursor; a left press places the cursor and starts a drag, a drag
    /// (motion with the button down) extends the selection, a release ends it, and a double-click selects the word under the mouse;
    /// a right-click asks for the context menu. A press, a double-click or a right-click places the cursor, so it drops the pending insertion
    /// and raises <see cref="CommandPressed"/> first. Returns false for any other event.
    /// </summary>
    /// <param name="mouse">The event.</param>
    internal bool HandleMouse(Mouse mouse)
    {
        if (PlacesCursor(mouse))
        {
            CommandPressed?.Invoke();
        }

        // The rows are mapped while the pending insertion is still drawn, so that a press on the virtual line and below it lands where it is drawn.
        bool handled = ApplyMouse(mouse);
        if (PlacesCursor(mouse))
        {
            SetPending(null);
        }

        if (!handled)
        {
            return false;
        }

        // A move can scroll, and later the selection and highlights follow the cursor, so the content is drawn again.
        SetNeedsDraw();
        return true;
    }

    /// <summary>Applies a mouse event to the view state. Returns true when it was handled.</summary>
    private bool ApplyMouse(Mouse mouse)
    {
        if (!SyncLayout())
        {
            return false;
        }

        // The fork reports a horizontal wheel as WheeledLeft and WheeledRight, which contain the vertical flags, so they are tested first.
        if (mouse.Flags.HasFlag(MouseFlags.WheeledLeft))
        {
            WheelTable(mouse, -ScrollColumns);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledRight))
        {
            WheelTable(mouse, ScrollColumns);
            return true;
        }

        // Shift with the vertical wheel scrolls a table under the mouse; elsewhere it scrolls the document as the wheel does.
        bool shift = mouse.Flags.HasFlag(MouseFlags.Shift);
        if (shift && mouse.Flags.HasFlag(MouseFlags.WheeledUp) && WheelTable(mouse, -ScrollColumns))
        {
            return true;
        }

        if (shift && mouse.Flags.HasFlag(MouseFlags.WheeledDown) && WheelTable(mouse, ScrollColumns))
        {
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            Wheel(-WheelLines);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            Wheel(WheelLines);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased))
        {
            return EndDrag();
        }

        if (mouse.Flags.HasFlag(MouseFlags.RightButtonClicked))
        {
            return ContextClick(mouse);
        }

        if (!HasCursor)
        {
            return false;
        }

        int x = mouse.Position?.X ?? 0;
        int y = mouse.Position?.Y ?? 0;
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
        {
            return SelectWord(x, y);
        }

        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            return false;
        }

        return mouse.Flags.HasFlag(MouseFlags.PositionReport)
            ? _dragging && Drag(x, y)
            : Press(x, y, ctrl: mouse.Flags.HasFlag(MouseFlags.Ctrl));
    }

    /// <summary>
    /// A left press in the view: the cursor goes to the cell, the selection is cleared, and a drag starts from there. A Ctrl+press on a link
    /// places the cursor the same way, and raises <see cref="LinkActivated"/> instead of starting a drag.
    /// </summary>
    /// <param name="ctrl">Whether Ctrl is held.</param>
    private bool Press(int x, int y, bool ctrl)
    {
        if (!InView(y))
        {
            return false;
        }

        SetFocus();

        // An edge marker scrolls its table, and a fold glyph or summary folds its unit; neither places the cursor.
        int line = LineAtRow(y);
        if (PressMarker(line, x) || ClickFold(line, x))
        {
            return true;
        }

        int before = _offset;
        int topBefore = _top;
        PlaceClick(x, y);
        Settle(before, topBefore);
        if (ctrl && CaretLink is { } link)
        {
            LinkActivated?.Invoke(link);
            return true;
        }

        _press = _offset;
        _dragging = true;
        if (App is { } && !App.Mouse.IsGrabbed(this))
        {
            App.Mouse.GrabMouse(this);
        }

        return true;
    }

    /// <summary>
    /// A motion with the button down: the cursor goes to the cell under the mouse and the selection runs from the press. Above or below
    /// the view, the top line scrolls by one line and the cursor goes to the edge row.
    /// </summary>
    private bool Drag(int x, int y)
    {
        int before = _offset;
        int topBefore = _top;
        int height = ViewportHeight;
        int line;
        if (y < 0)
        {
            // The edge row is the top one, or the first row of the view that has a mapped cell (a cursor on a row with none would scroll).
            _top = Math.Max(0, _top - 1);
            int last = Math.Min(_top + height - 1, LineCount - 1);
            line = _top;
            while (line < last && !HasMapped(line))
            {
                line++;
            }
        }
        else if (y >= height)
        {
            // The edge row is the last one of the view, or the last row above it that has a mapped cell.
            _top = Math.Min(MaxTop(), _top + 1);
            line = Math.Min(_top + height - 1, LineCount - 1);
            while (line > _top && !HasMapped(line))
            {
                line--;
            }
        }
        else
        {
            line = Math.Min(LineAtRow(y), LineCount - 1);
        }

        PlaceColumn(line, Math.Max(0, x));
        _goal = _column;
        _range = null;
        if (_anchor is null && _offset != _press)
        {
            _anchor = _press;
        }

        Settle(before, topBefore);
        return true;
    }

    /// <summary>The release of the button: the drag ends and the mouse is released to the application.</summary>
    private bool EndDrag()
    {
        if (!_dragging)
        {
            return false;
        }

        _dragging = false;
        App?.Mouse.UngrabMouse();
        return true;
    }

    /// <summary>
    /// A right-click: a click outside the selection places the cursor there (clearing the selection), a click inside keeps it. The window
    /// is then asked to open its menu at the mouse.
    /// </summary>
    private bool ContextClick(Mouse mouse)
    {
        int x = mouse.Position?.X ?? 0;
        int y = mouse.Position?.Y ?? 0;
        if (HasCursor && InView(y))
        {
            SetFocus();
            int offset = OffsetAtColumn(LineAtRow(y), Math.Max(0, x));
            if (SelectionRange is not { } selection || offset < selection.Start || offset >= selection.End)
            {
                int before = _offset;
                int topBefore = _top;
                PlaceClick(x, y);
                Settle(before, topBefore);
            }
        }

        ContextMenuRequested?.Invoke(mouse.ScreenPosition);
        return true;
    }

    /// <summary>The double-click: the word under the mouse is selected. A click on a space, a synthetic cell or outside the text selects nothing.</summary>
    private bool SelectWord(int x, int y)
    {
        if (!InView(y))
        {
            return false;
        }

        int line = LineAtRow(y);
        int cell = CellAt(line, LayoutColumn(line, x));
        if (cell < 0 || WordAt(line, cell) is not { } word)
        {
            return false;
        }

        SelectRange(word);
        return true;
    }

    /// <summary>Places the cursor at the cell under a view position, and clears the selection and the drag.</summary>
    private void PlaceClick(int x, int y)
    {
        _anchor = null;
        _range = null;
        PlaceColumn(LineAtRow(y), Math.Max(0, x));
        _goal = _column;
    }

    /// <summary>Whether a view row is a row of the document on the screen.</summary>
    private bool InView(int y) => y >= 0 && y < ViewportHeight && LineAtRow(y) < LineCount;

    /// <summary>The index of the cell of a line under a screen column, or -1 when no cell covers it.</summary>
    private int CellAt(int line, int column)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        int[] columns = ColumnsOf(line);
        for (int c = 0; c < cells.Length; c++)
        {
            if (cells[c].Width > 0 && column >= columns[c] && column < columns[c] + cells[c].Width)
            {
                return c;
            }
        }

        return -1;
    }

    /// <summary>
    /// The source range of the word that the cell at an index belongs to: the maximal run of mapped, non-space cells around it on the
    /// line. Null when the cell is synthetic or a space.
    /// </summary>
    private SourceRange? WordAt(int line, int cell)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        if (!IsWordCell(cells[cell]))
        {
            return null;
        }

        int first = cell;
        int last = cell;
        while (first > 0 && IsWordCell(cells[first - 1]))
        {
            first--;
        }

        while (last + 1 < cells.Length && IsWordCell(cells[last + 1]))
        {
            last++;
        }

        int baseOffset = Built.BaseOffset(line);
        int lastOffset = baseOffset + cells[last].Offset;
        return new SourceRange(baseOffset + cells[first].Offset, lastOffset + SourceCells.Length(_document, lastOffset));
    }

    /// <summary>Whether a cell is part of a word: mapped, and not a space.</summary>
    private static bool IsWordCell(Cell cell) => cell.IsMapped && !string.IsNullOrWhiteSpace(cell.Text);

    /// <summary>
    /// Selects an exact source range: the cursor goes to its first cell and is scrolled into view, and the selection is the range. Used
    /// for a search match and a word. Does nothing when the view has no cursor.
    /// </summary>
    /// <param name="range">The range, in absolute source offsets.</param>
    public void SelectRange(SourceRange range)
    {
        if (!SyncLayout() || !HasCursor)
        {
            return;
        }

        int before = _offset;
        int topBefore = _top;
        if (UnfoldHiding(range.Start))
        {
            Reflow(Built.Width);
        }

        PlaceAt(range.Start);
        _goal = _column;
        _anchor = null;
        _range = range;
        Settle(before, topBefore);
        SetNeedsDraw();
    }

    /// <summary>
    /// Places the cursor at a source offset, with the selection from an anchor offset to it (or none). A unit that hides the offset is
    /// unfolded first, as a search match does; the view scrolls to keep the cursor visible. The caret is placed by <see cref="ResolveCaret"/>,
    /// so an offset on markup lands on the next visible character.
    /// </summary>
    /// <param name="offset">The source offset of the caret.</param>
    /// <param name="anchor">The offset the selection starts from, or null for no selection.</param>
    public void PlaceCaret(int offset, int? anchor)
    {
        if (!SyncLayout())
        {
            return;
        }

        SetPending(null);
        int before = _offset;
        int topBefore = _top;
        if (UnfoldHiding(offset))
        {
            Reflow(Built.Width);
        }

        PlaceAt(offset);
        _goal = _column;
        _range = null;
        _anchor = anchor is { } start && start != _offset ? start : null;
        Settle(before, topBefore);
        SetNeedsDraw();
    }

    /// <summary>
    /// Sets the search matches drawn in the search colours, and which one is active (its index in <paramref name="matches"/>, or -1).
    /// The matches are sorted and do not overlap, as <see cref="RenderSearch.Find"/> gives them.
    /// </summary>
    /// <param name="matches">The matches, in document order. Empty removes the highlights.</param>
    /// <param name="active">The index of the active match, or -1 for none.</param>
    public void SetMatches(IReadOnlyList<SourceRange> matches, int active)
    {
        ArgumentNullException.ThrowIfNull(matches);

        _matches = ImmutableArray.CreateRange(matches);
        _active = active;
        SetNeedsDraw();
    }


    // ----- Layout -----

    /// <summary>
    /// Builds the layout at the viewport's width when there is none or the width changed. A first layout places the cursor at the first
    /// mapped cell; a later one is a reflow (see the remarks of the class). Returns false when the viewport has no width yet and no
    /// layout exists.
    /// </summary>
    private bool SyncLayout()
    {
        int width = Viewport.Width;
        if (width <= 0)
        {
            return _layout is not null;
        }

        if (_layout is not null && _layout.Width == width)
        {
            return true;
        }

        Reflow(width);
        return true;
    }

    private void Reflow(int width)
    {
        if (_layout is null)
        {
            _layout = BuildLayout(width);
            _top = 0;
            if (TakePendingGoTo())
            {
                return;
            }

            if (FirstMapped() is { } first)
            {
                PlaceCell(first.Line, first.Cell, keepGoal: false);
            }

            UpdateCursor();
            PositionChanged?.Invoke();
            return;
        }

        bool visible = IsCursorVisible();
        int anchor = visible || !HasCursor ? _offset : TopOffset;
        int row = visible ? _line - _top : 0;
        Rebuild(width, anchor, row, HasCursor);
        PositionChanged?.Invoke();
    }

    /// <summary>
    /// Builds the layout at a width for the current document and fold state, and places the top line so that the anchor's line is on a
    /// screen row (the cursor stays at its offset when there was one). Shared by a reflow and a new document.
    /// </summary>
    /// <param name="width">The width of the layout.</param>
    /// <param name="anchor">The source offset whose line keeps the row.</param>
    /// <param name="row">The screen row of the anchor's line.</param>
    /// <param name="hadCursor">Whether the view had a cursor before; without one the top line is only clamped.</param>
    private void Rebuild(int width, int anchor, int row, bool hadCursor)
    {
        _layout = BuildLayout(width);
        if (hadCursor)
        {
            int anchorLine = ResolveCaret(anchor) is { } anchorCaret ? anchorCaret.Line : _layout.PositionOf(anchor).Line;
            _top = Math.Clamp(anchorLine - row, 0, MaxTop());
            PlaceAt(_offset);
            _goal = _column;
            EnsureCaretColumnVisible();
        }
        else
        {
            _top = Math.Clamp(_top, 0, MaxTop());
        }

        SyncTracking();
        UpdateCursor();
    }

    private DocumentLayout Built => _layout ?? throw new InvalidOperationException("The view has no layout yet.");

    private int ViewportHeight => Math.Max(1, Viewport.Height);

    private int MaxTop() => Math.Max(0, DisplayedLineCount - ViewportHeight);

    private void ClampTop() => _top = Math.Clamp(_top, 0, MaxTop());

    private bool IsCursorVisible() =>
        (HasCursor || HasVirtualLine) && CursorRow >= 0 && CursorRow < ViewportHeight && !(HasCursor && CaretScrolledOut());

    /// <summary>Scrolls by the least amount that keeps the cursor's line, and the virtual line, in view.</summary>
    private void EnsureVisible()
    {
        if (HasCursor)
        {
            EnsureLineVisible(_line);
        }

        EnsureVirtualLineVisible();
    }

    /// <summary>Scrolls by the least amount that brings a display line into the viewport's rows.</summary>
    /// <param name="line">The display line.</param>
    private void EnsureLineVisible(int line)
    {
        int height = ViewportHeight;
        if (line < _top)
        {
            _top = line;
        }
        else if (line >= _top + height)
        {
            _top = line - height + 1;
        }

        ClampTop();
    }

    // ----- Cursor placement -----

    /// <summary>
    /// Whether a line is the last display line of its block that has mapped cells. Only such a line has an end position: the caret
    /// rests after its last cell (see <see cref="EndOffset"/>). A code header whose label maps the block's start has none: that cell is
    /// the block's start, not text the caret can end after, so the caret rests on the label instead.
    /// </summary>
    private bool IsBlockEnd(int line)
    {
        if (!HasMapped(line) || Built.Line(line).Cells.Last(cell => cell.IsMapped).Style.Role == StyleRole.CodeHeader)
        {
            return false;
        }

        int block = Built.BaseOffset(line);
        for (int l = line + 1; l < LineCount && Built.BaseOffset(l) == block; l++)
        {
            if (HasMapped(l))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a line ends in a hard break: a paragraph line with mapped cells whose next line is a line of the same block that starts a new run
    /// (not a wrap of this line). Its end is a caret stop, so that Delete there joins the lines (see <c>ViewEdit.DeleteForward</c>).
    /// </summary>
    private bool IsHardBreakEnd(int line)
    {
        if (!HasMapped(line) || line + 1 >= LineCount)
        {
            return false;
        }

        int block = BlockOfLine(line);
        int next = line + 1;
        return block >= 0 && _document.Blocks[block].Kind == BlockKind.Paragraph
            && Built.BaseOffset(next) == Built.BaseOffset(line) && !Built.Line(next).IsContinuation && HasMapped(next);
    }

    /// <summary>Whether a line has an end position that the caret can rest on: the end of a block's last line, or a hard break's line end.</summary>
    private bool HasEndStop(int line) => IsBlockEnd(line) || IsHardBreakEnd(line);

    /// <summary>
    /// The end position of a block's last line, as an absolute source offset: the offset of its last mapped cell plus that cell's source
    /// length. It is the offset just after the block's last character.
    /// </summary>
    private int EndOffset(int line)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        int last = cells.Length - 1;
        while (!cells[last].IsMapped)
        {
            last--;
        }

        int absolute = Built.BaseOffset(line) + cells[last].Offset;
        return absolute + SourceCells.Length(_document, absolute);
    }

    /// <summary>The screen column just after the last cell of a line.</summary>
    private int EndColumn(int line) => Built.Line(line).Cells.Sum(cell => cell.Width);

    /// <summary>The absolute source offset of a caret stop: a cell's offset, or the end offset for the end position (cell index <c>Length</c>).</summary>
    private int OffsetOf(int line, int cell) =>
        cell == Built.Line(line).Cells.Length ? EndOffset(line) : Built.BaseOffset(line) + Built.Line(line).Cells[cell].Offset;

    /// <summary>Whether a caret can rest on a line's cell index: a mapped cell, or the end position of a block's last line.</summary>
    private bool IsStop(int line, int cell)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        return cell < cells.Length ? cells[cell].IsMapped : HasEndStop(line);
    }

    /// <summary>
    /// The caret stop that an offset places the caret on: the end position of a block when the offset is the end offset of its last
    /// line, else the mapped cell that the source map gives (see <see cref="CellFor"/>). Null when the document has no stop.
    /// </summary>
    private (int Line, int Cell)? ResolveCaret(int offset)
    {
        // A document with no line has no stop (its caret is the empty line the view draws, see RenderedView.Pending).
        if (LineCount == 0)
        {
            return null;
        }

        (int line, int column) = Built.PositionOf(offset);

        // The end offset of a block is the offset of the next character of the source, which the map places on a later line. So the end
        // position is looked for on this line and on the last line before it that has mapped cells.
        int previous = LineWithMappedBefore(line);
        foreach (int candidate in new[] { line, previous })
        {
            if (candidate >= 0 && HasEndStop(candidate) && EndOffset(candidate) == offset)
            {
                return (candidate, Built.Line(candidate).Cells.Length);
            }
        }

        // A line with no stop of its own gives the stop before it, or the first one after it when none is before.
        int cell = CellFor(line, offset, column);
        return cell < 0 ? LastMappedAtOrBefore(line) ?? NextStop(line, -1) : (line, cell);
    }

    /// <summary>The last line before <paramref name="line"/> that has a mapped cell, or -1 when there is none.</summary>
    private int LineWithMappedBefore(int line)
    {
        for (int l = Math.Min(line, LineCount) - 1; l >= 0; l--)
        {
            if (HasMapped(l))
            {
                return l;
            }
        }

        return -1;
    }

    /// <summary>
    /// The caret stop that a screen column of a line selects: the cell covering the column, or the first mapped cell after it on the line;
    /// past the last cell, the end position of a block's last line. Null when the line has no stop at or after the column, so that the
    /// caller uses the source map instead.
    /// </summary>
    private (int Line, int Cell)? TargetAtColumn(int line, int x)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        int cover = CellAt(line, x);
        int start = cover >= 0 ? cover : x >= EndColumn(line) ? cells.Length : 0;
        for (int c = start; c < cells.Length; c++)
        {
            if (cells[c].IsMapped)
            {
                return (line, c);
            }
        }

        return HasEndStop(line) ? (line, cells.Length) : null;
    }

    /// <summary>The source offset that a screen column of a line selects (see <see cref="TargetAtColumn"/>).</summary>
    private int OffsetAtColumn(int line, int x)
    {
        int column = LayoutColumn(line, x);
        return TargetAtColumn(line, column) is { } target ? OffsetOf(target.Line, target.Cell) : Built.OffsetAt(line, column);
    }

    /// <summary>
    /// Sets the cursor to a caret stop of a line: a mapped cell, or the end position (cell index <c>Length</c>). The goal column follows the
    /// stop unless <paramref name="keepGoal"/> is set.
    /// </summary>
    private void PlaceCell(int line, int cell, bool keepGoal)
    {
        _line = line;
        _cell = cell;
        _column = cell < Built.Line(line).Cells.Length ? ColumnsOf(line)[cell] : EndColumn(line);
        _offset = OffsetOf(line, cell);
        if (!keepGoal)
        {
            _goal = _column;
        }
    }

    /// <summary>
    /// Sets the cursor to the caret stop of a source offset (see <see cref="ResolveCaret"/>). A line with no stop is replaced by the last
    /// stop before it; a document with none has no cursor.
    /// </summary>
    private void PlaceAt(int offset)
    {
        if (ResolveCaret(offset) is { } caret)
        {
            PlaceCell(caret.Line, caret.Cell, keepGoal: false);
            return;
        }

        _cell = -1;
        _offset = 0;
    }

    /// <summary>Sets the cursor to the stop that a screen column of a line selects (see <see cref="TargetAtColumn"/>).</summary>
    private void PlaceColumn(int line, int x)
    {
        int column = LayoutColumn(line, x);
        if (TargetAtColumn(line, column) is { } target)
        {
            PlaceCell(target.Line, target.Cell, keepGoal: false);
        }
        else
        {
            PlaceAt(Built.OffsetAt(line, column));
        }
    }

    /// <summary>The index of the mapped cell of a line at an offset, else the first mapped cell at or after the column, else the last mapped one; -1 when the line has none.</summary>
    private int CellFor(int line, int offset, int column)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        int[] columns = ColumnsOf(line);
        int baseOffset = Built.BaseOffset(line);
        int afterColumn = -1;
        int last = -1;
        for (int c = 0; c < cells.Length; c++)
        {
            if (!cells[c].IsMapped)
            {
                continue;
            }

            if (baseOffset + cells[c].Offset == offset)
            {
                return c;
            }

            if (afterColumn < 0 && columns[c] >= column)
            {
                afterColumn = c;
            }

            last = c;
        }

        return afterColumn >= 0 ? afterColumn : last;
    }

    /// <summary>The screen column of each cell of a line; a zero-width cell has the column of the cell before it.</summary>
    private int[] ColumnsOf(int line)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        var columns = new int[cells.Length];
        int column = 0;
        for (int c = 0; c < cells.Length; c++)
        {
            int width = cells[c].Width;
            columns[c] = width == 0 && c > 0 ? columns[c - 1] : column;
            column += width;
        }

        return columns;
    }

    private bool HasMapped(int line) => Built.Line(line).Cells.Any(cell => cell.IsMapped);

    // ----- Finding cells -----

    /// <summary>The caret stop after (line, cell) in document order: a mapped cell or an end position, or null when there is none.</summary>
    private (int Line, int Cell)? NextStop(int line, int cell)
    {
        for (int l = line; l < LineCount; l++)
        {
            int length = Built.Line(l).Cells.Length;
            for (int c = l == line ? cell + 1 : 0; c <= length; c++)
            {
                if (IsStop(l, c))
                {
                    return (l, c);
                }
            }
        }

        return null;
    }

    /// <summary>The caret stop before (line, cell) in document order: a mapped cell or an end position, or null when there is none.</summary>
    private (int Line, int Cell)? PrevStop(int line, int cell)
    {
        for (int l = Math.Min(line, LineCount - 1); l >= 0; l--)
        {
            int length = Built.Line(l).Cells.Length;
            int start = l == line ? Math.Min(cell - 1, length) : length;
            for (int c = start; c >= 0; c--)
            {
                if (IsStop(l, c))
                {
                    return (l, c);
                }
            }
        }

        return null;
    }

    /// <summary>The first mapped cell of the document, or null.</summary>
    private (int Line, int Cell)? FirstMapped() => NextStop(0, -1);

    /// <summary>The last caret stop of the document, which is the end position of the last block, or null for an empty document.</summary>
    private (int Line, int Cell)? LastStop() => LineCount == 0 ? null : PrevStop(LineCount - 1, int.MaxValue / 2);

    /// <summary>The last caret stop at or before a line, or null when there is none.</summary>
    private (int Line, int Cell)? LastMappedAtOrBefore(int line) => PrevStop(line, int.MaxValue / 2);

    /// <summary>
    /// The index of the first mapped cell of a line, or of its last caret stop (the end position on a block's last line, else the last
    /// mapped cell). -1 when the line has no stop.
    /// </summary>
    private int EdgeMappedCell(int line, bool first)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        if (!first && HasEndStop(line))
        {
            return cells.Length;
        }

        if (first)
        {
            for (int c = 0; c < cells.Length; c++)
            {
                if (cells[c].IsMapped)
                {
                    return c;
                }
            }
        }
        else
        {
            for (int c = cells.Length - 1; c >= 0; c--)
            {
                if (cells[c].IsMapped)
                {
                    return c;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// The mapped cell of a line nearest to the goal column. A cell that covers the column is nearest; otherwise the distance is to the
    /// cell's near edge, and a tie goes to the left.
    /// </summary>
    private int NearestCell(int line, int goal)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        int[] columns = ColumnsOf(line);
        int best = -1;
        int bestDistance = int.MaxValue;
        for (int c = 0; c < cells.Length; c++)
        {
            if (!cells[c].IsMapped)
            {
                continue;
            }

            int start = columns[c];
            int end = start + Math.Max(1, cells[c].Width) - 1;
            int distance = goal < start ? start - goal : goal > end ? goal - end : 0;
            if (distance < bestDistance)
            {
                best = c;
                bestDistance = distance;
            }
        }

        // The end position is a column too, at the end of the line.
        if (HasEndStop(line) && Math.Abs(goal - EndColumn(line)) is int endDistance && endDistance < bestDistance)
        {
            best = cells.Length;
        }

        return best;
    }

    /// <summary>The next line in a direction from <paramref name="from"/> that has a mapped cell, or -1 when there is none.</summary>
    private int NextLineWithMapped(int from, int direction)
    {
        for (int l = from + direction; l >= 0 && l < LineCount; l += direction)
        {
            if (HasMapped(l))
            {
                return l;
            }
        }

        return -1;
    }

    /// <summary>The line itself when it has a mapped cell, else the nearest one in the direction, else in the other direction; -1 when none.</summary>
    private int NearestMappedLine(int target, int direction)
    {
        if (target >= 0 && target < LineCount && HasMapped(target))
        {
            return target;
        }

        int next = NextLineWithMapped(target, direction);
        return next >= 0 ? next : NextLineWithMapped(target, -direction);
    }

    // ----- Moves -----

    private void MoveHorizontal(int direction)
    {
        int before = _offset;
        int topBefore = _top;
        (int Line, int Cell)? target = direction > 0 ? NextStop(_line, _cell) : PrevStop(_line, _cell);
        if (target is { } stop)
        {
            PlaceCell(stop.Line, stop.Cell, keepGoal: false);
        }

        Settle(before, topBefore);
    }

    /// <summary>
    /// Ctrl+Right and Ctrl+Left: the caret goes to the start of the next word, or to the start of the word it is in or the one before. A word is a
    /// run of letters, digits and underscores, or a run of other visible characters; spaces and blanks between words are skipped. A block's end
    /// position is a stop of its own, so the caret pauses at the end of a paragraph before it goes to the next one, as a word processor's does.
    /// </summary>
    /// <param name="forward">True for Ctrl+Right.</param>
    private void MoveWord(bool forward)
    {
        if (_cell < 0)
        {
            return;
        }

        (int Line, int Cell) at = (_line, _cell);
        (int Line, int Cell)? Step((int Line, int Cell) from) => forward ? NextStop(from.Line, from.Cell) : PrevStop(from.Line, from.Cell);

        if (forward)
        {
            // Past the rest of the word (or the end position) the caret is on, then past the blanks after it.
            WordClass word = ClassAt(at);
            if (Step(at) is not { } next)
            {
                return;
            }

            at = next;
            while (word is WordClass.Letters or WordClass.Symbols && ClassAt(at) == word && Step(at) is { } further)
            {
                at = further;
            }

            while (ClassAt(at) == WordClass.Blank && Step(at) is { } further)
            {
                at = further;
            }
        }
        else
        {
            // Back over the blanks before the caret, then to the start of the word found there (an end position is a stop of its own).
            if (Step(at) is not { } previous)
            {
                return;
            }

            at = previous;
            while (ClassAt(at) == WordClass.Blank && Step(at) is { } further)
            {
                at = further;
            }

            WordClass word = ClassAt(at);
            while (word is WordClass.Letters or WordClass.Symbols && Step(at) is { } further && ClassAt(further) == word)
            {
                at = further;
            }
        }

        MoveTo(at);
    }

    /// <summary>
    /// The class of a caret stop for word moves: a block's (or hard-broken line's) end position, a letter, digit or underscore, another visible
    /// character, or a blank (a space, or a cell that shows nothing).
    /// </summary>
    private WordClass ClassAt((int Line, int Cell) stop)
    {
        ImmutableArray<Cell> cells = Built.Line(stop.Line).Cells;
        if (stop.Cell >= cells.Length)
        {
            return WordClass.End;
        }

        string text = cells[stop.Cell].Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return WordClass.Blank;
        }

        Rune rune = Rune.GetRuneAt(text, 0);
        return Rune.IsLetterOrDigit(rune) || rune.Value == '_' ? WordClass.Letters : WordClass.Symbols;
    }

    /// <summary>The classes of caret stops that word moves go by (see <see cref="ClassAt"/>).</summary>
    private enum WordClass
    {
        /// <summary>A space, or a cell that shows nothing.</summary>
        Blank,

        /// <summary>A letter, a digit or an underscore.</summary>
        Letters,

        /// <summary>Another visible character, such as punctuation.</summary>
        Symbols,

        /// <summary>The end position of a block, or of a line that a hard break ends.</summary>
        End,
    }

    private void MoveVertical(int direction)
    {
        int line = NextLineWithMapped(_line, direction);
        if (line < 0)
        {
            return;
        }

        int before = _offset;
        int topBefore = _top;
        PlaceCell(line, NearestCell(line, _goal), keepGoal: true);
        Settle(before, topBefore);
    }

    private void MoveTo((int Line, int Cell)? target)
    {
        if (target is null)
        {
            return;
        }

        int before = _offset;
        int topBefore = _top;
        PlaceCell(target.Value.Line, target.Value.Cell, keepGoal: false);
        Settle(before, topBefore);
    }

    private void MoveToLineEnd(bool first)
    {
        int cell = EdgeMappedCell(_line, first);
        if (cell < 0)
        {
            return;
        }

        int before = _offset;
        int topBefore = _top;
        PlaceCell(_line, cell, keepGoal: false);
        Settle(before, topBefore);
    }

    /// <summary>
    /// Moves the cursor a page of display lines and scrolls the top by the same number of lines, so that the cursor keeps its row
    /// unless the document's ends stop it.
    /// </summary>
    private void MovePage(int direction)
    {
        int lines = ViewportHeight;
        int before = _offset;
        int topBefore = _top;
        int target = NearestMappedLine(Math.Clamp(_line + (direction * lines), 0, LineCount - 1), direction);
        if (target >= 0)
        {
            PlaceCell(target, NearestCell(target, _goal), keepGoal: true);
        }

        _top = Math.Clamp(_top + (direction * lines), 0, MaxTop());
        Settle(before, topBefore);
    }

    private void Wheel(int lines)
    {
        if (LineCount == 0)
        {
            return;
        }

        int topBefore = _top;
        _top = Math.Clamp(_top + lines, 0, MaxTop());
        if (_top != topBefore)
        {
            UpdateCursor();
            PositionChanged?.Invoke();
        }
    }

    /// <summary>After a move: keeps the cursor on screen, places the terminal cursor, and raises the event when the cursor or the top moved.</summary>
    private void Settle(int offsetBefore, int topBefore)
    {
        EnsureCaretColumnVisible();
        EnsureVisible();
        SyncTracking();
        UpdateCursor();
        if (_offset != offsetBefore || _top != topBefore)
        {
            PositionChanged?.Invoke();
        }
    }

    // ----- Drawing -----

    /// <summary>Shows the terminal cursor at the cursor's cell, or hides it when the cursor is off screen or there is none.</summary>
    private void UpdateCursor()
    {
        Cursor = IsCursorVisible()
            ? new Cursor { Position = CursorScreenPosition, Style = CursorStyle.BlinkingBar }
            : new Cursor { Position = null, Style = CursorStyle.BlinkingBar };
    }

    /// <summary>
    /// Draws a display line: the container prefix in place, then the content cells shifted by the block's offset (see the scrolling part of
    /// the class). The line is built column by column, so that a wide cell cut at the left edge is a space, a wide cell that does not fit is
    /// left out, and the edge markers replace the cell in their column. Runs of one attribute are drawn together.
    /// </summary>
    private void DrawLine(int y, int line, int width, Attribute normal)
    {
        DisplayLine display = Built.Line(line);
        ImmutableArray<Cell> cells = display.Cells;
        int baseOffset = Built.BaseOffset(line);
        SourceRange? selection = SelectionRange;
        (int first, int last) = SelectedSpan(cells, baseOffset, selection);
        int prefix = PrefixWidthOf(display);
        int shift = ShiftOf(line);
        string?[] texts = new string?[width];
        Attribute[] attributes = new Attribute[width];
        Array.Fill(texts, " ");
        Array.Fill(attributes, normal);

        string? alert = null;
        int column = 0;
        int contentColumn = 0;
        for (int c = 0; c < cells.Length; c++)
        {
            Cell cell = cells[c];
            if (cell.Width == 0)
            {
                continue;
            }

            bool isPrefix = c < display.PrefixCells;
            int start = isPrefix ? column : prefix + contentColumn - shift;
            if (isPrefix)
            {
                column += cell.Width;
            }
            else
            {
                contentColumn += cell.Width;
            }

            Attribute attribute;
            if (cell.Style.Role == StyleRole.AlertTitle)
            {
                alert ??= AlertTitleOf(display);
                attribute = MdColors.ForAlert(alert, normal);
            }
            else
            {
                attribute = MdColors.For(cell.Style, normal);
            }

            attribute = Highlight(attribute, cell, c, baseOffset, selection, first, last);
            if (!isPrefix && start < prefix)
            {
                // Scrolled out on the left: a wide cell that still reaches the first column is drawn as a space there.
                if (start + cell.Width > prefix)
                {
                    texts[prefix] = " ";
                    attributes[prefix] = attribute;
                }

                continue;
            }

            if (start + cell.Width > width)
            {
                break;
            }

            texts[start] = cell.Text;
            attributes[start] = attribute;
            for (int k = 1; k < cell.Width; k++)
            {
                texts[start + k] = null;
            }
        }

        if (line == _line && HasCursor)
        {
            ShowPending(texts, attributes, CaretRawColumn(), normal);
        }

        (bool leftMarker, bool rightMarker) = MarkersOf(line);
        Attribute marker = MdColors.For(new CellStyle(StyleRole.TableBorder), normal);
        if (leftMarker)
        {
            PlaceMarker(texts, attributes, prefix, "◂", marker);
        }

        if (rightMarker)
        {
            PlaceMarker(texts, attributes, width - 1, "▸", marker);
        }

        DrawColumns(y, texts, attributes);
    }

    /// <summary>
    /// Puts an edge marker in a column. A wide cell that the marker covers, on either side of the column, is replaced by a space, so that no
    /// half of a wide cell is left.
    /// </summary>
    private static void PlaceMarker(string?[] texts, Attribute[] attributes, int column, string text, Attribute attribute)
    {
        if (column < 0 || column >= texts.Length)
        {
            return;
        }

        if (texts[column] is null && column > 0)
        {
            texts[column - 1] = " ";
        }

        if (column + 1 < texts.Length && texts[column + 1] is null)
        {
            texts[column + 1] = " ";
        }

        texts[column] = text;
        attributes[column] = attribute;
    }

    /// <summary>Draws the columns of one row: runs of columns with the same attribute are drawn together; a null column is the second half of a wide cell.</summary>
    private void DrawColumns(int y, string?[] texts, Attribute[] attributes)
    {
        int x = 0;
        while (x < texts.Length)
        {
            if (texts[x] is null)
            {
                x++;
                continue;
            }

            Attribute attribute = attributes[x];
            int start = x;
            var run = new System.Text.StringBuilder();
            while (x < texts.Length && (texts[x] is null || attributes[x] == attribute))
            {
                if (texts[x] is { } text)
                {
                    run.Append(text);
                }

                x++;
            }

            Move(start, y);
            SetAttribute(attribute);
            AddStr(run.ToString());
        }
    }

    /// <summary>
    /// The attribute of a cell with the highlights applied: a cell in a search match takes the match colour (the active one its own),
    /// which replaces the selection's reverse as <see cref="SearchColors"/> does. A cell in the selection is reversed. A synthetic cell
    /// between two selected cells of its line (a table's border) is reversed too, so that a selected row looks continuous.
    /// </summary>
    private Attribute Highlight(Attribute attribute, Cell cell, int index, int baseOffset, SourceRange? selection, int first, int last)
    {
        if (!cell.IsMapped)
        {
            return first >= 0 && index > first && index < last ? Reversed(attribute) : attribute;
        }

        int offset = baseOffset + cell.Offset;
        int match = MatchAt(offset);
        if (match >= 0)
        {
            return match == _active ? SearchColors.Active(attribute) : SearchColors.Match(attribute);
        }

        return selection is { } range && offset >= range.Start && offset < range.End ? Reversed(attribute) : attribute;
    }

    /// <summary>The attribute with the reverse style added.</summary>
    private static Attribute Reversed(Attribute attribute) => new(attribute.Foreground, attribute.Background, attribute.Style | TextStyle.Reverse);

    /// <summary>
    /// The indexes of the first and the last mapped cell of a line that are in the selection, or -1 and -1 when none is.
    /// </summary>
    private static (int First, int Last) SelectedSpan(ImmutableArray<Cell> cells, int baseOffset, SourceRange? selection)
    {
        if (selection is not { } range)
        {
            return (-1, -1);
        }

        int first = -1;
        int last = -1;
        for (int c = 0; c < cells.Length; c++)
        {
            if (cells[c].IsMapped && baseOffset + cells[c].Offset is int offset && offset >= range.Start && offset < range.End)
            {
                first = first < 0 ? c : first;
                last = c;
            }
        }

        return (first, last);
    }

    /// <summary>The index of the search match that holds an offset, or -1. The matches are sorted and do not overlap.</summary>
    private int MatchAt(int offset)
    {
        int low = 0;
        int high = _matches.Length - 1;
        int found = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (_matches[mid].Start <= offset)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found >= 0 && offset < _matches[found].End ? found : -1;
    }

    private static string AlertTitleOf(DisplayLine line) =>
        string.Concat(line.Cells.Where(cell => cell.Style.Role == StyleRole.AlertTitle).Select(cell => cell.Text));

    private void DrawBlank(int from, int to, int width, Attribute normal)
    {
        for (int y = from; y < to; y++)
        {
            Move(0, y);
            SetAttribute(normal);
            AddStr(new string(' ', width));
        }
    }
}
