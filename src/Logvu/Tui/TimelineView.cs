using Logvu.Core.Config;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tui;

/// <summary>
/// The timeline: a bold column header on the first row, then one row per visible event. It draws the <see cref="TimelineModel"/>
/// and forwards the timeline keys and the mouse wheel to it. It owns no rows and no position.
/// </summary>
/// <remarks>
/// <para>
/// The model's viewport height is the view's height less the header row, at least 1. The owner keeps the model in step with the view
/// through <see cref="ViewportRowsChanged"/>. The extra columns of the configuration are drawn between Level and Message, under their
/// titles.
/// </para>
/// <para>
/// Column 0 is a gutter: the selected row shows <c>&gt;</c> there, and the header and the other rows are blank. The text starts at
/// <see cref="Gutter"/>. A click selects the row under the pointer and starts a character selection on that row's text (see
/// <see cref="TimelineLayout.Text"/>); a drag extends it within the row, and only the pointer's column counts. A double-click on a row opens
/// its event's detail, as Enter does, and leaves no selection. <c>c</c> copies the selection,
/// or the selected event without one, and Esc clears a selection before anything else. A selection is forgotten when its row stops being the
/// selected one or stops being shown.
/// </para>
/// </remarks>
internal sealed class TimelineView : View
{
    /// <summary>The number of rows the wheel moves the viewport by, per event.</summary>
    internal const int WheelRows = 3;

    /// <summary>The columns left of the text. The selected row's cursor glyph is drawn in the first one.</summary>
    internal const int Gutter = 1;

    private readonly TimelineModel _model;
    private readonly TimeZoneInfo _zone;
    private readonly IReadOnlyList<ColumnSpec> _columns;

    // The widest of each extra column drawn at the current width (see ComposePage), and that width.
    private readonly List<int> _extraFloors = [];
    private int _floorWidth = -1;
    private string? _placeholder;
    private SearchPattern? _search;
    private RowKey? _activeRow;
    private bool _dimHeld;
    private SelectionState? _selection;
    private bool _dragging;

    /// <summary>Creates the view. It draws nothing but its header until a placeholder or rows are shown.</summary>
    /// <param name="model">The model the view draws and drives.</param>
    /// <param name="zone">The time zone that times are shown in.</param>
    /// <param name="columns">The configured extra columns, drawn between Level and Message in this order. Null or empty for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="zone"/> is <c>null</c>.</exception>
    public TimelineView(TimelineModel model, TimeZoneInfo zone, IReadOnlyList<ColumnSpec>? columns = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(zone);
        _model = model;
        _zone = zone;
        _columns = columns ?? Array.Empty<ColumnSpec>();
        CanFocus = true;
    }

    /// <summary>Raised when the view's height changes, with the model's viewport height for the new height.</summary>
    public event Action<int>? ViewportRowsChanged;

    /// <summary>
    /// Raised when Enter, Right or <c>l</c> is pressed with a row selected, with the selected row's key. The owner opens the event's detail.
    /// </summary>
    public event Action<RowKey>? OpenRequested;

    /// <summary>Raised by <c>c</c> with the selected text, when the selected row has a selection. The owner writes it to the clipboard.</summary>
    public event Action<string>? CopyRequested;

    /// <summary>
    /// Raised by <c>c</c> without a selection, with the key of the selected row. The owner copies the whole event, which it reads.
    /// </summary>
    public event Action<RowKey>? CopyEventRequested;

    /// <summary>Raised when the view gains or loses a selection. Not raised for each move of a drag.</summary>
    public event Action? SelectionChanged;

    /// <summary>
    /// The text shown on the first row below the header while there are no rows to show, for example "Loading…". Null for none.
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
    /// The search whose matches are highlighted in the extra and Message cells of the rows. Null for none. Changing it keeps
    /// <see cref="ActiveRow"/>; the owner clears that when the query changes.
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

    /// <summary>
    /// The row of the match the search last moved to. Its matches are drawn in the active colors, the others in the match colors. Null
    /// for none.
    /// </summary>
    public RowKey? ActiveRow
    {
        get => _activeRow;
        set
        {
            _activeRow = value;
            SetNeedsDraw();
        }
    }

    /// <summary>
    /// Whether the rows of a held page are drawn faint, while the model holds one (see <see cref="TimelineModel.IsHolding"/>). The owner
    /// sets it once a reload has taken long enough to be worth showing. The header and the placeholder are not faint.
    /// </summary>
    public bool DimHeld
    {
        get => _dimHeld;
        set
        {
            _dimHeld = value;
            SetNeedsDraw();
        }
    }

    /// <summary>True when there is a selection that <c>c</c> copies: a non-empty selection in the selected row.</summary>
    internal bool HasSelection => Current() is { Range.IsEmpty: false };

    /// <summary>The model's viewport height for a view of <paramref name="viewHeight"/> rows: the rows less the header, at least 1.</summary>
    /// <param name="viewHeight">The view's height.</param>
    public static int RowsFor(int viewHeight) => Math.Max(1, viewHeight - 1);

    /// <summary>
    /// The selected text as a search seed, when it is a non-empty selection of at most <see cref="DetailView.SeedMaxLength"/> UTF-16 units.
    /// Its line is the row's text, which is line 0 of the seed; its start is the selection's offset in that text. Null for none.
    /// </summary>
    internal SearchSeed? SelectionSeed()
    {
        if (Current() is not { Range.IsEmpty: false } selected)
        {
            return null;
        }

        int length = selected.Range.End.Offset - selected.Range.Start.Offset;
        if (length > DetailView.SeedMaxLength)
        {
            return null;
        }

        return new SearchSeed(TextSelection.Extract([selected.Text], selected.Range), 0, selected.Range.Start.Offset);
    }

    /// <summary>
    /// Handles a timeline key. Returns <c>false</c> for a key the timeline does not bind, so it reaches the window. Esc clears a selection and
    /// is not bound otherwise. <c>c</c> copies. Enter, Right and <c>l</c> open the selected row, which clears a selection first.
    /// </summary>
    /// <param name="key">The key.</param>
    internal bool HandleKey(Key key)
    {
        if (key == Key.Esc)
        {
            if (!HasSelection)
            {
                return false;
            }

            SetSelection(null);
            return true;
        }

        if (key == Key.C)
        {
            Copy();
            return true;
        }

        if (key == Key.Enter || key == Key.CursorRight || key == Key.L)
        {
            if (_model.SelectedRow is { } row)
            {
                SetSelection(null);
                OpenRequested?.Invoke(row.Key);
            }

            return true;
        }

        TimelineCommand? command = CommandFor(key);
        if (command is not { } action)
        {
            return false;
        }

        _model.Execute(action);
        return true;
    }

    /// <summary>
    /// Handles a mouse event. A left press on a row selects that row and pauses, without moving the viewport, and starts a character
    /// selection on its text; a press elsewhere clears the selection. A drag extends the selection within the row, and a release without a
    /// drag leaves none. The mouse is grabbed from the press to the release. A double-click on a row opens it, as Enter does, and leaves no
    /// selection; a double-click on the header or below the rows does nothing. A wheel event moves the viewport by <see cref="WheelRows"/>
    /// rows per event: up toward older rows and down toward newer ones.
    /// </summary>
    /// <param name="mouse">The mouse event.</param>
    /// <returns>
    /// <c>true</c> when the event was a press, a double-click on a row, a wheel event or part of a drag. <c>false</c> for a double-click that
    /// is not on a row.
    /// </returns>
    internal bool HandleMouse(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp) || mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            TimelineCommand command = mouse.Flags.HasFlag(MouseFlags.WheeledUp) ? TimelineCommand.ShiftUp : TimelineCommand.ShiftDown;
            for (int i = 0; i < WheelRows; i++)
            {
                _model.Execute(command);
            }

            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
        {
            return OpenAt(mouse);
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
            BeginSelection(mouse);
            return true;
        }

        return false;
    }

    /// <summary>The command a key is bound to in the timeline, or <c>null</c> when it is not bound.</summary>
    /// <param name="key">The key.</param>
    internal static TimelineCommand? CommandFor(Key key)
    {
        if (key == Key.CursorUp || key == Key.K)
        {
            return TimelineCommand.Up;
        }

        if (key == Key.CursorDown || key == Key.J)
        {
            return TimelineCommand.Down;
        }

        if (key == Key.CursorUp.WithShift)
        {
            return TimelineCommand.ShiftUp;
        }

        if (key == Key.CursorDown.WithShift)
        {
            return TimelineCommand.ShiftDown;
        }

        if (key == Key.PageUp)
        {
            return TimelineCommand.PageUp;
        }

        if (key == Key.PageDown)
        {
            return TimelineCommand.PageDown;
        }

        if (key == Key.End)
        {
            return TimelineCommand.End;
        }

        if (key == Key.Space)
        {
            return TimelineCommand.TogglePause;
        }

        return null;
    }

    /// <inheritdoc/>
    protected override void OnViewportChanged(DrawEventArgs e)
    {
        base.OnViewportChanged(e);
        ViewportRowsChanged?.Invoke(RowsFor(Viewport.Height));
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

        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        var header = new Attribute(normal.Foreground, normal.Background, TextStyle.Bold);

        IReadOnlyList<EventRow> visible = _model.ShownRows;
        bool faint = _dimHeld && _model.IsHolding;
        TimelinePage page = ComposePage(visible);
        ShownSelection? selection = Current(visible, page);

        DrawGutter(0, normal, cursor: false);
        DrawLine(0, TimelineLayout.Header(page.Columns, _columns), page.Columns, header, active: false, faint: false, highlight: (0, 0));

        for (int i = 0; i < page.Lines.Count && i + 1 < height; i++)
        {
            DrawGutter(i + 1, normal, cursor: i == _model.ShownSelectedScreenRow);
            bool active = _activeRow is { } key && key == visible[i].Key;
            (int From, int To) highlight = selection is { Range.IsEmpty: false } s && s.Index == i
                ? (s.Range.Start.Offset, s.Range.End.Offset)
                : (0, 0);
            DrawLine(i + 1, page.Lines[i], page.Columns, normal, active, faint, highlight);
        }

        if (visible.Count == 0 && _placeholder is { } text && height > 1)
        {
            Move(0, 1);
            SetAttribute(normal);
            AddStr(TextWidth.Fit(text, width));
        }

        return true;
    }

    /// <summary>
    /// Composes the page for the rows at the width of the text, which is the view's width less the gutter. The extra columns keep the
    /// widest width they have shown at this width (see <see cref="TimelineLayout.Compose"/>), so the columns do not jump as rows scroll. The
    /// page is the one a draw shows, and composing it again gives the same widths.
    /// </summary>
    private TimelinePage ComposePage(IReadOnlyList<EventRow> rows)
    {
        int width = Math.Max(0, Viewport.Width - Gutter);
        if (width != _floorWidth)
        {
            _floorWidth = width;
            _extraFloors.Clear();
        }

        if (_extraFloors.Count != _columns.Count)
        {
            _extraFloors.Clear();
            _extraFloors.AddRange(new int[_columns.Count]);
        }

        TimelinePage page = TimelineLayout.Compose(rows, width, _zone, _columns, _extraFloors);
        for (int i = 0; i < _extraFloors.Count; i++)
        {
            _extraFloors[i] = Math.Max(_extraFloors[i], page.Columns.Extra[i]);
        }

        return page;
    }

    /// <summary>
    /// The stored selection with the text of its row, when its row is the selected row and is shown. Otherwise the selection is forgotten
    /// and <c>null</c> is returned, so a selection never outlives its row.
    /// </summary>
    private ShownSelection? Current() =>
        _selection is null ? null : Current(_model.ShownRows, ComposePage(_model.ShownRows));

    /// <summary><see cref="Current()"/> for a page already composed from <paramref name="shown"/>.</summary>
    private ShownSelection? Current(IReadOnlyList<EventRow> shown, TimelinePage page)
    {
        if (_selection is not { } selection)
        {
            return null;
        }

        int index = -1;
        for (int i = 0; i < shown.Count && index < 0; i++)
        {
            if (shown[i].Key == selection.Key)
            {
                index = i;
            }
        }

        if (index < 0 || index >= page.Lines.Count || _model.SelectedRow?.Key != selection.Key)
        {
            Forget();
            return null;
        }

        string text = TimelineLayout.Text(page.Lines[index], page.Columns);
        return new ShownSelection(index, text, Range(selection, text));
    }

    /// <summary>
    /// The characters of the row from the anchor cell to the active cell, both cells included, on line 0 of <paramref name="text"/>. Empty
    /// until a drag has moved the active end. A drag forwards ends after the pointer's cell; a drag backwards starts at it.
    /// </summary>
    private static LogicalRange Range(SelectionState selection, string text)
    {
        if (!selection.Dragged)
        {
            return default;
        }

        LogicalRange cells = selection.Active.Before >= selection.Anchor.Before
            ? new LogicalRange(new LogicalPoint(0, selection.Anchor.Before), new LogicalPoint(0, selection.Active.After))
            : new LogicalRange(new LogicalPoint(0, selection.Active.Before), new LogicalPoint(0, selection.Anchor.After));
        return TextSelection.Clamp(cells.Start, cells.End, [text]);
    }

    /// <summary>
    /// Left press: selects the row under the pointer and starts a character selection on its text. A press on the header or below the
    /// rows selects nothing and clears the selection. The mouse is grabbed, so the drag keeps reporting when the pointer leaves the view.
    /// </summary>
    private void BeginSelection(Mouse mouse)
    {
        int row = mouse.Position?.Y ?? 0;
        int column = mouse.Position?.X ?? 0;
        int index = row - 1;
        if (index >= 0)
        {
            _model.SelectVisibleRow(index);
        }

        IReadOnlyList<EventRow> shown = _model.ShownRows;
        TimelinePage page = ComposePage(shown);
        if (index < 0 || index >= page.Lines.Count || _model.ShownSelectedScreenRow != index)
        {
            SetSelection(null);
            return;
        }

        string text = TimelineLayout.Text(page.Lines[index], page.Columns);
        (LogicalPoint Before, LogicalPoint After) cell = TextSelection.CellAt([new SelectionRow(0, 0, text.Length, Gutter)], [text], 0, column);
        var offsets = (cell.Before.Offset, cell.After.Offset);
        SetSelection(new SelectionState(shown[index].Key, offsets, offsets, Dragged: false));
        _dragging = true;
        App?.Mouse.GrabMouse(this);
    }

    /// <summary>
    /// Double-click: opens the row under the pointer, as Enter does, and clears the selection first. A double-click on the header or below
    /// the rows opens nothing. Returns <c>false</c> when no row is opened.
    /// </summary>
    private bool OpenAt(Mouse mouse)
    {
        StopDrag();
        int index = (mouse.Position?.Y ?? 0) - 1;
        if (index < 0 || index >= ComposePage(_model.ShownRows).Lines.Count)
        {
            return false;
        }

        _model.SelectVisibleRow(index);
        if (_model.ShownSelectedScreenRow != index || _model.SelectedRow is not { } row)
        {
            return false;
        }

        SetSelection(null);
        OpenRequested?.Invoke(row.Key);
        return true;
    }

    /// <summary>
    /// Drag: extends the selection to the pointer's column in the selected row. The pointer's row is ignored, so the selection stays in the
    /// row it started in.
    /// </summary>
    private void DragTo(Mouse mouse)
    {
        if (_selection is not { } selection || Current() is not { } shown)
        {
            return;
        }

        (LogicalPoint Before, LogicalPoint After) cell = TextSelection.CellAt([new SelectionRow(0, 0, shown.Text.Length, Gutter)], [shown.Text], 0, mouse.Position?.X ?? 0);
        SetSelection(selection with { Active = (cell.Before.Offset, cell.After.Offset), Dragged = true });
    }

    /// <summary>Left release: releases the grab. A press without a drag leaves no selection.</summary>
    private void EndDrag()
    {
        StopDrag();
        if (_selection is { Dragged: false })
        {
            SetSelection(null);
        }
    }

    /// <summary>Copies the selected text, or asks the owner to copy the selected event when there is no selection.</summary>
    private void Copy()
    {
        if (Current() is { Range.IsEmpty: false } selected)
        {
            CopyRequested?.Invoke(TextSelection.Extract([selected.Text], selected.Range));
        }
        else if (_model.SelectedRow is { } row)
        {
            CopyEventRequested?.Invoke(row.Key);
        }
    }

    /// <summary>
    /// Sets the selection and raises <see cref="SelectionChanged"/> when it starts or stops being one. Redraws in any case.
    /// </summary>
    private void SetSelection(SelectionState? selection)
    {
        bool had = HasSelection;
        _selection = selection;
        if (HasSelection != had)
        {
            SelectionChanged?.Invoke();
        }

        SetNeedsDraw();
    }

    /// <summary>Forgets the selection and any drag in progress. Used when the selected row changes or stops being shown.</summary>
    private void Forget()
    {
        _selection = null;
        StopDrag();
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
    /// Draws one line: each column at its offset, with the attribute of its cell. The extra columns and the message are searched; the
    /// time and the level are not. A faint line is drawn with the faint style on every cell. The <paramref name="highlight"/> is the
    /// selected range of the row's text, drawn in reverse video.
    /// </summary>
    private void DrawLine(
        int row,
        TimelineLine line,
        TimelineColumns columns,
        Attribute baseline,
        bool active,
        bool faint,
        (int From, int To) highlight)
    {
        int x = Gutter;
        int origin = 0;

        // A visible cell is followed by one space, as TimelineLayout.Text joins them, so the origin of each cell in the row's text is kept.
        void Cell(int width, string text, Attribute attribute, bool searched)
        {
            if (width <= 0)
            {
                return;
            }

            DrawCell(x, row, width, text, attribute, searched, active, faint, origin, highlight);
            x += width + 1;
            origin += text.Length + 1;
        }

        Cell(columns.Time, line.Time, LevelColors.Text(line.Severity, baseline), searched: false);
        Cell(columns.Level, line.LevelLabel, LevelColors.Label(line.Severity, baseline), searched: false);
        for (int i = 0; i < columns.Extra.Count; i++)
        {
            Cell(columns.Extra[i], line.Extra[i], LevelColors.Text(line.Severity, baseline), searched: true);
        }

        Cell(columns.Message, line.Message, LevelColors.Message(line.Severity, baseline), searched: true);
    }

    /// <summary>
    /// Draws one cell. A searched cell is drawn in runs: its matches in the match colors, or the active colors on the active row, and
    /// the rest in <paramref name="attribute"/>. The matches are those of the cell's text as drawn. A faint cell adds the faint style to
    /// each run's attribute, and the selected characters are drawn in reverse video. <paramref name="origin"/> is the cell's offset in the
    /// row's text, which <paramref name="highlight"/> is in.
    /// </summary>
    private void DrawCell(
        int x,
        int row,
        int width,
        string text,
        Attribute attribute,
        bool searched,
        bool active,
        bool faint,
        int origin,
        (int From, int To) highlight)
    {
        if (width <= 0)
        {
            return;
        }

        IReadOnlyList<SearchMatch>? matches = searched ? _search?.Matches(text) : null;
        int selectedFrom = Math.Clamp(highlight.From - origin, 0, text.Length);
        int selectedTo = Math.Clamp(highlight.To - origin, 0, text.Length);
        foreach (SearchRun run in SearchHighlight.Split(text, 0, text.Length, matches, _ => active))
        {
            int at = run.Start;
            while (at < run.End)
            {
                // Each piece is cut where the selection starts or ends, so it is drawn in one attribute.
                bool selected = at >= selectedFrom && at < selectedTo;
                int next = at < selectedFrom ? Math.Min(run.End, selectedFrom)
                    : selected ? Math.Min(run.End, selectedTo)
                    : run.End;
                Attribute painted = SearchHighlight.Paint(attribute, run.Kind);
                painted = faint ? Faint(painted) : painted;
                painted = selected ? SelectionColors.Invert(painted) : painted;
                Move(x + TextWidth.Columns(text[..at]), row);
                SetAttribute(painted);
                AddStr(text[at..next]);
                at = next;
            }
        }
    }

    /// <summary>Draws the gutter cell of a row: the cursor glyph in bold on the selected row, a blank otherwise.</summary>
    private void DrawGutter(int row, Attribute normal, bool cursor)
    {
        Move(0, row);
        if (cursor)
        {
            SetAttribute(new Attribute(normal.Foreground, normal.Background, TextStyle.Bold));
            AddStr(">");
        }
        else
        {
            SetAttribute(normal);
            AddStr(" ");
        }
    }

    /// <summary>The attribute with the faint style added, and its colors and other style kept.</summary>
    private static Attribute Faint(Attribute attribute) =>
        new(attribute.Foreground, attribute.Background, attribute.Style | TextStyle.Faint);

    /// <summary>
    /// The selection: the key of its row, and the anchor and the active end as cells, each as its offsets in the row's text (see
    /// <see cref="TextSelection.CellAt"/>). It selects nothing until a drag has moved the active end.
    /// </summary>
    private readonly record struct SelectionState(RowKey Key, (int Before, int After) Anchor, (int Before, int After) Active, bool Dragged);

    /// <summary>A selection in the shown rows: the index of its row, the row's text and the selected range in it.</summary>
    private readonly record struct ShownSelection(int Index, string Text, LogicalRange Range);
}
