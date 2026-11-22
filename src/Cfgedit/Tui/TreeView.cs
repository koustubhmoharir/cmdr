using Cfgedit.Core.Model;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cfgedit.Tui;

/// <summary>
/// The document's tree: its visible rows, drawn from a top display line, with the cursor on one part of one row. The cursor's row is
/// drawn with a tint, and its focused part in reverse video. Rows are laid out lazily, once per row and width; the layouts and the
/// display line counts are dropped when the document, the collapsed set or the width changes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cursor.</b> Moving between rows keeps the part index where the new row has one, otherwise the last part. The preferred part
/// is the part the user last moved to horizontally (or by Home, End or a click). Vertical moves never change it, so a run of
/// shorter rows does not lose the column.
/// </para>
/// <para>
/// <b>Scrolling.</b> Keys keep the cursor row entirely in view. The wheel scrolls by display lines and moves the cursor only when the
/// cursor row leaves the view.
/// </para>
/// </remarks>
internal sealed class TreeView : View
{
    /// <summary>The display lines the wheel scrolls per event.</summary>
    internal const int WheelLines = 3;

    /// <summary>A part of a row: the row's node, whether the row is the end row, and the part's index in the row.</summary>
    internal sealed record PartPosition(NodePath Path, bool IsEnd, int Part);

    /// <summary>The columns of a fold glyph and the space after it.</summary>
    private const int FoldColumns = 2;

    private ConfigDocument? _document;
    private HashSet<NodePath> _collapsed = [];
    private IReadOnlyList<Row> _rows = [];
    private readonly Dictionary<(NodePath Path, bool IsEnd), int> _indexOf = [];
    private RowLines?[] _layouts = [];
    private readonly List<int> _starts = [0];
    private int _layoutWidth = -1;
    private int _row;
    private int _part;
    private int _preferred;
    private int _top;
    private Func<NodePath, int, PartMarks?>? _marksOf;

    /// <summary>Creates the view. It shows nothing until <see cref="SetDocument"/> is called.</summary>
    public TreeView()
    {
        CanFocus = true;
    }

    /// <summary>Raised after the cursor moves, or the document or the collapsed set changes, so that the detail line can follow.</summary>
    public event Action? CursorChanged;

    /// <summary>Raised when a key reaches the tree, before it is handled, so that the window can clear a transient message.</summary>
    public event Action? KeyPressed;

    /// <summary>The inline editor over the cursor's part, or null when no editor is open.</summary>
    private InlineEditor? _editor;

    /// <summary>The document shown, or null before one is set.</summary>
    public ConfigDocument? Document => _document;

    /// <summary>The node of the cursor's row, or null when there is no row.</summary>
    public DocNode? CursorNode => _rows.Count > 0 ? _rows[_row].Node : null;

    /// <summary>The focused part, or null when there is no row or the row has no part.</summary>
    public DocPart? CursorDocPart
    {
        get
        {
            if (_rows.Count == 0)
            {
                return null;
            }

            IReadOnlyList<DocPart> parts = PartsOf(_row);
            return _part < parts.Count ? parts[_part] : null;
        }
    }

    /// <summary>True when the cursor is on an end row.</summary>
    public bool CursorIsEnd => _rows.Count > 0 && _rows[_row].IsEnd;

    /// <summary>The index of the cursor's row among the visible rows.</summary>
    internal int CursorRow => _row;

    /// <summary>The index of the focused part among the row's parts (its end parts on an end row).</summary>
    internal int CursorPart => _part;

    /// <summary>
    /// The part index the cursor returns to when a row has that many parts. It does not count a JSON root marker, so that Up and Down keep
    /// the same column whether a row has the marker or not.
    /// </summary>
    internal int PreferredPart => _preferred;

    /// <summary>The first display line in the viewport.</summary>
    internal int TopLine => _top;

    /// <summary>The visible rows, in display order.</summary>
    internal IReadOnlyList<Row> Rows => _rows;

    /// <summary>The collapsed nodes, by path.</summary>
    internal IReadOnlySet<NodePath> Collapsed => _collapsed;

    /// <summary>True while the inline editor is open. The tree then takes no keys and no mouse events: the field does.</summary>
    internal bool IsEditing => _editor is not null;

    /// <summary>The text of the inline editor, or null when none is open.</summary>
    internal string? EditorText => _editor?.Text;

    /// <summary>
    /// Opens the inline editor over the cursor's part. The field starts at the part's first column on its display line, runs to the right
    /// edge, and takes the focus. The part's line is scrolled into view first.
    /// </summary>
    /// <param name="text">The text the field starts with.</param>
    /// <param name="onKey">Handles the keys of the field first (see <see cref="InlineEditor"/>).</param>
    /// <exception cref="InvalidOperationException">An editor is already open, or the cursor is on no part.</exception>
    internal InlineEditor OpenEditor(string text, Func<Key, bool> onKey)
    {
        ArgumentNullException.ThrowIfNull(onKey);
        if (_editor is not null)
        {
            throw new InvalidOperationException("An editor is already open.");
        }

        if (_rows.Count == 0 || _part >= LayoutOf(_row).Parts.Count)
        {
            throw new InvalidOperationException("The cursor is on no part.");
        }

        ScrollPartIntoView();
        var editor = new InlineEditor(text, onKey);
        _editor = editor;
        Add(editor);
        PositionEditor();
        editor.SetFocus();
        SetNeedsDraw();
        return editor;
    }

    /// <summary>Closes the inline editor, if one is open, and returns the focus to the tree.</summary>
    internal void CloseEditor()
    {
        if (_editor is null)
        {
            return;
        }

        InlineEditor editor = _editor;
        _editor = null;
        Remove(editor);
        editor.Dispose();
        SetFocus();
        SetNeedsDraw();
    }

    /// <summary>
    /// Moves the cursor to a part of a visible row, and makes that the preferred part. Returns false, and does nothing, when the node has no
    /// visible row of that kind.
    /// </summary>
    internal bool MoveCursorTo(NodePath path, bool isEnd, int part)
    {
        int row = IndexOf(path, isEnd);
        if (row < 0)
        {
            return false;
        }

        _row = row;
        SetPart(part);
        EnsureCursorVisible();
        SetNeedsDraw();
        Notify();
        return true;
    }

    /// <summary>
    /// The next part after the cursor (or the previous one, for a negative direction) that <paramref name="accept"/> takes, in visible-row
    /// order. Rows that are not visible are not walked. Null when no part after the cursor is taken.
    /// </summary>
    /// <param name="direction">1 for the next part, -1 for the previous.</param>
    /// <param name="accept">Takes a node and one of its parts.</param>
    internal PartPosition? FindPart(int direction, Func<DocNode, DocPart, bool> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        if (direction == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "The direction must not be zero.");
        }

        if (_rows.Count == 0)
        {
            return null;
        }

        int row = _row;
        int part = _part + Math.Sign(direction);
        while (row >= 0 && row < _rows.Count)
        {
            IReadOnlyList<DocPart> parts = PartsOf(row);
            for (; part >= 0 && part < parts.Count; part += Math.Sign(direction))
            {
                if (accept(_rows[row].Node, parts[part]))
                {
                    return new PartPosition(_rows[row].Node.Path, _rows[row].IsEnd, part);
                }
            }

            row += Math.Sign(direction);
            if (row >= 0 && row < _rows.Count)
            {
                part = direction > 0 ? 0 : PartsOf(row).Count - 1;
            }
        }

        return null;
    }

    /// <summary>
    /// Shows a document. The first document gets the initial collapsed set (see <see cref="DocRows.Initial"/>); a later one keeps the
    /// collapsed set of the document it replaces (see <see cref="DocRows.Retain"/>), so an edit keeps the folds the user made. The cursor
    /// goes to <paramref name="focus"/> (or to the nearest existing node, and to the node it was on when <paramref name="focus"/> is null),
    /// or to the node's nearest visible row when that node is inside a collapsed one.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="focus">The node to put the cursor on, or null to keep the cursor's node.</param>
    public void SetDocument(ConfigDocument document, NodePath? focus)
    {
        ArgumentNullException.ThrowIfNull(document);

        SyncWidth();
        NodePath? wanted = focus ?? CursorNode?.Path;
        IReadOnlySet<NodePath> collapsed = _document is null ? DocRows.Initial(document) : DocRows.Retain(document, _collapsed);
        _document = document;
        _collapsed = new HashSet<NodePath>(collapsed);
        Rebuild();

        int row = 0;
        if (wanted is not null)
        {
            row = VisibleRowOf(document.ResolveNearest(wanted)) ?? 0;
        }

        _row = row;
        _part = PartFor(_row);
        EnsureCursorVisible();
        SetNeedsDraw();
        Notify();
    }

    /// <summary>
    /// Sets the search highlights: the matches of a part of a node, by the node's path and the part's index, or null for none. The layouts
    /// are dropped, so the next draw shows the new highlights. The cursor does not move.
    /// </summary>
    /// <param name="marksOf">The marks of a part, or null for no highlights at all.</param>
    internal void SetHighlights(Func<NodePath, int, PartMarks?>? marksOf)
    {
        _marksOf = marksOf;
        DropLayouts();
        SetNeedsDraw();
    }

    /// <summary>
    /// Moves the cursor to a part of a node, and expands every collapsed ancestor of the node so that the row is visible. The collapsed set
    /// changes, which is not an edit. Returns false, and does nothing, when the node is not in the document.
    /// </summary>
    /// <param name="path">The node.</param>
    /// <param name="part">The part's index in the node's parts.</param>
    internal bool RevealPart(NodePath path, int part)
    {
        DocNode? node = _document?.Resolve(path);
        if (node is null)
        {
            return false;
        }

        bool expanded = false;
        for (DocNode? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            expanded |= _collapsed.Remove(ancestor.Path);
        }

        if (expanded)
        {
            Rebuild();
        }

        return MoveCursorTo(path, false, part);
    }

    /// <summary>Handles a key of the tree. Returns <c>false</c> for a key the tree does not bind.</summary>
    /// <param name="key">The key.</param>
    internal bool HandleKey(Key key)
    {
        if (_document is null || _rows.Count == 0 || IsEditing)
        {
            return false;
        }

        SyncWidth();
        if (key == Key.CursorUp || IsPlain(key, 'k'))
        {
            MoveRow(_row - 1);
        }
        else if (key == Key.CursorDown || IsPlain(key, 'j'))
        {
            MoveRow(_row + 1);
        }
        else if (key == Key.CursorLeft || IsPlain(key, 'h'))
        {
            MovePart(-1);
        }
        else if (key == Key.CursorRight || IsPlain(key, 'l'))
        {
            MovePart(1);
        }
        else if (key == Key.Home || IsPlain(key, '0'))
        {
            SetPart(0);
        }
        else if (key == Key.End || IsPlain(key, '$'))
        {
            SetPart(int.MaxValue);
        }
        else if (key == Key.PageUp)
        {
            PageMove(-1);
        }
        else if (key == Key.PageDown)
        {
            PageMove(1);
        }
        else if (IsPlain(key, 'g'))
        {
            MoveRow(0);
        }
        else if (IsPlain(key, 'G'))
        {
            MoveRow(_rows.Count - 1);
        }
        else if (key == Key.Space)
        {
            ToggleFold();
        }
        else if (IsPlain(key, '*'))
        {
            ExpandSubtree();
        }
        else if (IsPlain(key, '-'))
        {
            CollapseParent();
        }
        else
        {
            return false;
        }

        SetNeedsDraw();
        Notify();
        return true;
    }

    /// <summary>
    /// Handles a mouse event. The wheel scrolls by <see cref="WheelLines"/> display lines. A left press focuses the part under the
    /// pointer, or the row's first part when the pointer is on no part; a press on the fold glyph of a collapsible row toggles it.
    /// </summary>
    /// <param name="mouse">The mouse event.</param>
    /// <returns><c>true</c> when the event was handled.</returns>
    internal bool HandleMouse(Mouse mouse)
    {
        if (_document is null || _rows.Count == 0 || IsEditing)
        {
            return false;
        }

        SyncWidth();
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

        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            return false;
        }

        int x = mouse.Position?.X ?? 0;
        int y = mouse.Position?.Y ?? 0;
        if (y < 0 || y >= Viewport.Height || _top + y >= TotalLines)
        {
            return false;
        }

        int row = RowAt(_top + y, out int subLine);
        RowLines layout = LayoutOf(row);
        DisplayLine displayLine = layout.Lines[subLine];
        bool onFold = subLine == 0 && !_rows[row].IsEnd && DocRows.IsCollapsible(_rows[row].Node)
            && x >= displayLine.Indent && x < displayLine.Indent + FoldColumns;

        SetFocus();
        _row = row;
        if (onFold)
        {
            _part = ClampPart(_row, _part);
            ToggleFold();
        }
        else
        {
            SetPart(PartAt(layout, subLine, x) ?? 0);
            EnsureCursorVisible();
        }

        SetNeedsDraw();
        Notify();
        return true;
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        KeyPressed?.Invoke();
        return HandleKey(key) || base.OnKeyDown(key);
    }

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

        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        SyncWidth();
        if (_rows.Count == 0)
        {
            DrawBlank(0, height, width, normal);
            return true;
        }

        ClampTop();
        PositionEditor();
        Color tint = TreeColors.CursorRowBackground(normal, App?.Driver?.DefaultAttribute);
        int y = 0;
        int row = RowAt(_top, out int subLine);
        while (y < height && row < _rows.Count)
        {
            RowLines layout = LayoutOf(row);
            for (int line = subLine; line < layout.Lines.Count && y < height; line++)
            {
                DrawLine(y, row, layout.Lines[line], width, normal, tint);
                y++;
            }

            subLine = 0;
            row++;
        }

        DrawBlank(y, height, width, normal);
        return true;
    }

    // ----- Rows and layouts -----

    /// <summary>Drops the layouts when the width changed, since a layout depends on it.</summary>
    private void SyncWidth()
    {
        int width = Math.Max(0, Viewport.Width);
        if (width == _layoutWidth)
        {
            return;
        }

        _layoutWidth = width;
        DropLayouts();
    }

    /// <summary>Forgets every row's layout and the display lines they gave, so that they are laid out again on the next draw.</summary>
    private void DropLayouts()
    {
        Array.Clear(_layouts);
        _starts.Clear();
        _starts.Add(0);
    }

    /// <summary>Flattens the document into rows, and drops everything derived from the old rows.</summary>
    private void Rebuild()
    {
        _rows = _document is null ? [] : DocRows.Visible(_document, _collapsed);
        _indexOf.Clear();
        for (int i = 0; i < _rows.Count; i++)
        {
            _indexOf[(_rows[i].Node.Path, _rows[i].IsEnd)] = i;
        }

        _layouts = new RowLines?[_rows.Count];
        _starts.Clear();
        _starts.Add(0);
    }

    /// <summary>The layout of a row at the view's width. It is computed once, then kept until the width or the rows change.</summary>
    private RowLines LayoutOf(int row)
    {
        return _layouts[row] ??= RowLayout.Layout(
            _rows[row],
            _collapsed.Contains(_rows[row].Node.Path),
            Math.Max(0, Viewport.Width),
            MarksFor(row));
    }

    /// <summary>The highlights of a row's parts, or null when the row has none (no search, or an end row, whose parts are not searched).</summary>
    private Func<int, PartMarks?>? MarksFor(int row)
    {
        if (_marksOf is null || _rows[row].IsEnd)
        {
            return null;
        }

        NodePath path = _rows[row].Node.Path;
        Func<NodePath, int, PartMarks?> marksOf = _marksOf;
        return part => marksOf(path, part);
    }

    /// <summary>Makes the display line of each row, up to and including <paramref name="upTo"/>, known.</summary>
    private void EnsureStarts(int upTo)
    {
        while (_starts.Count <= upTo)
        {
            int i = _starts.Count - 1;
            _starts.Add(_starts[i] + LayoutOf(i).Lines.Count);
        }
    }

    /// <summary>The first display line of a row.</summary>
    private int LineStartOf(int row)
    {
        EnsureStarts(row);
        return _starts[row];
    }

    /// <summary>The number of display lines of all the rows.</summary>
    private int TotalLines
    {
        get
        {
            if (_rows.Count == 0)
            {
                return 0;
            }

            EnsureStarts(_rows.Count);
            return _starts[_rows.Count];
        }
    }

    /// <summary>The row that holds a display line, and the line's index within the row. The line must be below <see cref="TotalLines"/>.</summary>
    private int RowAt(int line, out int subLine)
    {
        while (_starts.Count <= _rows.Count && _starts[^1] <= line)
        {
            EnsureStarts(_starts.Count);
        }

        int low = 0;
        int high = _starts.Count - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (_starts[mid] <= line)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        subLine = line - _starts[low];
        return low;
    }

    private int IndexOf(NodePath path, bool isEnd) => _indexOf.TryGetValue((path, isEnd), out int row) ? row : -1;

    /// <summary>The row of a node, or of its nearest ancestor that has one. Null when none is visible.</summary>
    private int? VisibleRowOf(DocNode node)
    {
        for (DocNode? current = node; current?.Parent is not null; current = current.Parent)
        {
            int row = IndexOf(current.Path, false);
            if (row >= 0)
            {
                return row;
            }
        }

        return null;
    }

    private IReadOnlyList<DocPart> PartsOf(int row) => _rows[row].IsEnd ? _rows[row].Node.EndParts : _rows[row].Node.Parts;

    private int ClampPart(int row, int part)
    {
        int count = PartsOf(row).Count;
        return count == 0 ? 0 : Math.Clamp(part, 0, count - 1);
    }

    private int ViewportHeight => Math.Max(1, Viewport.Height);

    private int MaxTop => Math.Max(0, TotalLines - ViewportHeight);

    private void ClampTop() => _top = Math.Clamp(_top, 0, MaxTop);

    /// <summary>Scrolls by display lines so that the cursor's row is entirely in view. A row taller than the view shows from its first line.</summary>
    private void EnsureCursorVisible()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        int start = LineStartOf(_row);
        int count = LayoutOf(_row).Lines.Count;
        int height = ViewportHeight;
        if (start < _top)
        {
            _top = start;
        }
        else if (start + count > _top + height)
        {
            _top = count >= height ? start : start + count - height;
        }

        ClampTop();
    }

    /// <summary>Scrolls so that the display line of the cursor's part is in view. Used before the inline editor opens over it.</summary>
    private void ScrollPartIntoView()
    {
        int line = LineStartOf(_row) + LayoutOf(_row).Parts[_part].Line;
        if (line < _top)
        {
            _top = line;
        }
        else if (line >= _top + ViewportHeight)
        {
            _top = line - ViewportHeight + 1;
        }

        ClampTop();
    }

    /// <summary>
    /// Places the inline editor over the cursor's part: on the part's display line, from the part's first column. A part that the width cuts
    /// to nothing starts at the last column. Does nothing when no editor is open.
    /// </summary>
    private void PositionEditor()
    {
        if (_editor is null || _rows.Count == 0 || _part >= LayoutOf(_row).Parts.Count)
        {
            return;
        }

        PartRange range = LayoutOf(_row).Parts[_part];
        int x = Math.Clamp(range.Start, 0, Math.Max(0, Viewport.Width - 1));
        int y = LineStartOf(_row) + range.Line - _top;
        _editor.X = x;
        _editor.Y = y;
    }

    private void Notify() => CursorChanged?.Invoke();

    // ----- Cursor moves -----

    /// <summary>Moves to a row, keeping the preferred part where the row has it, else its last part.</summary>
    private void MoveRow(int target)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        int row = Math.Clamp(target, 0, _rows.Count - 1);
        if (row == _row)
        {
            return;
        }

        _row = row;
        _part = PartFor(_row);
        EnsureCursorVisible();
    }

    /// <summary>Moves to the previous or next part. Left on the first part goes to the parent row (for an end row, its element's row).</summary>
    private void MovePart(int direction)
    {
        int next = _part + direction;
        if (next < 0)
        {
            GoParent();
            return;
        }

        if (next >= PartsOf(_row).Count)
        {
            return;
        }

        SetPart(next);
    }

    private void GoParent()
    {
        DocNode node = _rows[_row].Node;
        int row = _rows[_row].IsEnd ? IndexOf(node.Path, false) : node.Parent is { } parent ? IndexOf(parent.Path, false) : -1;
        if (row < 0)
        {
            return;
        }

        _row = row;
        _part = PartFor(_row);
        EnsureCursorVisible();
    }

    /// <summary>Puts the cursor on a part of its row, and records it as the preferred part.</summary>
    private void SetPart(int part)
    {
        _part = ClampPart(_row, part);
        _preferred = PreferredFor(_row, _part);
    }

    /// <summary>
    /// True when a row starts with the JSON root marker (<see cref="PartKind.Root"/>). The marker is not a column for up and down movement, so
    /// the preferred part skips it: see <see cref="PreferredFor"/> and <see cref="PartFor"/>.
    /// </summary>
    private bool HasRootMarker(int row)
    {
        IReadOnlyList<DocPart> parts = PartsOf(row);
        return parts.Count > 0 && parts[0].Kind == PartKind.Root;
    }

    /// <summary>The preferred part to record for a part of a row: on a row with the root marker, the part counted from after the marker.</summary>
    private int PreferredFor(int row, int part) => HasRootMarker(row) ? Math.Max(0, part - 1) : part;

    /// <summary>The part the cursor takes on a row for the preferred part: on a row with the root marker, the one after the marker.</summary>
    private int PartFor(int row) => ClampPart(row, HasRootMarker(row) ? _preferred + 1 : _preferred);

    /// <summary>
    /// Moves by a viewport of display lines, keeping the cursor's screen position where it can. At the end of the document the cursor
    /// goes to the last row (or the first, going up), as the timeline does.
    /// </summary>
    private void PageMove(int direction)
    {
        int height = ViewportHeight;
        int total = TotalLines;
        int newTop = direction > 0 ? Math.Min(MaxTop, _top + height) : Math.Max(0, _top - height);
        int target;
        if (newTop == _top)
        {
            target = direction > 0 ? total - 1 : 0;
        }
        else
        {
            int offset = Math.Clamp(LineStartOf(_row) - _top, 0, height - 1);
            target = Math.Clamp(newTop + offset, 0, total - 1);
        }

        int row = RowAt(target, out _);
        _top = newTop;
        _row = row;
        _part = PartFor(_row);
        EnsureCursorVisible();
    }

    /// <summary>
    /// Space. A collapsible node toggles. An end row collapses its element and the cursor moves to the element's row. A leaf folds its
    /// parent, and the cursor moves there.
    /// </summary>
    private void ToggleFold()
    {
        DocNode node = _rows[_row].Node;
        if (!DocRows.IsCollapsible(node))
        {
            if (node.Parent is { } parent && DocRows.IsCollapsible(parent))
            {
                SetFoldAndMove(parent.Path, collapse: true, moveTo: parent.Path);
            }

            return;
        }

        if (_rows[_row].IsEnd)
        {
            SetFoldAndMove(node.Path, collapse: true, moveTo: node.Path);
            return;
        }

        SetFoldAndMove(node.Path, collapse: !_collapsed.Contains(node.Path), moveTo: null);
    }

    /// <summary>Sets a node's collapsed state, and moves the cursor to the start row of <paramref name="moveTo"/> or keeps it on the cursor's node.</summary>
    private void SetFoldAndMove(NodePath path, bool collapse, NodePath? moveTo)
    {
        if (collapse)
        {
            _collapsed.Add(path);
        }
        else
        {
            _collapsed.Remove(path);
        }

        NodePath cursor = moveTo ?? CursorNode!.Path;
        Rebuild();
        int row = IndexOf(cursor, false);
        _row = Math.Max(0, row);
        _part = moveTo is null ? ClampPart(_row, _part) : PartFor(_row);
        EnsureCursorVisible();
    }

    /// <summary>* expands the cursor's node and every node under it.</summary>
    private void ExpandSubtree()
    {
        NodePath root = CursorNode!.Path;
        bool isEnd = CursorIsEnd;
        if (_collapsed.RemoveWhere(path => IsWithin(path, root)) == 0)
        {
            return;
        }

        Rebuild();
        _row = Math.Max(0, IndexOf(root, isEnd));
        _part = ClampPart(_row, _part);
        EnsureCursorVisible();
    }

    /// <summary>- collapses the cursor's parent, and the cursor moves to it.</summary>
    private void CollapseParent()
    {
        DocNode? parent = CursorNode!.Parent;
        if (parent is null || !DocRows.IsCollapsible(parent))
        {
            return;
        }

        SetFoldAndMove(parent.Path, collapse: true, moveTo: parent.Path);
    }

    private static bool IsWithin(NodePath path, NodePath root) => path.Count >= root.Count && path.Prefix(root.Count).Equals(root);

    /// <summary>Scrolls by display lines. The cursor moves only when its row leaves the view, to the first or the last row in view.</summary>
    private void Wheel(int lines)
    {
        int height = ViewportHeight;
        _top += lines;
        ClampTop();
        int start = LineStartOf(_row);
        int count = LayoutOf(_row).Lines.Count;
        if (start + count <= _top)
        {
            _row = RowAt(_top, out _);
            _part = PartFor(_row);
        }
        else if (start >= _top + height)
        {
            _row = RowAt(Math.Min(TotalLines - 1, _top + height - 1), out _);
            _part = PartFor(_row);
        }

        SetNeedsDraw();
        Notify();
    }

    /// <summary>The index of the part under a column of a display line, or null when the column is on no part.</summary>
    private static int? PartAt(RowLines layout, int subLine, int x)
    {
        for (int p = 0; p < layout.Parts.Count; p++)
        {
            PartRange range = layout.Parts[p];
            if (range.Line == subLine && x >= range.Start && x < range.End)
            {
                return p;
            }
        }

        return null;
    }

    private static bool IsPlain(Key key, char ch) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value == ch;

    // ----- Drawing -----

    private void DrawLine(int y, int row, DisplayLine line, int width, Attribute normal, Color tint)
    {
        bool cursorRow = row == _row;
        Color background = cursorRow ? tint : normal.Background;
        var plain = new Attribute(normal.Foreground, background, TextStyle.None);
        Move(0, y);
        SetAttribute(plain);

        int x = Math.Min(line.Indent, width);
        AddStr(new string(' ', x));
        foreach (Segment segment in line.Segments)
        {
            if (x >= width)
            {
                break;
            }

            Attribute attribute = TreeColors.For(segment.Style, normal, background);
            attribute = segment.Highlight switch
            {
                CellHighlight.Match => SearchColors.Match(attribute),
                CellHighlight.Active => SearchColors.Active(attribute),
                _ => attribute,
            };
            if (cursorRow && segment.PartIndex == _part)
            {
                attribute = TreeColors.Focused(attribute);
            }

            SetAttribute(attribute);
            AddStr(segment.Text);
            x += TextWidth.Columns(segment.Text);
        }

        if (x < width)
        {
            SetAttribute(plain);
            AddStr(new string(' ', width - x));
        }
    }

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
