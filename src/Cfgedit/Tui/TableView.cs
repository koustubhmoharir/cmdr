using System.Globalization;
using Cfgedit.Core.Model;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cfgedit.Tui;

/// <summary>
/// The table editor's view: a header row with the column names, then the rows of a <see cref="TableModel"/>. The row-index column is at
/// the left and stays in place, and the other columns scroll as a group, by whole columns. The cursor is one cell, or the index column of
/// a row (column -1). The cursor's row is tinted, and its cell is drawn in reverse video. An open inline editor sits over the cell.
/// </summary>
/// <remarks>
/// <b>Cursor.</b> The cursor keys, hjkl, Home and End, PgUp and PgDn, and g and G move the cursor and keep it in view. The window's keys
/// (editing, rows, undo, save) are not handled here: <see cref="HandleKey"/> returns false for them.
/// </remarks>
internal sealed class TableView : View
{
    /// <summary>The widest a column is drawn, in columns. A wider value is cut with an ellipsis.</summary>
    internal const int MaxColumnWidth = 30;

    /// <summary>The columns between the index column and the first column, and between two columns.</summary>
    internal const int Gap = 2;

    private TableModel? _model;
    private int[] _widths = [];
    private int _indexWidth = 1;
    private int _row;
    private int _col;
    private int _top;
    private int _left;
    private InlineEditor? _editor;

    /// <summary>Creates the view. It shows nothing until <see cref="SetTable"/> is called.</summary>
    public TableView()
    {
        CanFocus = true;
    }

    /// <summary>Raised after the cursor moves or the table is set, so that the detail line can follow.</summary>
    public event Action? CursorChanged;

    /// <summary>The index of the cursor's row.</summary>
    internal int CursorRow => _row;

    /// <summary>The index of the cursor's column, or -1 for the row-index column.</summary>
    internal int CursorColumn => _col;

    /// <summary>True while the inline editor is open. The table then takes no cursor keys: the field does.</summary>
    internal bool IsEditing => _editor is not null;

    /// <summary>The text of the inline editor, or null when none is open.</summary>
    internal string? EditorText => _editor?.Text;

    /// <summary>
    /// Shows a table with the cursor at a row and a column. An open editor is closed. The widths are measured from the table, and the
    /// cursor is clamped to it and scrolled into view.
    /// </summary>
    /// <param name="model">The table.</param>
    /// <param name="row">The row to put the cursor on.</param>
    /// <param name="column">The column to put the cursor on, or -1 for the index column.</param>
    internal void SetTable(TableModel model, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(model);
        CloseEditor();
        _model = model;
        Measure(model);
        Place(row, column);
    }

    /// <summary>Moves the cursor to a row and a column, clamped to the table, and scrolls it into view.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column, or -1 for the index column.</param>
    internal void Place(int row, int column)
    {
        if (_model is null)
        {
            return;
        }

        _row = Math.Clamp(row, 0, _model.Rows.Count - 1);
        _col = Math.Clamp(column, -1, _model.Columns.Count - 1);
        EnsureVisible();
        PositionEditor();
        SetNeedsDraw();
        Notify();
    }

    /// <summary>
    /// Opens the inline editor over the cursor's cell. The field starts at the cell's first column, is as wide as its text (see
    /// <see cref="InlineEditor"/>), and takes the focus. The cell is scrolled into view first.
    /// </summary>
    /// <param name="text">The text the field starts with.</param>
    /// <param name="onKey">Handles the keys of the field first (see <see cref="InlineEditor"/>).</param>
    /// <exception cref="InvalidOperationException">An editor is already open, or the cursor is on no cell.</exception>
    internal InlineEditor OpenEditor(string text, Func<Key, bool> onKey)
    {
        ArgumentNullException.ThrowIfNull(onKey);
        if (_editor is not null)
        {
            throw new InvalidOperationException("An editor is already open.");
        }

        if (_model is null || _col < 0)
        {
            throw new InvalidOperationException("The cursor is on no cell.");
        }

        EnsureVisible();
        var editor = new InlineEditor(text, onKey);
        _editor = editor;
        Add(editor);
        PositionEditor();
        editor.SetFocus();
        SetNeedsDraw();
        return editor;
    }

    /// <summary>Closes the inline editor, if one is open, and returns the focus to the table.</summary>
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
    /// Handles a cursor key: the arrows and hjkl, Home and End (the index column and the last column), PgUp and PgDn, and g and G. Returns
    /// false for any other key, and for every key while the editor is open.
    /// </summary>
    /// <param name="key">The key.</param>
    internal bool HandleKey(Key key)
    {
        if (_model is null || IsEditing)
        {
            return false;
        }

        int row = _row;
        int column = _col;
        if (key == Key.CursorUp || IsPlain(key, 'k'))
        {
            row = _row - 1;
        }
        else if (key == Key.CursorDown || IsPlain(key, 'j'))
        {
            row = _row + 1;
        }
        else if (key == Key.CursorLeft || IsPlain(key, 'h'))
        {
            column = _col - 1;
        }
        else if (key == Key.CursorRight || IsPlain(key, 'l'))
        {
            column = _col + 1;
        }
        else if (key == Key.Home || IsPlain(key, '0'))
        {
            column = -1;
        }
        else if (key == Key.End || IsPlain(key, '$'))
        {
            column = _model.Columns.Count - 1;
        }
        else if (key == Key.PageUp)
        {
            row = _row - DataHeight;
        }
        else if (key == Key.PageDown)
        {
            row = _row + DataHeight;
        }
        else if (IsPlain(key, 'g'))
        {
            row = 0;
        }
        else if (IsPlain(key, 'G'))
        {
            row = _model.Rows.Count - 1;
        }
        else
        {
            return false;
        }

        Place(row, column);
        return true;
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key) => HandleKey(key) || base.OnKeyDown(key);

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
        if (_model is null)
        {
            DrawBlank(0, height, width, normal);
            return true;
        }

        EnsureVisible();
        PositionEditor();
        Color tint = TreeColors.CursorRowBackground(normal, App?.Driver?.DefaultAttribute);
        DrawHeader(width, normal);
        int y = 1;
        for (int row = _top; row < _model.Rows.Count && y < height; row++, y++)
        {
            DrawRow(y, row, width, normal, tint);
        }

        DrawBlank(y, height, width, normal);
        return true;
    }

    // ----- Measure and scroll -----

    /// <summary>Sets the index column's width and each column's width: the widest of the header and the cells, capped at <see cref="MaxColumnWidth"/>.</summary>
    private void Measure(TableModel model)
    {
        int largest = 0;
        for (int row = 0; row < model.Rows.Count; row++)
        {
            largest = Math.Max(largest, LabelOf(model, row));
        }

        _indexWidth = Math.Max(1, largest.ToString(CultureInfo.InvariantCulture).Length);
        _widths = new int[model.Columns.Count];
        for (int column = 0; column < _widths.Length; column++)
        {
            int width = ColumnsOf(model.Columns[column].Name);
            for (int row = 0; row < model.Rows.Count; row++)
            {
                width = Math.Max(width, ColumnsOf(ShownOf(model.Cell(row, column)).Text));
            }

            _widths[column] = Math.Clamp(width, 1, MaxColumnWidth);
        }
    }

    /// <summary>The display width of a text as a cell shows it: its glyphs, uncut.</summary>
    private static int ColumnsOf(string text) => TextWidth.Columns(RowLayout.CellText(text, int.MaxValue));

    /// <summary>
    /// The number a row is labelled with. It is the row's item index in a JSON array, and the element's 1-based position among its
    /// same-named siblings in XML, as the row's path names it.
    /// </summary>
    private static int LabelOf(TableModel model, int row) => model.Rows[row].Steps[^1] switch
    {
        ItemStep item => item.Index,
        ElementStep element => element.Ordinal + 1,
        _ => row + 1,
    };

    /// <summary>The width of the index column and the gap after it: where the first column starts.</summary>
    private int BodyLeft => _indexWidth + Gap;

    /// <summary>The number of data rows in the view: its height less the header row.</summary>
    private int DataHeight => Math.Max(1, Viewport.Height - 1);

    /// <summary>The columns from <paramref name="from"/> to <paramref name="to"/> inclusive, with their gaps between them.</summary>
    private int Span(int from, int to)
    {
        int total = Gap * (to - from);
        for (int column = from; column <= to; column++)
        {
            total += _widths[column];
        }

        return total;
    }

    /// <summary>The x position of a column that is scrolled into view, from the left edge of the view.</summary>
    private int ColumnX(int column) => BodyLeft + (column > _left ? Span(_left, column - 1) + Gap : 0);

    /// <summary>
    /// Scrolls so that the cursor's row is in view, and the cursor's column is entirely in view when it fits the width. A column that is wider
    /// than the view shows from its first cell.
    /// </summary>
    private void EnsureVisible()
    {
        if (_model is null)
        {
            return;
        }

        int height = DataHeight;
        int maxTop = Math.Max(0, _model.Rows.Count - height);
        if (_row < _top)
        {
            _top = _row;
        }
        else if (_row >= _top + height)
        {
            _top = _row - height + 1;
        }

        _top = Math.Clamp(_top, 0, maxTop);

        _left = Math.Clamp(_left, 0, Math.Max(0, _widths.Length - 1));
        if (_col < 0)
        {
            return;
        }

        if (_col < _left)
        {
            _left = _col;
        }

        int available = Math.Max(0, Viewport.Width - BodyLeft);
        while (_left < _col && Span(_left, _col) > available)
        {
            _left++;
        }
    }

    /// <summary>
    /// Places the inline editor over the cursor's cell, from the cell's column. Setting X lays the field out again, so its width follows a
    /// horizontal scroll. Does nothing when no editor is open.
    /// </summary>
    private void PositionEditor()
    {
        if (_editor is null || _model is null || _col < 0)
        {
            return;
        }

        _editor.X = Math.Min(ColumnX(_col), Math.Max(0, Viewport.Width - 1));
        _editor.Y = 1 + _row - _top;
    }

    private void Notify() => CursorChanged?.Invoke();

    // ----- Cells -----

    /// <summary>
    /// The text a cell shows, before it is cut to its column, and the style it is drawn in. A missing cell is a dim middle dot, a nested
    /// cell its display, an empty JSON string a dim <c>""</c>, and any other value its logical value.
    /// </summary>
    private static (string Text, StyleKind Style) ShownOf(TableCell cell)
    {
        if (cell.IsMissing)
        {
            return ("\x00B7", StyleKind.Glyph);
        }

        if (cell.IsNested)
        {
            return (cell.Display, StyleKind.Summary);
        }

        if (cell.Part is null)
        {
            return (string.Empty, StyleKind.Text);
        }

        if (cell.Node?.JsonKind == JsonValueKind.String && cell.Part.Value.Length == 0)
        {
            return ("\"\"", StyleKind.Punctuation);
        }

        return (cell.Part.Value, StyleOf(cell));
    }

    /// <summary>The style of a present value: the JSON kind's, an attribute's, or the text of an element.</summary>
    private static StyleKind StyleOf(TableCell cell)
    {
        if (cell.Part!.Kind == PartKind.AttributeValue)
        {
            return StyleKind.AttributeValue;
        }

        return cell.Node?.JsonKind switch
        {
            JsonValueKind.String => StyleKind.String,
            JsonValueKind.Number => StyleKind.Number,
            JsonValueKind.True or JsonValueKind.False => StyleKind.Bool,
            JsonValueKind.Null => StyleKind.Null,
            _ => StyleKind.Text,
        };
    }

    // ----- Drawing -----

    private void DrawHeader(int width, Attribute normal)
    {
        TableModel model = _model!;
        DrawLine(
            0,
            width,
            normal.Background,
            normal,
            "#",
            StyleKind.Index,
            labelFocused: false,
            col => (model.Columns[col].Name, StyleKind.Key),
            int.MinValue);
    }

    private void DrawRow(int y, int row, int width, Attribute normal, Color tint)
    {
        TableModel model = _model!;
        bool cursorRow = row == _row;
        Color background = cursorRow ? tint : normal.Background;
        string label = LabelOf(model, row).ToString(CultureInfo.InvariantCulture);
        DrawLine(
            y,
            width,
            background,
            normal,
            label,
            StyleKind.Index,
            labelFocused: cursorRow && _col < 0,
            col => ShownOf(model.Cell(row, col)),
            cursorRow ? _col : int.MinValue);
    }

    /// <summary>
    /// Draws one line: the label in the index column, then the columns from the scroll position, each cut and padded to its width. The
    /// cell at <paramref name="focusedColumn"/> is in reverse video.
    /// </summary>
    private void DrawLine(
        int y,
        int width,
        Color background,
        Attribute normal,
        string label,
        StyleKind labelStyle,
        bool labelFocused,
        Func<int, (string Text, StyleKind Style)> cellOf,
        int focusedColumn)
    {
        var plain = new Attribute(normal.Foreground, background, TextStyle.None);

        string labelText = label.PadLeft(_indexWidth);
        if (labelText.Length > width)
        {
            labelText = labelText[..width];
        }

        Attribute labelAttr = TreeColors.For(labelStyle, normal, background);
        Move(0, y);
        SetAttribute(labelFocused ? TreeColors.Focused(labelAttr) : labelAttr);
        AddStr(labelText);

        int x = labelText.Length;
        int gap = Math.Min(BodyLeft - labelText.Length, width - x);
        if (gap > 0)
        {
            Move(x, y);
            SetAttribute(plain);
            AddStr(new string(' ', gap));
            x += gap;
        }

        for (int column = _left; column < _widths.Length && x < width; column++)
        {
            int cellWidth = Math.Min(_widths[column], width - x);
            (string text, StyleKind style) = cellOf(column);
            string shown = RowLayout.CellText(text, cellWidth);
            int pad = cellWidth - TextWidth.Columns(shown);
            Attribute attr = TreeColors.For(style, normal, background);
            if (column == focusedColumn)
            {
                attr = TreeColors.Focused(attr);
            }

            Move(x, y);
            SetAttribute(attr);
            AddStr(pad > 0 ? shown + new string(' ', pad) : shown);
            x += _widths[column] + Gap;
        }

        if (x < width)
        {
            Move(x, y);
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

    private static bool IsPlain(Key key, char ch) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value == ch;
}
