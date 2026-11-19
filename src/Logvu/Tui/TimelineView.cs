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
/// The model's viewport height is the view's height less the header row, at least 1. The owner keeps the model in step with the view
/// through <see cref="ViewportRowsChanged"/>.
/// </remarks>
internal sealed class TimelineView : View
{
    /// <summary>The number of rows the wheel moves the viewport by, per event.</summary>
    internal const int WheelRows = 3;

    private readonly TimelineModel _model;
    private readonly TimeZoneInfo _zone;

    // The widest source column drawn at the current width (see OnDrawingContent), and that width.
    private int _sourceFloor;
    private int _sourceFloorWidth = -1;
    private string? _placeholder;
    private SearchPattern? _search;
    private RowKey? _activeRow;

    /// <summary>Creates the view. It draws nothing but its header until a placeholder or rows are shown.</summary>
    /// <param name="model">The model the view draws and drives.</param>
    /// <param name="zone">The time zone that times are shown in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="zone"/> is <c>null</c>.</exception>
    public TimelineView(TimelineModel model, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(zone);
        _model = model;
        _zone = zone;
        CanFocus = true;
    }

    /// <summary>Raised when the view's height changes, with the model's viewport height for the new height.</summary>
    public event Action<int>? ViewportRowsChanged;

    /// <summary>
    /// Raised when Enter, Right or <c>l</c> is pressed with a row selected, with the selected row's key. The owner opens the event's detail.
    /// </summary>
    public event Action<RowKey>? OpenRequested;

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
    /// The search whose matches are highlighted in the Source and Message cells of the rows. Null for none. Changing it keeps
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

    /// <summary>The model's viewport height for a view of <paramref name="viewHeight"/> rows: the rows less the header, at least 1.</summary>
    /// <param name="viewHeight">The view's height.</param>
    public static int RowsFor(int viewHeight) => Math.Max(1, viewHeight - 1);

    /// <summary>Handles a timeline key. Returns <c>false</c> for a key the timeline does not bind, so it reaches the window.</summary>
    /// <param name="key">The key.</param>
    internal bool HandleKey(Key key)
    {
        if (key == Key.Enter || key == Key.CursorRight || key == Key.L)
        {
            if (_model.SelectedRow is { } row)
            {
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
    /// Handles a mouse event. A left press on a row selects that row and pauses, without moving the viewport; a press on the header
    /// is ignored. A wheel event moves the viewport by <see cref="WheelRows"/> rows per event: up toward older rows and down toward newer
    /// ones.
    /// </summary>
    /// <param name="mouse">The mouse event.</param>
    /// <returns><c>true</c> when the event was a press or a wheel event.</returns>
    internal bool HandleMouse(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            int row = mouse.Position?.Y ?? 0;
            if (row >= 1)
            {
                _model.SelectVisibleRow(row - 1);
            }

            return true;
        }

        TimelineCommand command;
        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            command = TimelineCommand.ShiftUp;
        }
        else if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            command = TimelineCommand.ShiftDown;
        }
        else
        {
            return false;
        }

        for (int i = 0; i < WheelRows; i++)
        {
            _model.Execute(command);
        }

        return true;
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
        Attribute focus = GetAttributeForRole(VisualRole.Focus);
        var header = new Attribute(normal.Foreground, normal.Background, TextStyle.Bold);

        IReadOnlyList<EventRow> visible = _model.VisibleRows;
        // The source column only grows while the width stays the same, so scrolling does not make the columns jump.
        if (width != _sourceFloorWidth)
        {
            _sourceFloorWidth = width;
            _sourceFloor = 0;
        }

        TimelinePage page = TimelineLayout.Compose(visible, width, _zone, _sourceFloor);
        _sourceFloor = Math.Max(_sourceFloor, page.Columns.Source);
        DrawLine(0, TimelineLayout.Header(page.Columns), page.Columns, header, selected: false, active: false);

        for (int i = 0; i < page.Lines.Count && i + 1 < height; i++)
        {
            bool selected = i == _model.SelectedScreenRow;
            Attribute baseline = selected ? focus : normal;
            bool active = _activeRow is { } key && key == visible[i].Key;
            DrawLine(i + 1, page.Lines[i], page.Columns, baseline, selected, active);
        }

        if (visible.Count == 0 && _placeholder is { } text && height > 1)
        {
            Move(0, 1);
            SetAttribute(normal);
            AddStr(TextWidth.Fit(text, width));
        }

        return true;
    }

    /// <summary>Draws one line: each column at its offset, with the attribute of its cell. Only the source and the message are searched.</summary>
    private void DrawLine(int row, TimelineLine line, TimelineColumns columns, Attribute baseline, bool selected, bool active)
    {
        int x = 0;
        DrawCell(x, row, columns.Time, line.Time, LevelColors.Text(line.Severity, baseline, selected), searched: false, active);
        x += columns.Time > 0 ? columns.Time + 1 : 0;
        DrawCell(x, row, columns.Level, line.LevelLabel, LevelColors.Label(line.Severity, baseline, selected), searched: false, active);
        x += columns.Level > 0 ? columns.Level + 1 : 0;
        DrawCell(x, row, columns.Source, line.Source, LevelColors.Text(line.Severity, baseline, selected), searched: true, active);
        x += columns.Source > 0 ? columns.Source + 1 : 0;
        DrawCell(x, row, columns.Message, line.Message, LevelColors.Message(line.Severity, baseline, selected), searched: true, active);
    }

    /// <summary>
    /// Draws one cell. A searched cell is drawn in runs: its matches in the match colors, or the active colors on the active row, and
    /// the rest in <paramref name="attribute"/>. The matches are those of the cell's text as drawn.
    /// </summary>
    private void DrawCell(int x, int row, int width, string text, Attribute attribute, bool searched, bool active)
    {
        if (width <= 0)
        {
            return;
        }

        IReadOnlyList<SearchMatch>? matches = searched ? _search?.Matches(text) : null;
        if (matches is null or { Count: 0 })
        {
            Move(x, row);
            SetAttribute(attribute);
            AddStr(text);
            return;
        }

        foreach (SearchRun run in SearchHighlight.Split(text, 0, text.Length, matches, _ => active))
        {
            Move(x + TextWidth.Columns(text[..run.Start]), row);
            SetAttribute(SearchHighlight.Paint(attribute, run.Kind));
            AddStr(text[run.Start..run.End]);
        }
    }
}
