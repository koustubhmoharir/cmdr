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
/// Shows the output buffer of one command: no wrapping, tail-follow by default. The view has a cursor line (shown while
/// focused) and an optional selection (a line selection from Shift+arrows, or a character selection from the mouse).
/// Scrolling up or moving the cursor off the last line pauses following; End, or moving back onto the last line, resumes
/// it. The cursor and the top line are remembered by absolute number, so they stay put as old lines are evicted. UI thread only.
/// </summary>
internal sealed class OutputView : View
{
    private const int WheelLines = 3;
    private const int HorizontalStep = 4;
    private const int ShadePercent = 12;
    private static readonly Color FallbackCursorShade = new(0x3A, 0x3F, 0x4B);

    private readonly MainController _controller;
    private CommandEntry? _entry;
    private OutputBuffer? _buffer;
    private long _lastVersion = -1;

    // While following, the view shows the last page and the cursor is the last line; _topLine and _cursorLine are
    // only read again after a pause (see Pause).
    private bool _following = true;
    private long _topLine;
    private long _cursorLine;
    private int _hOffset;

    private SelectionState? _selection;
    private bool _dragging;

    private readonly record struct SelectionState(SelectionPoint Anchor, SelectionPoint Active, bool LineMode);

    /// <summary>Retained lines and the viewport height, read once per operation. Last is First when there are no lines.</summary>
    private readonly record struct Geometry(long First, int Total, int Height)
    {
        public long Last => First + Math.Max(0, Total - 1);

        public long MaxTopLine => First + Math.Max(0, Total - Height);
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
        _topLine = 0;
        _cursorLine = 0;
        _hOffset = 0;
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
            _controller.ToggleFocusPane();
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

        int page = Math.Max(1, Viewport.Height);
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

        if (key == Key.CursorLeft)
        {
            return PanHorizontal(-HorizontalStep);
        }

        if (key == Key.CursorRight)
        {
            return PanHorizontal(HorizontalStep);
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        bool shift = mouse.Flags.HasFlag(MouseFlags.Shift);
        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            return shift ? PanHorizontal(-HorizontalStep) : Scroll(-WheelLines);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            return shift ? PanHorizontal(HorizontalStep) : Scroll(WheelLines);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledLeft))
        {
            return PanHorizontal(-HorizontalStep);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledRight))
        {
            return PanHorizontal(HorizontalStep);
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
        IReadOnlyList<OutputLine> lines = Array.Empty<OutputLine>();
        long first = 0;
        long cursor = -1;
        if (_buffer is not null)
        {
            Geometry geometry = Measure(_buffer, height);
            (first, lines) = ReadLines(_buffer, TopLine(geometry), height);
            if (HasFocus && geometry.Total > 0)
            {
                cursor = CursorLine(geometry);
            }
        }

        SelectionRange? selection = _selection is { } s ? SelectionRange.Between(s.Anchor, s.Active, s.LineMode) : null;
        Color shade = CursorShade(normal);

        for (int row = 0; row < height; row++)
        {
            long number = first + row;
            Color? cursorShade = number == cursor ? shade : null;
            if (row < lines.Count)
            {
                DrawLine(lines[row], number, row, width, normal, cursorShade, selection);
            }
            else
            {
                FillRow(row, 0, width, RowFill(normal, cursorShade));
            }
        }

        return true;
    }

    private Geometry Measure(OutputBuffer buffer, int height) =>
        new(buffer.FirstLineNumber, buffer.Count, Math.Max(0, height));

    private Geometry Measure(OutputBuffer buffer) => Measure(buffer, Viewport.Height);

    /// <summary>Absolute number of the first visible line.</summary>
    private long TopLine(Geometry geometry) =>
        _following ? geometry.MaxTopLine : Math.Clamp(_topLine, geometry.First, geometry.MaxTopLine);

    /// <summary>Absolute number of the cursor line.</summary>
    private long CursorLine(Geometry geometry) =>
        _following ? geometry.Last : Math.Clamp(_cursorLine, geometry.First, geometry.Last);

    /// <summary>Stores the current top line and cursor so that following can be turned off without moving either.</summary>
    private void Pause(Geometry geometry)
    {
        if (!_following)
        {
            return;
        }

        _topLine = TopLine(geometry);
        _cursorLine = CursorLine(geometry);
        _following = false;
    }

    /// <summary>
    /// Moves the cursor by one line, or by a page (which also moves the viewport). The view scrolls to keep the cursor
    /// visible. With <paramref name="extend"/>, the selection becomes a line selection from its anchor (or from the
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
        int height = Math.Max(1, geometry.Height);
        long step = page ? height : 1;
        long target = Math.Clamp(from + direction * step, geometry.First, geometry.Last);

        long top = TopLine(geometry);
        if (page)
        {
            top = Math.Clamp(top + direction * step, geometry.First, geometry.MaxTopLine);
        }

        if (target < top)
        {
            top = target;
        }
        else if (target >= top + height)
        {
            top = target - height + 1;
        }

        _topLine = Math.Clamp(top, geometry.First, geometry.MaxTopLine);
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

    /// <summary>Wheel scrolling: moves the viewport only, not the cursor. Reaching the bottom resumes following.</summary>
    private bool Scroll(int delta)
    {
        if (_buffer is null)
        {
            return false;
        }

        Geometry geometry = Measure(_buffer);
        long target = Math.Clamp(TopLine(geometry) + delta, geometry.First, geometry.MaxTopLine);
        Pause(geometry);
        _topLine = target;
        _following = delta > 0 && target >= geometry.MaxTopLine;
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

        Pause(geometry);
        _topLine = geometry.First;
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

    private bool PanHorizontal(int delta)
    {
        _hOffset = Math.Max(0, _hOffset + delta);
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

    /// <summary>The character position under a viewport-relative cell. The line is clamped to the retained lines.</summary>
    private SelectionPoint PointAt(Geometry geometry, int row, int column)
    {
        long line = Math.Clamp(TopLine(geometry) + row, geometry.First, geometry.Last);
        return new SelectionPoint(line, _hOffset + Math.Max(0, column));
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
    /// below the view scrolls by one line per motion event.
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

    private static int CharacterCount(string text) => text.EnumerateRunes().Count();

    /// <summary>Draws one line from column 0 of the viewport, honouring the horizontal offset, and pads the rest of the row.</summary>
    private void DrawLine(OutputLine line, long number, int row, int width, Attribute normal, Color? cursorShade, SelectionRange? selection)
    {
        int drawn = 0;
        int hidden = _hOffset;
        int column = 0;
        bool full = false;
        var run = new StringBuilder();
        Attribute? runAttr = null;
        int runStart = 0;

        void Flush()
        {
            if (runAttr is { } attr && run.Length > 0)
            {
                Move(runStart, row);
                SetAttribute(attr);
                AddStr(run.ToString());
            }

            run.Clear();
        }

        foreach (StyledSpan span in line.Spans)
        {
            if (full)
            {
                break;
            }

            Attribute spanAttr = ColorMap.ToAttribute(span.Style, line.Source, normal);
            foreach (Rune original in span.Text.EnumerateRunes())
            {
                Rune rune = Rune.IsControl(original) ? new Rune(' ') : original;
                int cols = Math.Max(0, rune.GetColumns());
                if (cols == 0)
                {
                    continue;
                }

                int cellColumn = column;
                column += cols;
                if (hidden >= cols)
                {
                    hidden -= cols;
                    continue;
                }

                int visible = cols - hidden;
                hidden = 0;
                if (drawn + visible > width)
                {
                    full = true;
                    break;
                }

                Attribute attr = spanAttr;
                if (cursorShade is { } shade)
                {
                    attr = Shade(attr, normal, shade);
                }

                if (selection is { } range && range.Covers(number, cellColumn, cols))
                {
                    attr = Invert(attr);
                }

                if (runAttr is null || !runAttr.Value.Equals(attr))
                {
                    Flush();
                    runAttr = attr;
                    runStart = drawn;
                }

                run.Append(visible == cols ? rune.ToString() : new string(' ', visible));
                drawn += visible;
            }
        }

        Flush();
        FillRow(row, drawn, width, RowFill(normal, cursorShade));
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
