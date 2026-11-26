using System.Collections.Immutable;
using Mdedit.Core.Render;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Cell = Mdedit.Core.Render.Cell;
using Point = System.Drawing.Point;

namespace Mdedit.Tui;

/// <summary>
/// The rendered document in a view: the display lines of its layout drawn from a top line, with the cursor on a mapped cell. The
/// cursor moves over mapped cells only (see <see cref="HandleKey"/>), keeps its source offset across a width change, and is shown as
/// the terminal cursor. The wheel scrolls without moving the cursor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cursor.</b> The cursor is a line, the index of a cell in that line, the cell's screen column and its source offset. Up and
/// Down keep a goal column, which only the horizontal moves, Home, End and a click change.
/// </para>
/// <para>
/// <b>Scrolling.</b> Keys keep the cursor on screen by the least scrolling. The wheel scrolls <see cref="WheelLines"/> lines.
/// </para>
/// <para>
/// <b>Reflow.</b> On a width change the layout is rebuilt. The anchor is the cursor's offset when the cursor is on screen, and the
/// first mapped cell of the top line otherwise. The anchor keeps its screen row (the cursor's row, or the top row), so the cursor's
/// offset is the same after the change and its line stays where it was on screen.
/// </para>
/// </remarks>
internal sealed class RenderedView : View
{
    /// <summary>The display lines the wheel scrolls per event.</summary>
    internal const int WheelLines = 3;

    private readonly RenderDocument _document;
    private readonly LayoutCache _cache = new();
    private DocumentLayout? _layout;
    private int _top;
    private int _line;
    private int _cell = -1;
    private int _column;
    private int _offset;
    private int _goal;

    /// <summary>Creates the view over a document. It lays out at the first width it is given.</summary>
    /// <param name="document">The document to show.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <c>null</c>.</exception>
    public RenderedView(RenderDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        CanFocus = true;
    }

    /// <summary>Raised after the cursor's source offset or the top line changes, so that the message line can follow.</summary>
    public event Action? PositionChanged;

    /// <summary>Raised when a key reaches the view, before it is handled, so that the window can clear a transient message.</summary>
    public event Action? KeyPressed;

    /// <summary>The document shown.</summary>
    public RenderDocument Document => _document;

    /// <summary>
    /// True when the document has a mapped cell to rest the cursor on. A document with none (empty, or only rules) has no cursor.
    /// </summary>
    public bool HasCursor => _cell >= 0;

    /// <summary>The absolute source offset of the cursor's cell, or 0 when there is no cursor.</summary>
    public int CursorOffset => _offset;

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
    /// ends), PgUp and PgDn, and g and G. Other keys are not handled, so the window sees them.
    /// </summary>
    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        KeyPressed?.Invoke();
        return HandleKey(key) || base.OnKeyDown(key);
    }

    /// <inheritdoc/>
    protected override bool OnMouseEvent(Mouse mouse) => HandleMouse(mouse) || base.OnMouseEvent(mouse);

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
        for (; y < height && _top + y < _layout.LineCount; y++)
        {
            DrawLine(y, _top + y, width, normal);
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

        // A move can scroll, and later the selection and highlights follow the cursor, so the content is drawn again.
        SetNeedsDraw();
        return true;
    }

    /// <summary>Applies a key to the view state. Returns true when it was handled.</summary>
    private bool ApplyKey(Key key)
    {
        if (!SyncLayout() || !HasCursor)
        {
            return false;
        }

        if (key == Key.CursorLeft)
        {
            MoveHorizontal(-1);
        }
        else if (key == Key.CursorRight)
        {
            MoveHorizontal(1);
        }
        else if (key == Key.CursorUp)
        {
            MoveVertical(-1);
        }
        else if (key == Key.CursorDown)
        {
            MoveVertical(1);
        }
        else if (key == Key.Home.WithCtrl || IsPlain(key, 'g'))
        {
            MoveTo(FirstMapped());
        }
        else if (key == Key.End.WithCtrl || IsPlain(key, 'G'))
        {
            MoveTo(LastMapped());
        }
        else if (key == Key.Home)
        {
            MoveToLineEnd(first: true);
        }
        else if (key == Key.End)
        {
            MoveToLineEnd(first: false);
        }
        else if (key == Key.PageUp)
        {
            MovePage(-1);
        }
        else if (key == Key.PageDown)
        {
            MovePage(1);
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles a mouse event: the wheel scrolls without moving the cursor, and a left press moves the cursor to the cell under the mouse
    /// (on a cell with no source text, to the next mapped cell). Returns false for any other event.
    /// </summary>
    /// <param name="mouse">The event.</param>
    internal bool HandleMouse(Mouse mouse)
    {
        if (!ApplyMouse(mouse))
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

        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) || !HasCursor)
        {
            return false;
        }

        int x = mouse.Position?.X ?? 0;
        int y = mouse.Position?.Y ?? 0;
        if (y < 0 || y >= ViewportHeight || _top + y >= LineCount)
        {
            return false;
        }

        SetFocus();
        int line = _top + y;
        int before = _offset;
        int topBefore = _top;
        int offset = _layout!.OffsetAt(line, Math.Max(0, x));
        PlaceAt(offset);
        _goal = _column;
        Settle(before, topBefore);
        return true;
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
            _layout = DocumentLayout.Build(_document, width, _cache);
            _top = 0;
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
        bool hadCursor = HasCursor;

        _layout = DocumentLayout.Build(_document, width, _cache);
        if (hadCursor)
        {
            (int anchorLine, _) = _layout.PositionOf(anchor);
            _top = Math.Clamp(anchorLine - row, 0, MaxTop());
            PlaceAt(_offset);
            _goal = _column;
        }
        else
        {
            _top = Math.Clamp(_top, 0, MaxTop());
        }

        UpdateCursor();
        PositionChanged?.Invoke();
    }

    private DocumentLayout Built => _layout ?? throw new InvalidOperationException("The view has no layout yet.");

    private int ViewportHeight => Math.Max(1, Viewport.Height);

    private int MaxTop() => Math.Max(0, LineCount - ViewportHeight);

    private void ClampTop() => _top = Math.Clamp(_top, 0, MaxTop());

    private bool IsCursorVisible() => HasCursor && _line >= _top && _line < _top + ViewportHeight;

    /// <summary>Scrolls by the least amount that keeps the cursor's line in view.</summary>
    private void EnsureVisible()
    {
        if (!HasCursor)
        {
            return;
        }

        int height = ViewportHeight;
        if (_line < _top)
        {
            _top = _line;
        }
        else if (_line >= _top + height)
        {
            _top = _line - height + 1;
        }

        ClampTop();
    }

    // ----- Cursor placement -----

    /// <summary>Sets the cursor to a mapped cell of a line. The goal column follows the cell unless <paramref name="keepGoal"/> is set.</summary>
    private void PlaceCell(int line, int cell, bool keepGoal)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        _line = line;
        _cell = cell;
        _column = ColumnsOf(line)[cell];
        _offset = Built.BaseOffset(line) + cells[cell].Offset;
        if (!keepGoal)
        {
            _goal = _column;
        }
    }

    /// <summary>
    /// Sets the cursor to the cell of a source offset, as the layout's source map places it (the next mapped cell when the offset is not
    /// mapped). A line with no mapped cell is replaced by the last mapped cell before it.
    /// </summary>
    private void PlaceAt(int offset)
    {
        (int line, int column) = Built.PositionOf(offset);
        int cell = CellFor(line, offset, column);
        if (cell < 0)
        {
            (line, cell) = LastMappedAtOrBefore(line) ?? (0, -1);
            if (cell < 0)
            {
                _cell = -1;
                _offset = 0;
                return;
            }
        }

        PlaceCell(line, cell, keepGoal: false);
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

    /// <summary>The mapped cell after (line, cell) in document order, or null when there is none.</summary>
    private (int Line, int Cell)? NextMapped(int line, int cell)
    {
        for (int l = line; l < LineCount; l++)
        {
            ImmutableArray<Cell> cells = Built.Line(l).Cells;
            for (int c = l == line ? cell + 1 : 0; c < cells.Length; c++)
            {
                if (cells[c].IsMapped)
                {
                    return (l, c);
                }
            }
        }

        return null;
    }

    /// <summary>The mapped cell before (line, cell) in document order, or null when there is none.</summary>
    private (int Line, int Cell)? PrevMapped(int line, int cell)
    {
        for (int l = Math.Min(line, LineCount - 1); l >= 0; l--)
        {
            ImmutableArray<Cell> cells = Built.Line(l).Cells;
            int start = l == line ? Math.Min(cell - 1, cells.Length - 1) : cells.Length - 1;
            for (int c = start; c >= 0; c--)
            {
                if (cells[c].IsMapped)
                {
                    return (l, c);
                }
            }
        }

        return null;
    }

    /// <summary>The first mapped cell of the document, or null.</summary>
    private (int Line, int Cell)? FirstMapped() => NextMapped(0, -1);

    /// <summary>The last mapped cell of the document, or null.</summary>
    private (int Line, int Cell)? LastMapped() =>
        LineCount == 0 ? null : PrevMapped(LineCount - 1, Built.Line(LineCount - 1).Cells.Length);

    /// <summary>The last mapped cell of the line at or before <paramref name="line"/>, or null.</summary>
    private (int Line, int Cell)? LastMappedAtOrBefore(int line) => PrevMapped(line, int.MaxValue / 2);

    /// <summary>The index of the first or the last mapped cell of a line, or -1 when it has none.</summary>
    private int EdgeMappedCell(int line, bool first)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
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
        (int Line, int Cell)? target = direction > 0 ? NextMapped(_line, _cell) : PrevMapped(_line, _cell);
        if (target is { } cell)
        {
            PlaceCell(cell.Line, cell.Cell, keepGoal: false);
        }

        Settle(before, topBefore);
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
        EnsureVisible();
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
            ? new Cursor { Position = ViewportToScreen(new Point(_column, _line - _top)), Style = CursorStyle.BlinkingBar }
            : new Cursor { Position = null, Style = CursorStyle.BlinkingBar };
    }

    private void DrawLine(int y, int line, int width, Attribute normal)
    {
        DisplayLine display = Built.Line(line);
        string? alert = null;
        Move(0, y);
        int x = 0;
        foreach (Cell cell in display.Cells)
        {
            if (x + cell.Width > width)
            {
                break;
            }

            if (cell.Width == 0)
            {
                continue;
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

            SetAttribute(attribute);
            AddStr(cell.Text);
            x += cell.Width;
        }

        if (x < width)
        {
            SetAttribute(normal);
            AddStr(new string(' ', width - x));
        }
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

    private static bool IsPlain(Key key, char ch) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value == ch;
}
