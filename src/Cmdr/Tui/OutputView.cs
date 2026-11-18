using System.Text;
using Cmdr.Core.Output;
using Cmdr.Core.Session;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// Shows the output buffer of one command. Long lines wrap to the view width, cell by cell like a terminal; nothing is
/// dropped, and copy takes the original lines. Scrolling moves by screen rows, tail-follows by default. The view has a
/// cursor line (shown while focused, all its rows shaded) and an optional selection (a line selection from Shift+arrows,
/// or a character selection from the mouse). Scrolling up, moving the cursor off the last line, or a mouse press pauses
/// following. End resumes it, and so does arriving at the tail: the last page of rows with the cursor on the last line (not
/// merely the last line with its earlier rows in view), by wheel, cursor keys, or a click without a drag. Wheel scrolling down
/// with the viewport already at the bottom moves the cursor down, so the wheel alone can reach the tail. Following stays
/// paused during a drag. The cursor and the top are remembered by absolute line number, so they
/// stay put as old lines are evicted. A selection or cursor on a partial line follows that line when another stream completes
/// a line in front of it (see <see cref="PartialPins"/>). UI thread only.
/// </summary>
internal sealed class OutputView : View, ISearchTarget
{
    private const int WheelLines = 3;
    private const int ShadePercent = 12;

    /// <summary>The longest selected text, in UTF-16 units, that seeds a search (see <see cref="SelectionSeed"/>).</summary>
    private const int SeedMaxLength = 200;

    private static readonly Color FallbackCursorShade = new(0x3A, 0x3F, 0x4B);

    private readonly MainController _controller;
    private CommandEntry? _entry;
    private OutputBuffer? _buffer;
    private long _lastVersion = -1;

    // While following, the view shows the last rows and the cursor is the last line; _top and _cursorLine are only read
    // again after a pause (see Pause).
    private bool _following = true;
    private ScrollPosition _top;
    private long _cursorLine;

    // Set when the paused cursor is on a partial line: the line's identity, so that it can be found again if its number
    // changes (see Revalidate). Null for a completed line, which keeps its number.
    private PartialPin? _cursorPin;
    private long _pinVersion = -1;

    private SelectionState? _selection;
    private bool _dragging;

    // The search: its pattern (null when there is none), the match counts per line, and the active match. The active match is the
    // origin of the next Find, and is kept as a line number, so it follows its line as old lines are evicted (see Find).
    private SearchPattern? _search;
    private readonly OutputSearchIndex _searchIndex = new();
    private ActiveMatch? _active;

    private readonly LineCache _lines = new();

    // The screen rows of the last draw (the line number and row of each, top to bottom), and the screen rectangle of the viewport
    // they were drawn in. Two arrays swap roles each frame, so a draw allocates nothing once they have grown to the height.
    // Nothing is remembered after Show, so the first draw of a shown entry never asks for a scroll.
    private (long Number, int Row)[] _drawn = [];
    private (long Number, int Row)[] _current = [];
    private int _drawnCount;
    private System.Drawing.Rectangle _drawnScreen;

    // The pins are set for endpoints on partial lines only (see PinAt).
    private readonly record struct SelectionState(
        SelectionPoint Anchor,
        SelectionPoint Active,
        bool LineMode,
        PartialPin? AnchorPin = null,
        PartialPin? ActivePin = null);

    /// <summary>The active match: its absolute line, and its offset and length in UTF-16 units of that line's plain text.</summary>
    private readonly record struct ActiveMatch(long Line, int Start, int Length);

    /// <summary>A line with its text, its cells, the attribute of each span, and the column range of each of its rows.</summary>
    private sealed record PreparedLine(
        string PlainText,
        IReadOnlyList<OutputWrap.Cell> Cells,
        IReadOnlyList<Attribute> SpanAttributes,
        IReadOnlyList<(int StartColumn, int EndColumn)> Rows);

    /// <summary>Row <paramref name="Row"/> of the absolute line <paramref name="Number"/>, as one screen row.</summary>
    private readonly record struct ScreenRow(long Number, int Row, PreparedLine Prepared)
    {
        public int StartColumn => Prepared.Rows[Row].StartColumn;

        public int EndColumn => Prepared.Rows[Row].EndColumn;

        public bool LastOfLine => Row == Prepared.Rows.Count - 1;
    }

    /// <summary>
    /// The retained lines and the viewport, measured once per operation. Row counts are wrapped when first needed and
    /// cached for the operation, so only the lines an operation reaches are wrapped.
    /// Geometry reads the buffer live (drawing and measuring): a frame built while the buffer changes is corrected by the next
    /// redraw. Only copy works from one snapshot (see <see cref="CopyTextFrom"/>).
    /// </summary>
    private sealed class Geometry
    {
        private readonly Dictionary<long, int> _rows = new();
        private readonly LineCache _cache;

        public Geometry(OutputBuffer buffer, int width, int height, LineCache cache)
        {
            _cache = cache;
            Buffer = buffer;
            Width = width;
            Height = Math.Max(0, height);
            First = buffer.FirstLineNumber;
            Total = buffer.Count;
        }

        public OutputBuffer Buffer { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Absolute number of the first retained line.</summary>
        public long First { get; }

        public int Total { get; }

        /// <summary>Absolute number of the last retained line. Last is First when there are no lines.</summary>
        public long Last => First + Math.Max(0, Total - 1);

        public ScrollPosition MaxTop => OutputScroll.MaxTop(First, Last, Height, Rows);

        /// <summary>The number of screen rows a line wraps to at this width. A line that is no longer retained counts as one row.</summary>
        public int Rows(long line)
        {
            if (!_rows.TryGetValue(line, out int rows))
            {
                (_, IReadOnlyList<OutputLine> lines) = ReadLines(Buffer, line, 1);
                rows = lines.Count == 1 ? _cache.RowCount(lines[0], Width) : 1;
                _rows[line] = rows;
            }

            return rows;
        }

        public ScrollPosition Clamp(ScrollPosition position) => OutputScroll.Clamp(position, First, Last, Height, Rows);

        public ScrollPosition ScrollBy(ScrollPosition position, int delta) =>
            OutputScroll.ScrollBy(position, delta, First, Last, Height, Rows);

        public ScrollPosition EnsureVisible(ScrollPosition top, long cursor) =>
            OutputScroll.EnsureVisible(top, cursor, First, Last, Height, Rows);

        public long PageTarget(long cursor, int direction) =>
            OutputScroll.PageTarget(cursor, direction, First, Last, Height, Rows);
    }

    public OutputView(MainController controller)
    {
        _controller = controller;
        CanFocus = true;
        TabStop = TabBehavior.TabStop;
    }

    /// <summary>True while the view shows the last lines and follows new output.</summary>
    public bool Following => _following;

    /// <summary>True when there is a selection, which c copies. A selection is never empty: it covers at least one cell or a line.</summary>
    internal bool HasSelection => _selection is not null;

    /// <summary>True when the buffer has a line (completed or partial), so c copies the cursor line. Reads the count only.</summary>
    internal bool HasLines => _buffer is { Count: > 0 };

    /// <summary>Shows the output of a command, or nothing for null. Resets scrolling, cursor and selection. Showing the same buffer again keeps them.</summary>
    public void Show(CommandEntry? entry)
    {
        OutputBuffer? buffer = entry?.Process.Output.Buffer;
        if (ReferenceEquals(entry, _entry) && ReferenceEquals(buffer, _buffer))
        {
            return;
        }

        StopDrag();
        _entry = entry;
        _buffer = buffer;
        _following = true;
        _top = default;
        _cursorLine = 0;
        _cursorPin = null;
        _selection = null;
        _active = null;
        _searchIndex.Reset(_search);
        _lastVersion = -1;
        _pinVersion = -1;
        _drawnCount = 0;
        _lines.Clear();
        SetNeedsDraw();
    }

    /// <summary>Called on the UI thread every 100 ms. Redraws when the buffer changed.</summary>
    public void Tick()
    {
        Revalidate();
        long version = _buffer?.Version ?? -1;
        if (version != _lastVersion)
        {
            _lastVersion = version;
            SetNeedsDraw();
        }
    }

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView)
    {
        base.OnHasFocusChanged(newHasFocus, previousFocusedView, focusedView);
        SetNeedsDraw();
        _controller.OnOutputFocusChanged(newHasFocus);
    }

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
            return CopySelectionOrCursorLine();
        }

        if (key == Key.CursorUp)
        {
            return MoveCursor(-1, page: false, extend: false);
        }

        if (key == Key.CursorDown)
        {
            return MoveCursor(1, page: false, extend: false);
        }

        if (key == Key.PageUp)
        {
            return MoveCursor(-1, page: true, extend: false);
        }

        if (key == Key.PageDown)
        {
            return MoveCursor(1, page: true, extend: false);
        }

        if (key == Key.CursorUp.WithShift)
        {
            return MoveCursor(-1, page: false, extend: true);
        }

        if (key == Key.CursorDown.WithShift)
        {
            return MoveCursor(1, page: false, extend: true);
        }

        if (key == Key.PageUp.WithShift)
        {
            return MoveCursor(-1, page: true, extend: true);
        }

        if (key == Key.PageDown.WithShift)
        {
            return MoveCursor(1, page: true, extend: true);
        }

        if (key == Key.Home)
        {
            return MoveToFirstLine();
        }

        if (key == Key.End)
        {
            return ResumeFollowing();
        }

        // Lines wrap, so there is nothing to scroll sideways. The keys are consumed so that focus stays here.
        if (key == Key.CursorLeft || key == Key.CursorRight)
        {
            return true;
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
            return Scroll(-WheelLines);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            return Scroll(WheelLines);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledLeft) || mouse.Flags.HasFlag(MouseFlags.WheeledRight))
        {
            return true;
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

    // Skips the framework's viewport clear: OnDrawingContent paints every cell of the viewport, so the clear is redundant work.
    protected override bool OnClearingViewport() => true;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        IReadOnlyList<ScreenRow> rows = Array.Empty<ScreenRow>();
        long cursor = -1;
        if (_buffer is not null)
        {
            Geometry geometry = Measure(_buffer);
            rows = ScreenRows(geometry, TopPosition(geometry), normal);
            _lines.NextGeneration();
            if (HasFocus && geometry.Total > 0)
            {
                cursor = CursorLine(geometry);
            }
        }

        HintScrollUp(rows, height);

        SelectionRange? selection = _selection is { } s ? SelectionRange.Between(s.Anchor, s.Active, s.LineMode) : null;
        Color shade = CursorShade(normal);

        // The matches of a line are found once per frame: the rows of a line are consecutive, and they share them. Nothing is found
        // without a search.
        IReadOnlyList<SearchMatch>? matches = null;
        long matchedLine = long.MinValue;
        int activeStart = -1;
        for (int row = 0; row < height; row++)
        {
            if (row < rows.Count)
            {
                ScreenRow screenRow = rows[row];
                Color? cursorShade = screenRow.Number == cursor ? shade : null;
                if (_search is { } search && screenRow.Number != matchedLine)
                {
                    matchedLine = screenRow.Number;
                    matches = search.Matches(screenRow.Prepared.PlainText);
                    activeStart = ActiveStartOn(screenRow.Number, matches);
                }

                DrawRow(row, screenRow, width, normal, cursorShade, selection, matches, activeStart);
            }
            else
            {
                FillRow(row, 0, width, RowFill(normal, null));
            }
        }

        return true;
    }

    /// <summary>
    /// Tells the driver that the viewport's rows moved up since the last draw, so the terminal can scroll them instead of the
    /// view resending them, then remembers this draw's rows. The hint is given before the rows are painted; the painting still
    /// writes every cell, and the output compares each cell with the terminal, so a wrong or missing hint costs bytes only. A
    /// hint is never asked for across a different screen rectangle (a resize or a move), and the arguments are checked against
    /// the driver's rows before the call, so the call cannot throw.
    /// </summary>
    private void HintScrollUp(IReadOnlyList<ScreenRow> rows, int height)
    {
        System.Drawing.Rectangle screen = ViewportToScreen();
        int previousCount = screen == _drawnScreen ? _drawnCount : 0;
        if (_current.Length < height)
        {
            _current = new (long Number, int Row)[height];
            _drawn = new (long Number, int Row)[height];
        }

        for (int i = 0; i < rows.Count; i++)
        {
            _current[i] = (rows[i].Number, rows[i].Row);
        }

        int lines = ScrollRowsFor(_drawn.AsSpan(0, previousCount), _current.AsSpan(0, rows.Count), height);
        IDriver? driver = App?.Driver;
        if (lines > 0 && driver is { } && screen.Y >= 0 && height >= 2 && screen.Y + height <= driver.Rows)
        {
            driver.ScrollRowsUp(screen.Y, height, lines);
        }

        (_drawn, _current) = (_current, _drawn);
        _drawnCount = rows.Count;
        _drawnScreen = screen;
    }

    /// <summary>
    /// The number of rows the viewport scrolled up between two draws, or 0 when it did not scroll up by a count that keeps the
    /// overlap. The first row of <paramref name="current"/> must be in <paramref name="previous"/> at a row below the top and
    /// inside the band (fewer than <paramref name="height"/> rows down), and every row of the overlap must match, so the scroll
    /// is exactly the one that moved the old rows up. A row that moved down, or that is not in the previous draw, gives 0.
    /// </summary>
    /// <remarks>
    /// Every row of the overlap is compared, which is one pass over at most <paramref name="height"/> rows. Pairs of
    /// (line number, row) are unique, so the first row of <paramref name="current"/> has at most one place in
    /// <paramref name="previous"/>.
    /// </remarks>
    internal static int ScrollRowsFor(ReadOnlySpan<(long Number, int Row)> previous, ReadOnlySpan<(long Number, int Row)> current, int height)
    {
        if (previous.IsEmpty || current.IsEmpty)
        {
            return 0;
        }

        int lines = previous.IndexOf(current[0]);
        if (lines <= 0 || lines >= height)
        {
            return 0;
        }

        for (int i = 0; i < current.Length && i + lines < previous.Length; i++)
        {
            if (current[i] != previous[i + lines])
            {
                return 0;
            }
        }

        return lines;
    }

    /// <summary>
    /// Measures the buffer for an operation. Every operation that reads a selection or the cursor measures first, so the
    /// stored coordinates are checked against the buffer before they are used.
    /// </summary>
    private Geometry Measure(OutputBuffer buffer)
    {
        Revalidate();
        return new(buffer, Viewport.Width, Viewport.Height, _lines);
    }

    /// <summary>
    /// Moves the selection endpoints and the paused cursor that are on partial lines to where their lines are now, after the
    /// buffer changed. A partial line moves to a later number when another stream or a system line completes a line in front
    /// of it, so its stored number then names other text. Each pin is found again by its source and text (see
    /// <see cref="PartialPins"/>). A selection endpoint whose line is gone (rewritten or evicted) clears the selection; the
    /// cursor keeps its number, clamped as usual. Checked only when the buffer's version changed.
    /// </summary>
    private void Revalidate()
    {
        OutputBuffer? buffer = _buffer;
        if (buffer is null || buffer.Version == _pinVersion)
        {
            return;
        }

        // The capture starts one line before the smallest pinned line (a pin moves back by at most one line), so every
        // pin resolves inside it. Without pins the tail alone is enough.
        Revalidate(buffer.Capture(RevalidationStart(buffer)));
    }

    /// <summary>
    /// Revalidates the pins against one snapshot: the snapshot's version is the one checked, so a snapshot the pins were already
    /// checked against changes nothing. The snapshot must not be older than the last check.
    /// </summary>
    private void Revalidate(BufferSnapshot snapshot)
    {
        if (snapshot.Version == _pinVersion)
        {
            return;
        }

        _pinVersion = snapshot.Version;
        if (_following)
        {
            _cursorPin = null;
        }

        if (_selection is null && _cursorPin is null)
        {
            return;
        }

        if (_selection is { } s)
        {
            SelectionPoint? anchor = Track(s.Anchor, s.AnchorPin, snapshot);
            SelectionPoint? active = Track(s.Active, s.ActivePin, snapshot);
            _selection = anchor is { } a && active is { } b ? Select(snapshot, a, b, s.LineMode) : null;
        }

        if (_cursorPin is { } pin)
        {
            long? line = PartialPins.Find(pin, _cursorLine, snapshot.FirstLineNumber, snapshot.LastLineNumber, snapshot.LineAt);
            if (line is { } l)
            {
                _cursorLine = l;
            }

            _cursorPin = line is { } found ? PinAt(snapshot, found) : null;
        }
    }

    /// <summary>
    /// The first line a revalidation reads: the smallest pinned line minus one, since a pin moves back by at most one line.
    /// Without pins, the line before the partials. The paused cursor is pinned only while not following.
    /// </summary>
    private long RevalidationStart(OutputBuffer buffer)
    {
        long start = buffer.NextLineNumber;
        if (_selection is { } s)
        {
            if (s.AnchorPin is not null)
            {
                start = Math.Min(start, s.Anchor.Line);
            }

            if (s.ActivePin is not null)
            {
                start = Math.Min(start, s.Active.Line);
            }
        }

        if (_cursorPin is not null && !_following)
        {
            start = Math.Min(start, _cursorLine);
        }

        return start - 1;
    }

    /// <summary>The point's line as found again by its pin: the same point, moved to the pinned line's new number, or null when it is gone.</summary>
    private static SelectionPoint? Track(SelectionPoint point, PartialPin? pin, BufferSnapshot snapshot)
    {
        if (pin is not { } p)
        {
            return point;
        }

        long? line = PartialPins.Find(p, point.Line, snapshot.FirstLineNumber, snapshot.LastLineNumber, snapshot.LineAt);
        return line is { } l ? point with { Line = l } : null;
    }

    /// <summary>The pin for a line in the snapshot: its identity when it is a partial line (number at or after <see cref="BufferSnapshot.NextLineNumber"/>), otherwise null.</summary>
    private static PartialPin? PinAt(BufferSnapshot snapshot, long line)
    {
        if (line < snapshot.NextLineNumber)
        {
            return null;
        }

        OutputLine? found = snapshot.LineAt(line);
        return found is null ? null : PartialPin.For(found);
    }

    /// <summary>
    /// The pin for a line of the live buffer, for the operations that move the cursor or the selection. Only the partial lines
    /// (at the tail) are read, so this is a small capture.
    /// </summary>
    private static PartialPin? PinAt(OutputBuffer buffer, long line) =>
        line < buffer.NextLineNumber ? null : PinAt(buffer.Capture(line), line);

    /// <summary>A selection between two points, with the pins of the points that are on partial lines, from a snapshot.</summary>
    private static SelectionState Select(BufferSnapshot snapshot, SelectionPoint anchor, SelectionPoint active, bool lineMode) =>
        new(anchor, active, lineMode, PinAt(snapshot, anchor.Line), PinAt(snapshot, active.Line));

    /// <summary>A selection between two points, with the pins of the points that are on partial lines, from the live buffer.</summary>
    private static SelectionState Select(OutputBuffer buffer, SelectionPoint anchor, SelectionPoint active, bool lineMode) =>
        new(anchor, active, lineMode, PinAt(buffer, anchor.Line), PinAt(buffer, active.Line));

    /// <summary>Sets the paused cursor's line, with its pin.</summary>
    private void SetCursorLine(OutputBuffer buffer, long line)
    {
        _cursorLine = line;
        _cursorPin = PinAt(buffer, line);
    }

    /// <summary>The position of the top row: the stored one, or the last page while following.</summary>
    private ScrollPosition TopPosition(Geometry geometry) => _following ? geometry.MaxTop : geometry.Clamp(_top);

    /// <summary>Absolute number of the cursor line.</summary>
    private long CursorLine(Geometry geometry) =>
        _following ? geometry.Last : Math.Clamp(_cursorLine, geometry.First, geometry.Last);

    /// <summary>Stores the current top and cursor so that following can be turned off without moving either.</summary>
    private void Pause(Geometry geometry)
    {
        if (!_following)
        {
            return;
        }

        _top = TopPosition(geometry);
        SetCursorLine(geometry.Buffer, CursorLine(geometry));
        _following = false;
    }

    /// <summary>
    /// Moves the cursor by one line, or by a page (which also scrolls by a page of rows). The view scrolls to keep the cursor
    /// line fully visible. With <paramref name="extend"/>, the selection becomes a line selection from its anchor (or from the
    /// cursor when there is none) to the new cursor line.
    /// </summary>
    private bool MoveCursor(int direction, bool page, bool extend)
    {
        if (_buffer is null)
        {
            return false;
        }

        Geometry geometry = Measure(_buffer);
        if (geometry.Total == 0)
        {
            return true;
        }

        long from = CursorLine(geometry);
        Pause(geometry);
        long target = page
            ? geometry.PageTarget(from, direction)
            : Math.Clamp(from + direction, geometry.First, geometry.Last);

        ScrollPosition top = TopPosition(geometry);
        if (page)
        {
            top = geometry.ScrollBy(top, direction * geometry.Height);
        }

        _top = geometry.EnsureVisible(top, target);
        SetCursorLine(geometry.Buffer, target);
        _following = target == geometry.Last && _top == geometry.MaxTop;

        if (extend)
        {
            SelectionPoint anchor = _selection?.Anchor ?? new SelectionPoint(from, 0);
            _selection = Select(geometry.Buffer, anchor, new SelectionPoint(target, 0), lineMode: true);
        }

        SetNeedsDraw();
        return true;
    }

    /// <summary>
    /// Wheel scrolling: moves the viewport by screen rows, not the cursor. Once the viewport is already at the bottom, scrolling
    /// down moves the cursor down instead, by as many lines; a cursor above the view first moves to the top line in view.
    /// Following resumes when the viewport is at the bottom and the cursor is on the last line. A drag's autoscroll only
    /// scrolls.
    /// </summary>
    private bool Scroll(int delta)
    {
        if (_buffer is null)
        {
            return false;
        }

        Geometry geometry = Measure(_buffer);
        ScrollPosition from = TopPosition(geometry);
        ScrollPosition target = geometry.ScrollBy(from, delta);
        Pause(geometry);
        _top = target;
        if (!_dragging && delta > 0 && target == geometry.MaxTop)
        {
            if (from == geometry.MaxTop && geometry.Total > 0)
            {
                long cursor = CursorLine(geometry);
                long next = cursor < target.Line ? target.Line : cursor + delta;
                SetCursorLine(geometry.Buffer, Math.Min(next, geometry.Last));
            }

            _following = CursorLine(geometry) == geometry.Last;
        }
        else
        {
            _following = false;
        }

        SetNeedsDraw();
        return true;
    }

    private bool MoveToFirstLine()
    {
        if (_buffer is null)
        {
            return false;
        }

        Geometry geometry = Measure(_buffer);
        if (geometry.Total == 0)
        {
            return true;
        }

        ScrollPosition top = new(geometry.First, 0);
        _top = top;
        SetCursorLine(geometry.Buffer, geometry.First);
        _following = top == geometry.MaxTop && geometry.First == geometry.Last;
        SetNeedsDraw();
        return true;
    }

    private bool ResumeFollowing()
    {
        if (_buffer is null)
        {
            return false;
        }

        _following = true;
        _cursorPin = null;
        SetNeedsDraw();
        return true;
    }

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
    /// Sets the search pattern. Null ends the search: the highlights go and the active match is dropped. A pattern keeps the active
    /// match as the origin of the next <see cref="Find"/>.
    /// </summary>
    public void SetSearch(SearchPattern? pattern)
    {
        _search = pattern;
        _searchIndex.Reset(pattern);
        if (pattern is null)
        {
            _active = null;
        }

        SetNeedsDraw();
    }

    /// <summary>
    /// Moves to the next (or previous) match after the active match, or after the top of the view when there is none, and scrolls
    /// it into view. Past the end (or start) it wraps around the retained lines and returns <see cref="SearchResult.Wrapped"/>. Pauses
    /// following, also at the tail. Changes nothing when there is no match.
    /// </summary>
    public SearchResult Find(bool backward, bool inclusive)
    {
        SearchPattern? pattern = _search;
        OutputBuffer? buffer = _buffer;
        if (pattern is null || buffer is null)
        {
            return SearchResult.NotFound;
        }

        Geometry geometry = Measure(buffer);

        // The snapshot is taken before the index is updated, so the index counts at least the lines the snapshot holds.
        BufferSnapshot snapshot = buffer.Capture(geometry.First);
        if (snapshot.LastLineNumber < snapshot.FirstLineNumber)
        {
            return SearchResult.NotFound;
        }

        _searchIndex.Update(buffer);
        long first = snapshot.FirstLineNumber;
        long last = snapshot.LastLineNumber;

        // The origin is the active match when it is retained. Otherwise it is the edge of the view in the search direction: the top
        // row going forward, the last row going backward.
        long originLine;
        int originStart;
        bool originInclusive = inclusive;
        if (_active is { } active && active.Line >= first && active.Line <= last)
        {
            originLine = active.Line;
            originStart = active.Start;
        }
        else if (backward)
        {
            originLine = ViewLine(geometry, last: true);
            originStart = int.MaxValue;
        }
        else
        {
            originLine = ViewLine(geometry, last: false);
            originStart = 0;
            originInclusive = true;
        }

        originLine = Math.Clamp(originLine, first, last);

        // Positions compare by line, then by start. Going backward the matches before the origin come first, going forward the ones after it.
        bool Past(long line, int start)
        {
            int order = line != originLine ? line.CompareTo(originLine) : start.CompareTo(originStart);
            bool inOrder = backward ? order < 0 : order > 0;
            return inOrder || originInclusive && order == 0;
        }

        bool AnyMatch(long line, int start) => true;

        // The first match past the origin, or else, after wrapping, the first match from the other end up to the origin.
        ActiveMatch? match = backward
            ? FirstMatch(pattern, snapshot, originLine, first, backward: true, Past)
            : FirstMatch(pattern, snapshot, originLine, last, backward: false, Past);
        bool wrapped = match is null;
        match ??= backward
            ? FirstMatch(pattern, snapshot, last, originLine, backward: true, AnyMatch)
            : FirstMatch(pattern, snapshot, first, originLine, backward: false, AnyMatch);
        if (match is not { } target)
        {
            return SearchResult.NotFound;
        }

        _active = target;
        Pause(geometry);
        SetCursorLine(buffer, target.Line);
        ScrollToMatch(geometry, snapshot.LineAt(target.Line)!, target);
        SetNeedsDraw();
        return wrapped ? SearchResult.Wrapped : SearchResult.Found;
    }

    /// <summary>
    /// The number of matches on the retained lines, and the 1-based number of the active match among them (0 when there is no active
    /// match, or it is no longer a match of the pattern).
    /// </summary>
    public SearchStatus SearchStatus()
    {
        SearchPattern? pattern = _search;
        OutputBuffer? buffer = _buffer;
        if (pattern is null || buffer is null)
        {
            return new SearchStatus(0, 0);
        }

        _searchIndex.Update(buffer);
        int active = 0;
        if (_active is { } current && ReadLines(buffer, current.Line, 1) is (long found, [OutputLine line]) && found == current.Line)
        {
            IReadOnlyList<SearchMatch> matches = pattern.Matches(line.PlainText);
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Start == current.Start && matches[i].Length == current.Length)
                {
                    active = _searchIndex.CountBefore(current.Line) + i + 1;
                    break;
                }
            }
        }

        return new SearchStatus(_searchIndex.Total, active);
    }

    /// <summary>
    /// The selected text a search starts from: a character selection within one buffer line, of at most 200 UTF-16 units. The
    /// seed is the text, the absolute number of its line, and the UTF-16 offset of the text in the line's plain text, which are the
    /// units of the matches. Null when there is no selection, it is a line selection or spans lines, or its text is empty or too
    /// long. The selection and the line are read from one snapshot, as the copy reads them (see <see cref="CopyTextFrom"/>).
    /// </summary>
    internal SearchSeed? SelectionSeed()
    {
        if (_selection is not { LineMode: false })
        {
            return null;
        }

        BufferSnapshot? snapshot = CaptureForCopy();
        if (snapshot is null)
        {
            return null;
        }

        Revalidate(snapshot);
        if (_selection is not { LineMode: false } selection)
        {
            return null;
        }

        SelectionRange range = SelectionRange.Between(selection.Anchor, selection.Active, lineMode: false);
        if (range.Start.Line != range.End.Line || snapshot.LineAt(range.Start.Line)?.PlainText is not { } plain)
        {
            return null;
        }

        string text = SelectionText.Slice(plain, range.Start.Column, range.End.Column);
        if (text.Length == 0 || text.Length > SeedMaxLength)
        {
            return null;
        }

        return new SearchSeed(text, range.Start.Line, FirstCellOffset(plain, range.Start.Column, range.End.Column));
    }

    /// <summary>
    /// Makes the seed's text the active match, the origin of the next <see cref="Find"/>, so that a Find with <c>inclusive</c> set
    /// takes the selected occurrence first. Call after <see cref="SetSearch"/>. The text is highlighted only when it is a match.
    /// </summary>
    internal void ActivateSeed(SearchSeed seed) =>
        _active = new ActiveMatch(seed.Line, seed.Start, seed.Text.Length);

    /// <summary>
    /// The UTF-16 offset of the first character that <see cref="SelectionText.Slice"/> takes for the columns from
    /// <paramref name="fromColumn"/> to <paramref name="toColumn"/>. Zero when no character is in those columns.
    /// </summary>
    private static int FirstCellOffset(string text, int fromColumn, int toColumn)
    {
        int column = 0;
        foreach ((int offset, int _, int width) in TextWidth.Graphemes(text))
        {
            if (width > 0 && column + width - 1 >= fromColumn && column <= toColumn)
            {
                return offset;
            }

            column += width;
        }

        return 0;
    }

    /// <summary>
    /// The first match, in search order, on the lines from <paramref name="from"/> to <paramref name="to"/> that <paramref name="accept"/>
    /// takes. Going backward the lines and the matches on each line run downward, otherwise upward. A completed line that the index
    /// counted no match on is skipped; a partial line is always matched, since its count is not final.
    /// </summary>
    private ActiveMatch? FirstMatch(
        SearchPattern pattern,
        BufferSnapshot snapshot,
        long from,
        long to,
        bool backward,
        Func<long, int, bool> accept)
    {
        long step = backward ? -1 : 1;
        for (long line = from; line != to + step; line += step)
        {
            if (line < snapshot.NextLineNumber && _searchIndex.CountOn(line) == 0)
            {
                continue;
            }

            IReadOnlyList<SearchMatch> matches = pattern.Matches(snapshot.LineAt(line)!.PlainText);
            for (int i = 0; i < matches.Count; i++)
            {
                SearchMatch match = matches[backward ? matches.Count - 1 - i : i];
                if (accept(line, match.Start))
                {
                    return new ActiveMatch(line, match.Start, match.Length);
                }
            }
        }

        return null;
    }

    /// <summary>The line of the first row shown in the view (the last row when <paramref name="last"/>). The first or last retained line when no row is shown.</summary>
    private long ViewLine(Geometry geometry, bool last)
    {
        IReadOnlyList<ScreenRow> rows = ScreenRows(geometry, TopPosition(geometry), GetAttributeForRole(VisualRole.Normal));
        if (rows.Count == 0)
        {
            return last ? geometry.Last : geometry.First;
        }

        return last ? rows[rows.Count - 1].Number : rows[0].Number;
    }

    /// <summary>
    /// Scrolls so that the match is on screen. Nothing changes when its row is already shown. A line that fits the view goes fully into
    /// view. A taller line puts the match's row at the top, clamped to the last page.
    /// </summary>
    private void ScrollToMatch(Geometry geometry, OutputLine line, ActiveMatch match)
    {
        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        ScrollPosition top = TopPosition(geometry);
        PreparedLine prepared = _lines.Prepare(line, geometry.Width, normal);
        int row = MatchRow(prepared, match.Start);
        if (ScreenRows(geometry, top, normal).Any(shown => shown.Number == match.Line && shown.Row == row))
        {
            return;
        }

        _top = prepared.Rows.Count <= geometry.Height
            ? geometry.EnsureVisible(top, match.Line)
            : geometry.Clamp(new ScrollPosition(match.Line, row));
    }

    /// <summary>The row of a prepared line that holds the first cell the match touches, or row 0 when no cell does.</summary>
    private static int MatchRow(PreparedLine prepared, int start)
    {
        IReadOnlyList<OutputWrap.Cell> cells = prepared.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].Offset + cells[i].Length > start)
            {
                for (int row = 0; row < prepared.Rows.Count; row++)
                {
                    if (cells[i].Column < prepared.Rows[row].EndColumn)
                    {
                        return row;
                    }
                }

                return prepared.Rows.Count - 1;
            }
        }

        return 0;
    }

    /// <summary>The start of the active match when it is one of <paramref name="matches"/> on line <paramref name="number"/>, or -1.</summary>
    private int ActiveStartOn(long number, IReadOnlyList<SearchMatch> matches)
    {
        if (_active is not { } active || active.Line != number)
        {
            return -1;
        }

        foreach (SearchMatch match in matches)
        {
            if (match.Start == active.Start && match.Length == active.Length)
            {
                return active.Start;
            }
        }

        return -1;
    }

    /// <summary>The index of the first match that ends after <paramref name="offset"/>, or the count when there is none. The matches are in order.</summary>
    private static int FirstMatchEndingAfter(IReadOnlyList<SearchMatch> matches, int offset)
    {
        int low = 0;
        int high = matches.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (matches[middle].End <= offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// The attribute of a cell that a match covers: the active match's color when it covers the active match, else the match color,
    /// and <paramref name="attr"/> unchanged when no match covers it. <paramref name="matchIndex"/> moves forward over the matches the
    /// cell is past, since the cells of a row come in order.
    /// </summary>
    private static Attribute HighlightAttribute(Attribute attr, IReadOnlyList<SearchMatch> matches, ref int matchIndex, OutputWrap.Cell cell, int activeStart)
    {
        int cellEnd = cell.Offset + cell.Length;
        while (matchIndex < matches.Count && matches[matchIndex].End <= cell.Offset)
        {
            matchIndex++;
        }

        bool covered = false;
        bool active = false;
        for (int m = matchIndex; m < matches.Count && matches[m].Start < cellEnd; m++)
        {
            covered = true;
            active |= matches[m].Start == activeStart;
        }

        if (!covered)
        {
            return attr;
        }

        return active ? SearchColors.Active(attr) : SearchColors.Match(attr);
    }

    /// <summary>
    /// The character position under a viewport cell. On a row that is not the last row of its line, the column stops at the
    /// row's last cell, so a drag past the right edge selects to the end of the row.
    /// </summary>
    private SelectionPoint PointAt(Geometry geometry, int row, int column)
    {
        IReadOnlyList<ScreenRow> rows = ScreenRows(geometry, TopPosition(geometry), GetAttributeForRole(VisualRole.Normal));
        if (rows.Count == 0)
        {
            return new SelectionPoint(geometry.First, 0);
        }

        ScreenRow target = rows[Math.Clamp(row, 0, rows.Count - 1)];
        int offset = Math.Max(0, column);
        if (!target.LastOfLine)
        {
            offset = Math.Min(offset, Math.Max(0, target.EndColumn - target.StartColumn - 1));
        }

        return new SelectionPoint(target.Number, target.StartColumn + offset);
    }

    /// <summary>
    /// Left press: focuses the view, moves the cursor to the cell and starts a character selection. The mouse is grabbed,
    /// so the drag keeps reporting when the pointer leaves the view.
    /// </summary>
    private bool BeginDrag(Mouse mouse)
    {
        SetFocus();
        if (_buffer is null)
        {
            return true;
        }

        Geometry geometry = Measure(_buffer);
        if (geometry.Total == 0)
        {
            return true;
        }

        Pause(geometry);
        SelectionPoint point = PointAt(geometry, mouse.Position?.Y ?? 0, mouse.Position?.X ?? 0);
        SetCursorLine(geometry.Buffer, point.Line);
        _following = false;
        _selection = Select(geometry.Buffer, point, point, lineMode: false);
        _dragging = true;
        App?.Mouse.GrabMouse(this);
        SetNeedsDraw();
        return true;
    }

    /// <summary>
    /// Drag: extends the character selection to the cell under the pointer and moves the cursor there. Dragging above or
    /// below the view scrolls by one screen row per motion event.
    /// </summary>
    private void DragTo(Mouse mouse)
    {
        if (_buffer is null || _selection is null)
        {
            return;
        }

        Geometry geometry = Measure(_buffer);
        if (geometry.Total == 0)
        {
            return;
        }

        int row = mouse.Position?.Y ?? 0;
        int column = mouse.Position?.X ?? 0;
        if (row < 0)
        {
            Scroll(-1);
            row = 0;
        }
        else if (row >= Math.Max(1, geometry.Height))
        {
            Scroll(1);
            row = Math.Max(1, geometry.Height) - 1;
        }

        geometry = Measure(_buffer);
        if (_selection is not { } selection)
        {
            // The anchor's line was rewritten or evicted while autoscrolling, which cleared the selection.
            return;
        }

        Pause(geometry);
        SelectionPoint point = PointAt(geometry, row, column);
        SetCursorLine(geometry.Buffer, point.Line);
        _following = false;
        _selection = Select(geometry.Buffer, selection.Anchor, point, selection.LineMode);
        SetNeedsDraw();
    }

    /// <summary>
    /// Left release: releases the grab. A press without a drag leaves no selection. Following resumes only when the viewport
    /// is at the tail and the cursor is on the last line, so a plain click there keeps following.
    /// </summary>
    private void EndDrag()
    {
        Revalidate();
        StopDrag();
        if (_selection is { LineMode: false } s && s.Anchor == s.Active)
        {
            _selection = null;
        }

        if (_selection is null && _buffer is not null)
        {
            Geometry geometry = Measure(_buffer);
            if (geometry.Total > 0)
            {
                _following = CursorLine(geometry) == geometry.Last && TopPosition(geometry) == geometry.MaxTop;
            }
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

    /// <summary>
    /// Copies the selection, or the cursor line when there is no selection. The text goes to the controller, which writes
    /// it to the clipboard and reports the result.
    /// </summary>
    private bool CopySelectionOrCursorLine()
    {
        if (_buffer is null)
        {
            return false;
        }

        string? text = CopyTextFrom(CaptureForCopy());
        if (text is not null)
        {
            _controller.CopyToClipboard(text, $"Copied {CharacterCount(text)} characters");
        }

        return true;
    }

    /// <summary>
    /// The snapshot a copy works from: the lines from one line before the selection's first line, or before the cursor line
    /// when there is no selection, to the end. Null when there is no buffer.
    /// </summary>
    internal BufferSnapshot? CaptureForCopy()
    {
        OutputBuffer? buffer = _buffer;
        if (buffer is null)
        {
            return null;
        }

        long from;
        if (_selection is { } s)
        {
            from = Math.Min(s.Anchor.Line, s.Active.Line) - 1;
        }
        else
        {
            // Following copies the last line, which is at or after the line before the next line number, so the capture covers it.
            from = (_following ? buffer.NextLineNumber : _cursorLine) - 1;
        }

        return buffer.Capture(from);
    }

    /// <summary>
    /// The text a copy takes, read entirely from <paramref name="snapshot"/>: the selection's text, or the cursor line's text when
    /// there is no selection. The pins are revalidated against the same snapshot the text is extracted from, so output that
    /// arrives meanwhile cannot shift the lines between the check and the extraction. Null when there is nothing to copy.
    /// </summary>
    internal string? CopyTextFrom(BufferSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        Revalidate(snapshot);
        if (_selection is { } s)
        {
            SelectionRange range = SelectionRange.Between(s.Anchor, s.Active, s.LineMode);
            long count = Math.Min(int.MaxValue, range.End.Line - range.Start.Line + 1);
            (long first, IReadOnlyList<OutputLine> lines) = snapshot.LinesFrom(range.Start.Line, count);
            return SelectionText.Extract(lines.Select(l => l.PlainText).ToList(), first, range);
        }

        if (snapshot.LastLineNumber < snapshot.FirstLineNumber)
        {
            return null;
        }

        long cursor = _following
            ? snapshot.LastLineNumber
            : Math.Clamp(_cursorLine, snapshot.FirstLineNumber, snapshot.LastLineNumber);
        return snapshot.LineAt(cursor)?.PlainText ?? string.Empty;
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> lines from absolute line <paramref name="from"/>, clipped to the retained lines.
    /// Returns the absolute number of the first line returned. Retries when the buffer evicts lines during the read, so the
    /// numbers always match the lines.
    /// </summary>
    private static (long First, IReadOnlyList<OutputLine> Lines) ReadLines(OutputBuffer buffer, long from, long count)
    {
        while (true)
        {
            long first = buffer.FirstLineNumber;
            long start = Math.Max(from, first);
            long available = Math.Min(int.MaxValue, count - (start - from));
            IReadOnlyList<OutputLine> lines = available > 0
                ? buffer.GetLines((int)(start - first), (int)available)
                : Array.Empty<OutputLine>();
            if (buffer.FirstLineNumber == first)
            {
                return (start, lines);
            }
        }
    }

    /// <summary>
    /// The screen rows from the top position, in order, until the viewport is full or the lines run out. Only the lines the
    /// viewport reaches are read and wrapped; a line is wrapped once and shared by its rows.
    /// </summary>
    private List<ScreenRow> ScreenRows(Geometry geometry, ScrollPosition top, Attribute normal)
    {
        var rows = new List<ScreenRow>(geometry.Height);
        (long first, IReadOnlyList<OutputLine> lines) = ReadLines(geometry.Buffer, top.Line, geometry.Height);
        for (int i = 0; i < lines.Count && rows.Count < geometry.Height; i++)
        {
            long number = first + i;
            PreparedLine prepared = _lines.Prepare(lines[i], geometry.Width, normal);
            int firstRow = number == top.Line ? Math.Min(top.Row, prepared.Rows.Count - 1) : 0;
            for (int row = firstRow; row < prepared.Rows.Count && rows.Count < geometry.Height; row++)
            {
                rows.Add(new ScreenRow(number, row, prepared));
            }
        }

        return rows;
    }

    private static PreparedLine Prepare(OutputLine line, int width, Attribute normal)
    {
        List<OutputWrap.Cell> cells = OutputWrap.Cells(line);
        ReadOnlySpan<StyleRun> runs = line.Runs;
        var spanAttributes = new Attribute[runs.Length];
        for (int i = 0; i < runs.Length; i++)
        {
            spanAttributes[i] = ColorMap.ToAttribute(runs[i].Style, line.Source, normal);
        }

        return new PreparedLine(line.PlainText, cells, spanAttributes, OutputWrap.Rows(cells.Select(cell => cell.Width).ToList(), width));
    }

    /// <summary>
    /// The prepared lines and row counts of recent frames, so a redraw (10 a second while output streams) wraps only the lines
    /// it has not seen. Keyed by the line object: completed lines never change, and a partial line publishes a new object
    /// whenever its text changes. Keeps what the last two frames used (see <see cref="NextGeneration"/>), so it holds about two
    /// screens of lines. Cleared when the width changes; prepared lines also when the normal attribute changes.
    /// </summary>
    private sealed class LineCache
    {
        private Dictionary<OutputLine, PreparedLine> _prepared = new(ReferenceEqualityComparer.Instance);
        private Dictionary<OutputLine, PreparedLine> _preparedBefore = new(ReferenceEqualityComparer.Instance);
        private Dictionary<OutputLine, int> _rowCounts = new(ReferenceEqualityComparer.Instance);
        private Dictionary<OutputLine, int> _rowCountsBefore = new(ReferenceEqualityComparer.Instance);
        private int _width = -1;
        private Attribute _normal;

        public PreparedLine Prepare(OutputLine line, int width, Attribute normal)
        {
            UseWidth(width);
            if (!normal.Equals(_normal))
            {
                _normal = normal;
                _prepared.Clear();
                _preparedBefore.Clear();
            }

            if (!_prepared.TryGetValue(line, out PreparedLine? prepared))
            {
                if (!_preparedBefore.Remove(line, out prepared))
                {
                    prepared = OutputView.Prepare(line, width, normal);
                }

                _prepared[line] = prepared;
            }

            return prepared;
        }

        public int RowCount(OutputLine line, int width)
        {
            UseWidth(width);
            if (_prepared.TryGetValue(line, out PreparedLine? prepared) || _preparedBefore.TryGetValue(line, out prepared))
            {
                return prepared.Rows.Count;
            }

            if (!_rowCounts.TryGetValue(line, out int rows))
            {
                if (!_rowCountsBefore.Remove(line, out rows))
                {
                    rows = OutputWrap.RowCount(line, width);
                }

                _rowCounts[line] = rows;
            }

            return rows;
        }

        /// <summary>Called after each frame: what the frame before it used and this one did not is dropped.</summary>
        public void NextGeneration()
        {
            (_preparedBefore, _prepared) = (_prepared, _preparedBefore);
            _prepared.Clear();
            (_rowCountsBefore, _rowCounts) = (_rowCounts, _rowCountsBefore);
            _rowCounts.Clear();
        }

        /// <summary>Drops everything, for a different buffer.</summary>
        public void Clear()
        {
            _prepared.Clear();
            _preparedBefore.Clear();
            _rowCounts.Clear();
            _rowCountsBefore.Clear();
        }

        private void UseWidth(int width)
        {
            if (width != _width)
            {
                _width = width;
                Clear();
            }
        }
    }

    private static int CharacterCount(string text) => text.EnumerateRunes().Count();

    /// <summary>
    /// Draws one screen row: the cells of the row's column range, styled as the span they belong to, with the cursor shade,
    /// the selection, the search highlights and the padding exactly as the line would have them. Pads the rest of the row.
    /// <paramref name="matches"/> are the matches of the row's line (null without a search), and <paramref name="activeStart"/> is the
    /// start of the active match when it is one of them, or -1.
    /// </summary>
    private void DrawRow(
        int screenRow,
        ScreenRow row,
        int width,
        Attribute normal,
        Color? cursorShade,
        SelectionRange? selection,
        IReadOnlyList<SearchMatch>? matches,
        int activeStart)
    {
        int start = row.StartColumn;
        int end = row.EndColumn;
        int drawn = 0;
        var run = new StringBuilder();
        Attribute? runAttr = null;
        int runStart = 0;

        void Flush()
        {
            if (runAttr is { } attr && run.Length > 0)
            {
                Move(runStart, screenRow);
                SetAttribute(attr);
                AddStr(run.ToString());
            }

            run.Clear();
        }

        IReadOnlyList<OutputWrap.Cell> cells = row.Prepared.Cells;
        int first = OutputWrap.FirstCellAt(cells, start);
        int matchIndex = matches is null || first >= cells.Count ? 0 : FirstMatchEndingAfter(matches, cells[first].Offset);
        for (int i = first; i < cells.Count && cells[i].Column < end; i++)
        {
            OutputWrap.Cell cell = cells[i];
            int offset = cell.Column - start;
            if (offset + cell.Width > width)
            {
                // A wide character wider than the view itself: nothing of it fits.
                break;
            }

            Attribute attr = row.Prepared.SpanAttributes[cell.Span];
            if (cursorShade is { } shade)
            {
                attr = Shade(attr, normal, shade);
            }

            if (selection is { } range && range.Covers(row.Number, cell.Column, cell.Width))
            {
                attr = Invert(attr);
            }

            if (matches is not null)
            {
                attr = HighlightAttribute(attr, matches, ref matchIndex, cell, activeStart);
            }

            if (runAttr is null || !runAttr.Value.Equals(attr))
            {
                Flush();
                runAttr = attr;
                runStart = offset;
            }

            run.Append(cell.Text);
            drawn = offset + cell.Width;
        }

        Flush();
        FillRow(screenRow, drawn, width, RowFill(normal, cursorShade));
    }

    private void FillRow(int row, int fromCol, int width, Attribute attr)
    {
        if (fromCol >= width)
        {
            return;
        }

        Move(fromCol, row);
        SetAttribute(attr);
        AddStr(new string(' ', width - fromCol));
    }

    /// <summary>The cursor-line background is a shade of the normal background, toward the normal foreground.</summary>
    private static Color CursorShade(Attribute normal)
    {
        Color bg = normal.Background;
        Color fg = normal.Foreground;
        if (bg.A == 0 || fg.A == 0)
        {
            return FallbackCursorShade;
        }

        return new Color(Blend(bg.R, fg.R), Blend(bg.G, fg.G), Blend(bg.B, fg.B));

        static int Blend(int from, int to) => from + (to - from) * ShadePercent / 100;
    }

    /// <summary>On the cursor line, cells with the default background take the shade; explicitly colored cells keep theirs.</summary>
    private static Attribute Shade(Attribute attr, Attribute normal, Color shade) =>
        attr.Background.Equals(normal.Background) ? new Attribute(attr.Foreground, shade, attr.Style) : attr;

    /// <summary>
    /// Selected cells use the terminal's reverse video rather than swapped colors: a swap does nothing when a color is the
    /// terminal default (Color.None), which is the usual case for plain output.
    /// </summary>
    internal static Attribute Invert(Attribute attr) => new(attr.Foreground, attr.Background,
        attr.Style.HasFlag(TextStyle.Reverse) ? attr.Style & ~TextStyle.Reverse : attr.Style | TextStyle.Reverse);

    private static Attribute RowFill(Attribute normal, Color? cursorShade) =>
        cursorShade is { } shade ? new Attribute(normal.Foreground, shade, normal.Style) : normal;
}
