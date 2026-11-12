using System.Text;
using Cmdr.Core.Output;
using Cmdr.Core.Session;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// Shows the output buffer of one command. Long lines wrap to the view width, cell by cell like a terminal; nothing is
/// dropped, and copy takes the original lines. Scrolling moves by screen rows, tail-follows by default. The view has a
/// cursor line (shown while focused, all its rows shaded) and an optional selection (a line selection from Shift+arrows,
/// or a character selection from the mouse). Scrolling up or moving the cursor off the last line pauses following; End, or
/// moving back onto the last line, resumes it. The cursor and the top are remembered by absolute line number, so they
/// stay put as old lines are evicted. UI thread only.
/// </summary>
internal sealed class OutputView : View
{
    private const int WheelLines = 3;
    private const int ShadePercent = 12;
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

    private SelectionState? _selection;
    private bool _dragging;

    private readonly record struct SelectionState(SelectionPoint Anchor, SelectionPoint Active, bool LineMode);

    /// <summary>A line with its cells, the attribute of each span, and the column range of each of its rows.</summary>
    private sealed record PreparedLine(
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
    /// </summary>
    private sealed class Geometry
    {
        private readonly Dictionary<long, int> _rows = new();

        public Geometry(OutputBuffer buffer, int width, int height)
        {
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
                rows = lines.Count == 1 ? OutputWrap.RowCount(lines[0], Width) : 1;
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
        _selection = null;
        _lastVersion = -1;
        SetNeedsDraw();
    }

    /// <summary>Called on the UI thread every 100 ms. Redraws when the buffer changed.</summary>
    public void Tick()
    {
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

        // Esc means "stop" in cmdr; in this view it clears the selection and never reaches the application.
        if (key == Key.Esc)
        {
            return ClearSelection();
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
            if (HasFocus && geometry.Total > 0)
            {
                cursor = CursorLine(geometry);
            }
        }

        SelectionRange? selection = _selection is { } s ? SelectionRange.Between(s.Anchor, s.Active, s.LineMode) : null;
        Color shade = CursorShade(normal);

        for (int row = 0; row < height; row++)
        {
            if (row < rows.Count)
            {
                ScreenRow screenRow = rows[row];
                Color? cursorShade = screenRow.Number == cursor ? shade : null;
                DrawRow(row, screenRow, width, normal, cursorShade, selection);
            }
            else
            {
                FillRow(row, 0, width, RowFill(normal, null));
            }
        }

        return true;
    }

    private Geometry Measure(OutputBuffer buffer) => new(buffer, Viewport.Width, Viewport.Height);

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
        _cursorLine = CursorLine(geometry);
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
        _cursorLine = target;
        _following = target == geometry.Last;

        if (extend)
        {
            SelectionPoint anchor = _selection?.Anchor ?? new SelectionPoint(from, 0);
            _selection = new SelectionState(anchor, new SelectionPoint(target, 0), LineMode: true);
        }

        SetNeedsDraw();
        return true;
    }

    /// <summary>Wheel scrolling: moves the viewport by screen rows, not the cursor. Reaching the bottom resumes following.</summary>
    private bool Scroll(int delta)
    {
        if (_buffer is null)
        {
            return false;
        }

        Geometry geometry = Measure(_buffer);
        ScrollPosition target = geometry.ScrollBy(TopPosition(geometry), delta);
        Pause(geometry);
        _top = target;
        _following = delta > 0 && target == geometry.MaxTop;
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

        _top = new ScrollPosition(geometry.First, 0);
        _cursorLine = geometry.First;
        _following = geometry.Last == geometry.First;
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
        _cursorLine = point.Line;
        _following = point.Line == geometry.Last;
        _selection = new SelectionState(point, point, LineMode: false);
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
        Pause(geometry);
        SelectionPoint point = PointAt(geometry, row, column);
        _cursorLine = point.Line;
        _following = point.Line == geometry.Last;
        SelectionState selection = _selection.Value;
        _selection = selection with { Active = point };
        SetNeedsDraw();
    }

    /// <summary>Left release: releases the grab. A press without a drag leaves no selection.</summary>
    private void EndDrag()
    {
        StopDrag();
        if (_selection is { LineMode: false } s && s.Anchor == s.Active)
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

        if (_selection is { } s)
        {
            SelectionRange range = SelectionRange.Between(s.Anchor, s.Active, s.LineMode);
            long count = Math.Min(int.MaxValue, range.End.Line - range.Start.Line + 1);
            (long first, IReadOnlyList<OutputLine> lines) = ReadLines(_buffer, range.Start.Line, count);
            string text = SelectionText.Extract(lines.Select(l => l.PlainText).ToList(), first, range);
            _controller.CopyToClipboard(text, $"Copied {CharacterCount(text)} characters");
            return true;
        }

        Geometry geometry = Measure(_buffer);
        if (geometry.Total == 0)
        {
            return true;
        }

        (_, IReadOnlyList<OutputLine> cursorLines) = ReadLines(_buffer, CursorLine(geometry), 1);
        string line = cursorLines.Count > 0 ? cursorLines[0].PlainText : string.Empty;
        _controller.CopyToClipboard(line, $"Copied {CharacterCount(line)} characters");
        return true;
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
    private static List<ScreenRow> ScreenRows(Geometry geometry, ScrollPosition top, Attribute normal)
    {
        var rows = new List<ScreenRow>(geometry.Height);
        (long first, IReadOnlyList<OutputLine> lines) = ReadLines(geometry.Buffer, top.Line, geometry.Height);
        for (int i = 0; i < lines.Count && rows.Count < geometry.Height; i++)
        {
            long number = first + i;
            PreparedLine prepared = Prepare(lines[i], geometry.Width, normal);
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
        Attribute[] spanAttributes = line.Spans.Select(span => ColorMap.ToAttribute(span.Style, line.Source, normal)).ToArray();
        return new PreparedLine(cells, spanAttributes, OutputWrap.Rows(cells.Select(cell => cell.Width).ToList(), width));
    }

    private static int CharacterCount(string text) => text.EnumerateRunes().Count();

    /// <summary>
    /// Draws one screen row: the cells of the row's column range, styled as the span they belong to, with the cursor shade,
    /// the selection and the padding exactly as the line would have them. Pads the rest of the row.
    /// </summary>
    private void DrawRow(int screenRow, ScreenRow row, int width, Attribute normal, Color? cursorShade, SelectionRange? selection)
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
        for (int i = OutputWrap.FirstCellAt(cells, start); i < cells.Count && cells[i].Column < end; i++)
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

            if (runAttr is null || !runAttr.Value.Equals(attr))
            {
                Flush();
                runAttr = attr;
                runStart = offset;
            }

            run.Append(cell.Rune.ToString());
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
    private static Attribute Invert(Attribute attr) => new(attr.Foreground, attr.Background, attr.Style | TextStyle.Reverse);

    private static Attribute RowFill(Attribute normal, Color? cursorShade) =>
        cursorShade is { } shade ? new Attribute(normal.Foreground, shade, normal.Style) : normal;
}
