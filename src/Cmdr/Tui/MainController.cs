using System.IO;
using System.Threading;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Session = Cmdr.Core.Session.Session;

namespace Cmdr.Tui;

/// <summary>
/// Builds the main window and applies the key rules of the command list. All behaviour is delegated to the session and
/// <see cref="Selection"/>; this class only maps keys to actions, shows dialogs and messages, and keeps the views in step.
/// </summary>
/// <remarks>
/// Session events arrive on any thread. Handlers marshal to the UI thread with <see cref="IApplication.Invoke(Action)"/>.
/// Views are touched on the UI thread only.
/// </remarks>
internal sealed class MainController : IDisposable, IListSearchHost
{
    private static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(5);
    // The hint bar shows the keys that act on the focused pane and row, for the session's mode (see HintLeftHint).
    // "^Q Quit" stays visible at the right edge when the bar is narrow.
    // While the search bar's find field has the focus, the search keys replace all parts (see SearchHint).
    // Each label is a click target that performs its key (see KeyHints).
    private static readonly Hint SaveHint = KeyHints.Bound(Key.S.WithCtrl, "^S Save");
    private static readonly Hint MaxHint = KeyHints.Bound((Key)'+', "+ Max");
    private static readonly Hint ToEditHint = KeyHints.Bound(Key.E.WithCtrl, "^E Edit");
    private static readonly Hint ToRunHint = KeyHints.Bound(Key.E.WithCtrl, "^E Run");
    private const string EditOnlyMessage = "Press Ctrl+E for edit mode to change groups and commands.";
    private const string RunOnlyMessage = "Press Ctrl+E for run mode to start or stop commands.";
    private static readonly Hint NewCommandHint = KeyHints.Bound(Key.N.WithCtrl, "^N Command");
    private static readonly Hint NewGroupHint = KeyHints.Bound(Key.G.WithCtrl, "^G Group");
    private static readonly Hint MoveHint = KeyHints.Concat(
        KeyHints.Bound(Key.CursorUp.WithAlt, "Alt+↑"),
        KeyHints.Bound(Key.CursorDown.WithAlt, "↓"),
        KeyHints.Plain(" Move"));
    private static readonly Hint HintsRight = KeyHints.Concat(
        KeyHints.Plain(KeyHints.Separator),
        KeyHints.Bound(Key.Q.WithCtrl, "^Q Quit"));
    private const int TopRowMinHeight = 5;
    private const int TopRowMaxPercent = 60;

    private readonly IApplication _app;
    private readonly Session _session;
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly Selection _selection = new();
    private readonly CommandListSource _source;
    private int _refreshPending;
    private volatile bool _closed;
    private bool _loadErrorsShown;
    private string? _shownHints;
    private bool _maximized;

    // Files and groups collapsed in the list, by key (a ConfigSource or a GroupEntry, see TreeRows.KeyOf). UI state only: not saved,
    // and not part of the session. Compared by identity.
    private readonly HashSet<object> _collapsed = new(ReferenceEqualityComparer.Instance);

    // Files and groups the list search expanded to reveal a match. Closing the search collapses them again (see RestoreSearchCollapsed).
    private readonly HashSet<object> _searchExpanded = new(ReferenceEqualityComparer.Instance);
    private ListSearchTarget _listSearch = null!;

    // The search bar's target: the pane that had the focus when the bar opened, while the bar is open. Null while it is closed.
    private View? _searchPane;
    private SearchPattern? _searchPattern;

    // The last query with non-empty text, reopened by F3 and '/'. Its option flags follow the bar when it closes (see CloseSearch).
    private SearchQuery _lastQuery = new(string.Empty, false, false, false);

    // The rows shown: the session's rows without the commands of collapsed groups.
    private IReadOnlyList<ListRow> _rows = Array.Empty<ListRow>();
    private object? _shownKey;
    private string? _outputTitle;
    private string? _detailsTitle;
    private string? _message;
    private DateTimeOffset _messageUntil;

    private Window _window = null!;
    private FrameView _listFrame = null!;
    private FrameView _detailsFrame = null!;
    private OutputHeader _outputHeader = null!;
    private CommandListView _list = null!;
    private DetailsView _details = null!;
    private OutputView _output = null!;
    private HintsLine _status = null!;
    private SearchBar _searchBar = null!;

    public MainController(IApplication app, Session session)
    {
        _app = app;
        _session = session;
        _source = new CommandListSource(_selection, _collapsed);
    }

    /// <summary>The main window. Valid after construction.</summary>
    public Window Window => _window;

    /// <summary>Creates the views, subscribes to the session and builds the rows.</summary>
    public void Build()
    {
        _list = new CommandListView(this, _source)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            TabStop = TabBehavior.TabStop,
        };
        _listSearch = new ListSearchTarget(this, _source);

        _details = new DetailsView(this) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

        // The frames' heights come from ApplyLayout: the top row is as tall as the list needs (see TopRowHeight) in run mode.
        _listFrame = new FrameView { Title = BuildTitle(), X = 0, Y = 0 };
        _listFrame.Width = Dim.Func(view => Math.Max(32, (view?.SuperView?.Viewport.Width ?? 0) * 40 / 100), _listFrame);
        _listFrame.Add(_list);

        _detailsFrame = new FrameView { Title = string.Empty, X = Pos.Right(_listFrame), Y = 0, Width = Dim.Fill() };
        _detailsFrame.Add(_details);

        // The output has no box: a one-row header line stands above it, and the output takes the rows below.
        _outputHeader = new OutputHeader(() => _output.SetFocus()) { HeaderTitle = "Output", X = 0, Y = Pos.Bottom(_detailsFrame), Width = Dim.Fill(), Height = 1 };
        _output = new OutputView(this) { X = 0, Y = Pos.Bottom(_outputHeader), Width = Dim.Fill(), Height = Dim.Fill(1) };

        _status = new HintsLine { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };

        // The search bar sits above the status line and is shown only while a search is open (see SetSearchBarOpen).
        _searchBar = new SearchBar { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 1, Visible = false };
        _searchBar.QueryChanged += OnSearchQueryChanged;
        _searchBar.FocusPaneRequested += FocusSearchPane;
        _searchBar.Closed += CloseSearch;
        _searchBar.FindRequested += FindNext;
        ShowHints();

        _window = new MainWindow(this) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _window.Add(_listFrame, _detailsFrame, _outputHeader, _output, _status, _searchBar);
        ApplyLayout();

        // Groups marked expanded: false start collapsed. Only here, at load; the list's state is not written back.
        foreach (GroupEntry group in TreeRows.InitiallyCollapsed(_session.AllGroups))
        {
            _collapsed.Add(group);
        }

        _session.EntryChanged += OnEntryChanged;
        _session.StructureChanged += OnStructureChanged;
        RebuildRows();
    }

    /// <summary>Starts the timers and focuses the list. Call after <see cref="Build"/>, before the application runs.</summary>
    public void Start()
    {
        _app.AddTimeout(TimeSpan.FromMilliseconds(100), OnFastTick);
        _app.AddTimeout(TimeSpan.FromSeconds(1), OnSlowTick);
        _list.SetFocus();
    }

    /// <summary>Unsubscribes from the session and stops marshalling to the UI. Call before the application is disposed.</summary>
    public void Dispose()
    {
        _closed = true;
        _session.EntryChanged -= OnEntryChanged;
        _session.StructureChanged -= OnStructureChanged;
    }

    /// <summary>Keys handled everywhere in the main window. Returns true when handled.</summary>
    public bool OnGlobalKey(Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            RequestQuit();
            return true;
        }

        if (key == Key.S.WithCtrl)
        {
            Save();
            return true;
        }

        if (key == Key.E.WithCtrl)
        {
            SwitchMode();
            return true;
        }

        if (IsMaximizeKey(key))
        {
            ToggleMaximized();
            return true;
        }

        if (IsSearchKey(key))
        {
            OpenSearch();
            return true;
        }

        if (key == Key.F3)
        {
            return OnFindKey(backward: false);
        }

        if (key == Key.F3.WithShift)
        {
            return OnFindKey(backward: true);
        }

        return false;
    }

    /// <summary>'/' without Ctrl or Alt, and Ctrl+F: both open the search bar, see <see cref="OpenSearch"/>.</summary>
    private static bool IsSearchKey(Key key) => key == Key.F.WithCtrl || (!key.IsCtrl && !key.IsAlt && key.AsRune.Value == '/');

    /// <summary>
    /// '+' and '=' are one key on most keyboards. Both are accepted, with or without Shift: the driver reports the rune
    /// either way, and <see cref="Key.AsRune"/> resolves the shifted form.
    /// </summary>
    private static bool IsMaximizeKey(Key key) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value is '+' or '=';

    /// <summary>
    /// Shows the output pane over the whole area above the status line and hides the list and the info pane, or restores
    /// the normal layout. Focus on a hidden pane moves to the output first; otherwise it stays where it is. There is no output
    /// pane in edit mode, so the key does nothing there.
    /// </summary>
    private void ToggleMaximized()
    {
        if (IsEditMode)
        {
            return;
        }

        if (!_maximized && (_list.HasFocus || _details.HasFocus))
        {
            _output.SetFocus();
        }

        _maximized = !_maximized;
        ApplyLayout();
    }

    /// <summary>
    /// Lays out the main window for the session's mode, the maximize state and the search bar. Run mode: the top row (list and info
    /// pane) is as tall as the list needs, with the output header and the output below it; maximized, the output alone takes the
    /// area. Edit mode: no output pane, and the top row fills the area above the status line, or above the search bar while it is open.
    /// Every change of these calls this, so the layout is decided here.
    /// </summary>
    private void ApplyLayout()
    {
        bool edit = IsEditMode;
        int rowsBelowTop = _searchBar.Visible ? 2 : 1;

        _listFrame.Visible = !_maximized;
        _detailsFrame.Visible = !_maximized;
        _listFrame.Height = edit ? Dim.Fill(rowsBelowTop) : Dim.Func(TopRowHeight, _listFrame);
        _detailsFrame.Height = edit ? Dim.Fill(rowsBelowTop) : Dim.Func(TopRowHeight, _detailsFrame);

        _outputHeader.Visible = !edit;
        _output.Visible = !edit;
        // The header moves when the top row is hidden; the output sits one row below it in both layouts.
        _outputHeader.Y = _maximized ? 0 : Pos.Bottom(_detailsFrame);
        _output.Height = Dim.Fill(rowsBelowTop);

        _window.SetNeedsLayout();
    }

    /// <summary>
    /// Ctrl+E. In run mode, switches to edit mode unless <see cref="Session.SwitchToEdit"/> refuses (the refusal is shown and nothing
    /// changes). Entering edit mode clears the marks, restores the output when it was maximized, and moves the focus off the output
    /// pane, which is hidden there, to the list. The open search follows the focus to the list. In edit mode, switches to run mode and
    /// keeps the focus where it is. Either way the layout, the title and the hints follow the mode.
    /// </summary>
    private void SwitchMode()
    {
        if (IsEditMode)
        {
            _session.SwitchToRun();
        }
        else
        {
            string? refusal = _session.SwitchToEdit();
            if (refusal is not null)
            {
                ShowMessage(refusal);
                return;
            }

            _selection.Clear();
            _maximized = false;
            if (_output.HasFocus)
            {
                _list.SetFocus();
            }

            if (_searchPane is { } pane && !ReferenceEquals(pane, _list) && !ReferenceEquals(pane, _details))
            {
                RetargetSearch(_list);
            }
        }

        _list.SetNeedsDraw();
        _details.SetNeedsDraw();
        ApplyLayout();
        _listFrame.Title = BuildTitle();
        RefreshHints();
    }

    /// <summary>True in edit mode: groups and commands can be changed, and nothing can be started (see <see cref="SwitchMode"/>).</summary>
    private bool IsEditMode => _session.Mode == SessionMode.Edit;

    /// <summary>
    /// Keys of the command list. Returns true when handled. Esc is always handled, so it never quits, and it does nothing else.
    /// </summary>
    /// <remarks>
    /// In run mode, Enter, Ctrl+C, F5, L and Del act on the marks or the focused row here (Del clears their output). In edit mode
    /// Enter and F2 edit the focused row, Del deletes it, and Ctrl+N, Ctrl+G and Alt+Up/Down add and move. The info and output
    /// panes take the same keys for the shown command alone (<see cref="OnShownCommandKey"/>). Marking, moving, adding and
    /// collapsing are list-only. A file row does nothing with the command keys (see <see cref="IsIgnoredOnFile"/>); Ctrl+N and Ctrl+G
    /// add a command or a group as they do with no row focused.
    /// </remarks>
    public bool OnListKey(Key key)
    {
        if (OnGlobalKey(key))
        {
            return true;
        }

        ListRow? focused = FocusedRow;

        if (key == Key.Tab || key == Key.Tab.WithShift)
        {
            CycleFocusPane(backward: key == Key.Tab.WithShift);
            return true;
        }

        // A file row takes the keys of a group or command row and does nothing with them: no action and no message.
        if (focused is FileRow && IsIgnoredOnFile(key))
        {
            return true;
        }

        // Marks are for run mode. In edit mode their keys are consumed and do nothing, so the list's own keys do not get them either.
        if (IsEditMode && IsMarkKey(key))
        {
            return true;
        }

        if (key == Key.Space)
        {
            if (focused is CommandRow command)
            {
                _selection.Toggle(command.Entry);
                _list.SetNeedsDraw();
            }

            return true;
        }

        if (key == Key.CursorLeft)
        {
            CollapseOrFocusHeader(focused);
            return true;
        }

        if (key == Key.CursorRight)
        {
            ExpandHeader(focused);
            return true;
        }

        if (key == Key.CursorUp.WithShift)
        {
            return ExtendMarks(-1);
        }

        if (key == Key.CursorDown.WithShift)
        {
            return ExtendMarks(1);
        }

        if (key == Key.A.WithCtrl)
        {
            if (focused is not null)
            {
                _selection.ToggleAll(GroupOf(focused));
                _list.SetNeedsDraw();
            }

            return true;
        }

        if (key == Key.C)
        {
            CopyCommandLine(focused);
            return true;
        }

        // The edit keys act in edit mode and say how to get there in run mode.
        if (key == Key.N.WithCtrl)
        {
            return EditOnly(() => AddCommand(focused));
        }

        if (key == Key.G.WithCtrl)
        {
            return EditOnly(() => AddGroup(focused));
        }

        if (key == Key.F2)
        {
            return EditOnly(() => EditFocused(focused));
        }

        if (key == Key.Delete)
        {
            if (IsEditMode)
            {
                DeleteFocused(focused);
            }
            else
            {
                ClearOutputs(TargetOf(focused));
            }

            return true;
        }

        if (key == Key.CursorUp.WithAlt)
        {
            return EditOnly(() => MoveItem(focused, -1));
        }

        if (key == Key.CursorDown.WithAlt)
        {
            return EditOnly(() => MoveItem(focused, 1));
        }

        if (key == Key.Enter)
        {
            if (IsEditMode)
            {
                EditFocused(focused);
            }
            else
            {
                Activate(TargetOf(focused));
            }

            return true;
        }

        // Esc closes the search when it is open, and is consumed either way so that it never quits the application. Stopping is Ctrl+C only.
        if (key == Key.Esc)
        {
            return OnPaneEscape();
        }

        // The run keys act in run mode and say how to get there in edit mode.
        if (key == Key.C.WithCtrl)
        {
            return RunOnly(() => StopTargets(TargetOf(focused)));
        }

        if (key == Key.F5)
        {
            return RunOnly(() => RestartTargets(TargetOf(focused)));
        }

        if (IsLogKey(key))
        {
            return RunOnly(() => ToggleLog(focused));
        }

        return false;
    }

    /// <summary>Space, Shift+Up and Shift+Down (extend the marks) and Ctrl+A (mark all in the group). Ctrl+click is a mouse action, see <see cref="OnCtrlClick"/>.</summary>
    private static bool IsMarkKey(Key key) =>
        key == Key.Space || key == Key.CursorUp.WithShift || key == Key.CursorDown.WithShift || key == Key.A.WithCtrl;

    /// <summary>
    /// The keys a file row takes and does nothing with: Enter, F2, Del, Space, Ctrl+A, Ctrl+C, c, F5, L and Alt+Up/Down. Ctrl+N and
    /// Ctrl+G are not among them: they add a command or a group whatever row is focused. Left, Right, Up, Down and Shift+Up/Down are
    /// not among them either.
    /// </summary>
    private static bool IsIgnoredOnFile(Key key) =>
        key == Key.Enter || key == Key.F2 || key == Key.Delete || key == Key.Space || key == Key.A.WithCtrl || key == Key.C
        || key == Key.C.WithCtrl || key == Key.F5 || key == Key.CursorUp.WithAlt || key == Key.CursorDown.WithAlt || IsLogKey(key);

    /// <summary>
    /// Runs an edit-mode action in edit mode. In run mode it shows the message that says how to reach edit mode. Always handled.
    /// </summary>
    private bool EditOnly(Action action)
    {
        if (IsEditMode)
        {
            action();
        }
        else
        {
            ShowMessage(EditOnlyMessage);
        }

        return true;
    }

    /// <summary>
    /// Runs a run-mode action in run mode. In edit mode it shows the message that says how to reach run mode. Always handled.
    /// </summary>
    private bool RunOnly(Action action)
    {
        if (IsEditMode)
        {
            ShowMessage(RunOnlyMessage);
        }
        else
        {
            action();
        }

        return true;
    }

    /// <summary>
    /// The command keys of the info and output panes: Enter starts (edits in edit mode), Ctrl+C stops, F5 restarts, L toggles the
    /// log, F2 edits, and Del deletes in edit mode or clears the output in run mode. They act on the command the panes show (the
    /// focused row, when it is a command) and on that command alone: marks are ignored. A group header shows no command, so these
    /// keys do nothing there. Returns true for these keys, even when nothing is done; the panes handle their own keys first and
    /// call this for the rest.
    /// </summary>
    public bool OnShownCommandKey(Key key)
    {
        if (key == Key.Enter)
        {
            if (IsEditMode)
            {
                EditFocused(ShownRow);
            }
            else
            {
                Activate(ShownTarget());
            }

            return true;
        }

        if (key == Key.C.WithCtrl)
        {
            return RunOnly(() => StopTargets(ShownTarget()));
        }

        if (key == Key.F5)
        {
            return RunOnly(() => RestartTargets(ShownTarget()));
        }

        if (IsLogKey(key))
        {
            return RunOnly(() => ToggleLog(ShownRow));
        }

        if (key == Key.F2)
        {
            return EditOnly(() => EditFocused(ShownRow));
        }

        if (key == Key.Delete)
        {
            if (IsEditMode)
            {
                DeleteFocused(ShownRow);
            }
            else
            {
                ClearOutputs(ShownTarget());
            }

            return true;
        }

        return false;
    }

    /// <summary>The focused row when it is a command, which is the command the info and output panes show; otherwise null.</summary>
    private ListRow? ShownRow => FocusedRow as CommandRow;

    /// <summary>The target of a key in the info or output pane: the shown command alone, or null when a group header is focused.</summary>
    private ActionTarget? ShownTarget() => ShownRow is CommandRow command ? new CommandsTarget(new[] { command.Entry }) : null;

    /// <summary>L, without Shift, Ctrl or Alt. Both the list and the panes turn the log on or off with it.</summary>
    private static bool IsLogKey(Key key) => key.NoShift == Key.L && !key.IsCtrl && !key.IsAlt;

    /// <summary>
    /// Tab moves the focus through the list, the details strip and the output, and Shift+Tab backward. Handled here rather
    /// than by Terminal.Gui's focus traversal, which did not move focus between these views in this setup. In edit mode the
    /// output is hidden, so the focus moves between the list and the details strip only.
    /// </summary>
    public void CycleFocusPane(bool backward)
    {
        // While the output is maximized, the other panes are hidden and must not get the focus.
        View[] panes = IsEditMode ? new View[] { _list, _details }
            : _maximized ? new View[] { _output }
            : new View[] { _list, _details, _output };
        panes[FocusCycle.Next(Array.FindIndex(panes, pane => pane.HasFocus), panes.Length, backward)].SetFocus();
    }

    /// <summary>Called by the details view after its scroll position changed. Updates the range shown in its title.</summary>
    public void OnDetailsScrolled() => UpdateDetailsTitle();

    /// <summary>
    /// Ctrl+click toggles the mark of the row clicked (an index into the shown rows). Without a row, the focused row is used.
    /// Group headers have no mark. Marks are for run mode: in edit mode the click does nothing.
    /// </summary>
    public void OnCtrlClick(int rowIndex)
    {
        if (IsEditMode)
        {
            return;
        }

        int index = rowIndex >= 0 ? rowIndex : FocusedIndex;
        if (index >= 0 && index < _rows.Count && _rows[index] is CommandRow command)
        {
            _selection.Toggle(command.Entry);
            _list.SetNeedsDraw();
        }
    }

    /// <summary>
    /// A plain click on the ▾ or ▸ of a file row or a group header (see <see cref="RowLayout.GroupGlyphColumn"/>). Focuses the header and
    /// toggles it the way the keys do: Left collapses an expanded header and Right expands a collapsed one. Command rows are left alone.
    /// </summary>
    public void OnGroupGlyphClick(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Count || _rows[rowIndex] is CommandRow)
        {
            return;
        }

        ListRow header = _rows[rowIndex];
        FocusRowIndex(rowIndex);
        if (_collapsed.Contains(TreeRows.KeyOf(header)))
        {
            ExpandHeader(header);
        }
        else
        {
            CollapseOrFocusHeader(header);
        }
    }

    /// <summary>Index into the shown rows of the focused row, or -1 when nothing is focused.</summary>
    private int FocusedIndex => _list.SelectedItem is { } line ? _source.Lines.LineToRow(line) : -1;

    private ListRow? FocusedRow
    {
        get
        {
            int i = FocusedIndex;
            return i >= 0 && i < _rows.Count ? _rows[i] : null;
        }
    }

    /// <summary>Focuses the shown row at the index (its main line) and reveals it. -1 clears the focus.</summary>
    private void FocusRowIndex(int rowIndex)
    {
        int line = _source.Lines.RowToLine(rowIndex);
        _list.SelectedItem = line >= 0 ? line : null;
        _list.RevealFocusedBlock();
    }

    private int IndexOfKey(object? key)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (ReferenceEquals(KeyOf(_rows[i]), key))
            {
                return i;
            }
        }

        return -1;
    }

    private ActionTarget? TargetOf(ListRow? focused) => focused is null ? null : _selection.Targets(focused);

    /// <summary>The commands an action applies to: every command below a group, or the commands of the target.</summary>
    private static IReadOnlyList<CommandEntry> CommandsOf(ActionTarget target) => target switch
    {
        GroupTarget group => group.Group.AllCommands,
        CommandsTarget commands => commands.Commands,
        _ => Array.Empty<CommandEntry>(),
    };

    private static GroupEntry GroupOf(ListRow row) => row switch
    {
        GroupRow group => group.Group,
        CommandRow command => command.Entry.Group,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown row type."),
    };

    private static object? KeyOf(ListRow? row) => row is null ? null : TreeRows.KeyOf(row);

    private static bool IsStoppable(CommandEntry entry)
    {
        if (entry.IsQueued)
        {
            return true;
        }

        ProcessState state = entry.Process.Snapshot.State;
        return state is ProcessState.Running or ProcessState.Stopping;
    }

    private void OnEntryChanged(CommandEntry entry)
    {
        if (_closed || Interlocked.Exchange(ref _refreshPending, 1) == 1)
        {
            return;
        }

        Invoke(() =>
        {
            Volatile.Write(ref _refreshPending, 0);
            RedrawLists();
        });
    }

    private void OnStructureChanged()
    {
        if (_closed)
        {
            return;
        }

        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            RebuildRows();
        }
        else
        {
            Invoke(RebuildRows);
        }
    }

    private void Invoke(Action action)
    {
        try
        {
            _app.Invoke(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The application has stopped; there is nothing left to update.
        }
    }

    private bool OnFastTick()
    {
        if (_closed)
        {
            return false;
        }

        if (!_loadErrorsShown)
        {
            // Shown once, after the first frame, so that the message box has a screen behind it.
            _loadErrorsShown = true;
            if (_session.LoadErrors.Count > 0)
            {
                string text = "The configuration has errors:\n" + string.Join("\n", _session.LoadErrors)
                    + "\n\nFix them here or in the file; Ctrl+S reports them again.";
                MessageBox.ErrorQuery(_app, "Configuration", text, "_OK");
            }
        }

        object? key = KeyOf(FocusedRow);
        if (!ReferenceEquals(key, _shownKey))
        {
            UpdateFocusedViews();
        }

        _output.Tick();
        UpdateOutputTitle();
        UpdateDetailsTitle();
        TickSearch();

        // The Enter hint depends on the targets, their states and marks; it is recomputed on every tick.
        if (_message is null)
        {
            ShowHints();
        }

        if (_message is not null && DateTimeOffset.UtcNow >= _messageUntil)
        {
            _message = null;
            ShowHints();
        }

        return true;
    }

    private bool OnSlowTick()
    {
        if (_closed)
        {
            return false;
        }

        // Only a running, starting or stopping command shows a duration that grows with the clock. Every other change arrives as
        // an event (OnEntryChanged), so with nothing like that in view the tick draws nothing: a redraw of one view redraws the
        // whole window in Terminal.Gui, and that once a second was most of cmdr's own idle CPU.
        if (ShowsElapsedTime(_rows))
        {
            _list.SetNeedsDraw();
        }

        return true;
    }

    /// <summary>True if one of the visible rows is a command whose status shows an elapsed time.</summary>
    internal static bool ShowsElapsedTime(IReadOnlyList<ListRow> rows)
    {
        foreach (ListRow row in rows)
        {
            if (row is CommandRow { Entry.Process.Snapshot.State: ProcessState.Running or ProcessState.Stopping })
            {
                return true;
            }
        }

        return false;
    }

    private void RedrawLists()
    {
        _list.SetNeedsDraw();
        _details.SetNeedsDraw();
    }

    private void RebuildRows()
    {
        ListRow? focusedRow = FocusedRow;
        object? focusKey = KeyOf(focusedRow);
        int previousIndex = Math.Max(FocusedIndex, 0);

        // Collapsed state of removed groups is dropped; a focused row that is now hidden gives the focus to the header that hides it.
        _collapsed.RemoveWhere(key => key is GroupEntry group && !_session.AllGroups.Any(existing => ReferenceEquals(existing, group)));
        if (focusedRow is GroupRow or CommandRow && TreeRows.FocusHeaderFor(TreeRows.ItemOf(focusedRow), _collapsed) is { } header)
        {
            focusKey = header;
        }

        _rows = VisibleRows(_session.BuildRows());
        _source.Rows = _rows;
        _selection.Prune();
        // The top row's height is a function of the list's line count; the function is evaluated at layout.
        _window.SetNeedsLayout();

        int index = focusKey is null ? -1 : IndexOfKey(focusKey);
        if (index < 0 && _rows.Count > 0)
        {
            // Nothing had the focus (the first load): the first group or command, so that a file row does not take it. With no
            // group or command at all, the first file row.
            index = focusedRow is null ? FirstItemRowIndex() : Math.Clamp(previousIndex, 0, _rows.Count - 1);
        }

        FocusRowIndex(index);
        _listFrame.Title = BuildTitle();
        UpdateFocusedViews();
    }

    /// <summary>
    /// The height of the top row (list and info pane): the list's display lines and its border, at most 60% of the window's
    /// height, and at least <see cref="TopRowMinHeight"/>. The minimum wins when the window is shorter than that.
    /// </summary>
    private int TopRowHeight(View? view)
    {
        int window = view?.SuperView?.Viewport.Height ?? 0;
        int wanted = _source.Count + 2;
        return Math.Max(TopRowMinHeight, Math.Min(window * TopRowMaxPercent / 100, wanted));
    }

    /// <summary>The rows shown: everything below a collapsed file or group is left out, at any depth.</summary>
    private IReadOnlyList<ListRow> VisibleRows(IReadOnlyList<ListRow> all) => TreeRows.Visible(all, _collapsed);

    /// <summary>
    /// The index of the first row that is not a file row (the first group or command), or of the first row when there is none. Used
    /// for the focus when nothing had it.
    /// </summary>
    private int FirstItemRowIndex()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] is not FileRow)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Points the details and output views at the focused row. Called when the focus moves or the rows change.</summary>
    private void UpdateFocusedViews()
    {
        ListRow? row = FocusedRow;
        _shownKey = KeyOf(row);

        _details.Show(row, DetailsText.ConfigDirFor(row, _session.ConfigDir));
        _output.Show((row as CommandRow)?.Entry);
        UpdateOutputTitle();
        UpdateDetailsTitle();
        _list.SetNeedsDraw();
    }

    /// <summary>The details title: the path of the focused row, with the range shown when its content overflows.</summary>
    private void UpdateDetailsTitle()
    {
        string title = DetailsText.FrameTitle(DetailsText.Title(FocusedRow), _details.Scroll);
        if (title != _detailsTitle)
        {
            _detailsTitle = title;
            _detailsFrame.Title = title;
        }
    }

    private void UpdateOutputTitle()
    {
        string title;
        if (FocusedRow is not CommandRow)
        {
            title = "Output";
        }
        else
        {
            title = _output.Following ? "Output — following —" : "Output — paused (End to follow) —";
        }

        if (title != _outputTitle)
        {
            _outputTitle = title;
            _outputHeader.HeaderTitle = title;
        }
    }

    /// <summary>Called by the output view when its focus changes. The header shows the focus state in its title.</summary>
    public void OnOutputFocusChanged(bool hasFocus)
    {
        if (_outputHeader is null)
        {
            return;
        }

        _outputHeader.PaneFocused = hasFocus;
    }

    /// <summary>The list's title: the file name, " [edit]" in edit mode, and " *" when there are unsaved changes.</summary>
    private string BuildTitle() =>
        Path.GetFileName(_session.ConfigPath)
        + (IsEditMode ? " [edit]" : string.Empty)
        + (_session.IsDirty ? " *" : string.Empty);

    /// <summary>
    /// Left: on a command, collapses its group (the focus moves to the header). On an expanded header, collapses it. On a collapsed
    /// group, moves the focus to the header of its parent, or to its file row at top level. A file row collapses; a collapsed file
    /// does nothing, as there is no header above a file.
    /// </summary>
    private void CollapseOrFocusHeader(ListRow? focused)
    {
        switch (focused)
        {
            case FileRow file when _collapsed.Contains(file.Source):
                break;

            case FileRow file:
                _collapsed.Add(file.Source);
                RebuildRows();
                break;

            case GroupRow group when _collapsed.Contains(group.Group):
                object owner = group.Group.Parent is { } parent ? parent : group.Group.Source;
                FocusKey(owner);
                break;

            case GroupRow group:
                _collapsed.Add(group.Group);
                RebuildRows();
                break;

            case CommandRow command:
                // RebuildRows moves the focus of a command in a collapsed group to its header.
                _collapsed.Add(command.Entry.Group);
                RebuildRows();
                break;
        }
    }

    /// <summary>Right: expands a collapsed file or group header; the focus stays on it. Otherwise nothing.</summary>
    private void ExpandHeader(ListRow? focused)
    {
        object? key = focused switch
        {
            FileRow file => file.Source,
            GroupRow group => group.Group,
            _ => null,
        };
        if (key is not null && _collapsed.Remove(key))
        {
            RebuildRows();
        }
    }

    /// <summary>The search of the command list. Valid after <see cref="Build"/>.</summary>
    internal ISearchTarget ListSearch => _listSearch;

    /// <summary>
    /// Collapses again the files and groups that the list search expanded, except those that hold the focused row, and forgets them.
    /// Those the user collapsed or expanded in the meantime need nothing: collapsing a collapsed one changes nothing.
    /// </summary>
    internal void RestoreSearchCollapsed()
    {
        ListRow? focused = FocusedRow;
        bool changed = false;
        foreach (object key in _searchExpanded)
        {
            bool exists = key is ConfigSource || _session.AllGroups.Any(existing => ReferenceEquals(existing, key));
            if (!HoldsFocus(key, focused) && exists)
            {
                changed |= _collapsed.Add(key);
            }
        }

        _searchExpanded.Clear();
        if (changed)
        {
            RebuildRows();
        }
    }

    /// <summary>
    /// Whether the focused row is in the file or group with the key: the file row itself, or an item of that file, or a group or command
    /// below that group.
    /// </summary>
    private static bool HoldsFocus(object key, ListRow? focused)
    {
        if (focused is null)
        {
            return false;
        }

        if (focused is FileRow focusedFile)
        {
            return ReferenceEquals(focusedFile.Source, key);
        }

        ItemEntry item = TreeRows.ItemOf(focused);
        if (key is ConfigSource file)
        {
            return ReferenceEquals(item.Source, file);
        }

        return item.Parent is { } parent && GroupTree.IsSelfOrDescendant(parent, (GroupEntry)key);
    }

    #region Search

    // The search bar searches the pane that had the focus when it opened (the target). Only that pane highlights. While the bar
    // is open, the focus moving to another pane moves the search there (see RetargetSearch). Keys go to the bar while its field
    // has the focus; the bar reports them through its events.

    /// <summary>Esc in a pane: closes the search when it is open, without clearing its text. Returns true: Esc is always handled.</summary>
    public bool OnPaneEscape()
    {
        CloseSearch();
        return true;
    }

    /// <summary>
    /// '/' or Ctrl+F. Closed: opens the bar over the focused pane with the last query, the text selected, and the focus in the field.
    /// Open: focuses the field with its text selected, and moves the search to the focused pane first when another pane has the focus.
    /// When the info or output pane has a selection within one line (see <see cref="SelectionSeedOf"/>), the query is the selected text
    /// instead, with the options of the last query (closed) or of the bar (open), and the selected occurrence is the first match. A
    /// selection that spans lines leaves the query as it was.
    /// </summary>
    private void OpenSearch()
    {
        View pane = FocusedPane() ?? _list;
        SearchSeed? seed = SelectionSeedOf(pane);
        if (_searchPane is not null)
        {
            if (!ReferenceEquals(pane, _searchPane))
            {
                RetargetSearch(pane);
            }

            if (seed is { } open)
            {
                SearchQuery query = SeedQuery(_searchBar.Query, open.Text);
                _lastQuery = query;
                _searchBar.SetQuery(query);
                ApplySearch(query, find: true, open);
            }

            _searchBar.FocusField(selectAll: true);
            return;
        }

        _searchPane = pane;
        SetSearchBarOpen(true);
        SearchQuery start = seed is { } closed ? SeedQuery(_lastQuery, closed.Text) : _lastQuery;
        if (seed is not null)
        {
            _lastQuery = start;
        }

        _searchBar.SetQuery(start);
        _searchBar.FocusField(selectAll: true);
        ApplySearch(start, find: true, seed);
    }

    /// <summary>
    /// The query a selection seeds: the options of <paramref name="options"/> and the selected text as the pattern, escaped when the
    /// Regex option is on so that it matches itself.
    /// </summary>
    private static SearchQuery SeedQuery(SearchQuery options, string text) =>
        options with { Text = options.Regex ? System.Text.RegularExpressions.Regex.Escape(text) : text };

    /// <summary>
    /// The selection seed of a pane (see <c>SelectionSeed</c> on the info and output panes): null for the list, which has no
    /// selection, and for a pane with no seed.
    /// </summary>
    private SearchSeed? SelectionSeedOf(View pane)
    {
        if (ReferenceEquals(pane, _details))
        {
            return _details.SelectionSeed();
        }

        return ReferenceEquals(pane, _output) ? _output.SelectionSeed() : null;
    }

    /// <summary>Makes the seed the active match of the info or output pane (see <c>ActivateSeed</c>). The list has no seed.</summary>
    private void ActivateSeed(View pane, SearchSeed seed)
    {
        if (ReferenceEquals(pane, _details))
        {
            _details.ActivateSeed(seed);
        }
        else if (ReferenceEquals(pane, _output))
        {
            _output.ActivateSeed(seed);
        }
    }

    /// <summary>
    /// F3 (backward false) and Shift+F3 (backward true) with the bar closed. With a previous query, reopens the bar over the focused
    /// pane with that query and moves to the next match, keeping the focus in the pane. Without one, opens the bar as '/' does.
    /// </summary>
    private bool OnFindKey(bool backward)
    {
        if (_searchPane is not null)
        {
            FindNext(backward);
            return true;
        }

        if (_lastQuery.Text.Length == 0)
        {
            OpenSearch();
            return true;
        }

        _searchPane = FocusedPane() ?? _list;
        SetSearchBarOpen(true);
        _searchBar.SetQuery(_lastQuery);
        ApplySearch(_lastQuery, find: false);
        FindNext(backward);
        return true;
    }

    /// <summary>
    /// Applies the query to the target. With a <paramref name="seed"/>, the selected occurrence becomes the active match before the
    /// find, so the find starts there. With <paramref name="find"/>, moves to the first match from the origin, as typing does.
    /// </summary>
    private void ApplySearch(SearchQuery query, bool find, SearchSeed? seed = null)
    {
        _searchPattern = SearchPattern.Create(query, out _);
        View pane = _searchPane!;
        ISearchTarget target = SearchTargetFor(pane);
        target.SetSearch(_searchPattern);
        if (seed is { } selected && _searchPattern is not null)
        {
            ActivateSeed(pane, selected);
        }

        if (_searchPattern is not null && find)
        {
            target.Find(backward: false, inclusive: true);
        }

        RefreshSearchStatus();
    }

    /// <summary>
    /// The bar's query changed by the user: applies it and moves to the first match from the origin. A non-empty query becomes
    /// <see cref="_lastQuery"/> at once, so that clearing the field does not lose it for F3 and '/' after the bar closes.
    /// </summary>
    private void OnSearchQueryChanged(SearchQuery query)
    {
        if (query.Text.Length > 0)
        {
            _lastQuery = query;
        }

        if (_searchPane is not null)
        {
            ApplySearch(query, find: true);
        }
    }

    /// <summary>Moves the target to the next (or previous) match. Wrapping shows a message; no match shows in the bar's status.</summary>
    private void FindNext(bool backward)
    {
        if (_searchPane is null || _searchPattern is null)
        {
            return;
        }

        if (SearchTargetFor(_searchPane).Find(backward, inclusive: false) == SearchResult.Wrapped)
        {
            ShowMessage("Search wrapped");
        }

        RefreshSearchStatus();
    }

    /// <summary>
    /// Moves the search to <paramref name="pane"/>: the old target drops its highlights, and the new one takes the current pattern
    /// without moving to a match, so that it does not jump.
    /// </summary>
    private void RetargetSearch(View pane)
    {
        SearchTargetFor(_searchPane!).SetSearch(null);
        _searchPane = pane;
        SearchTargetFor(pane).SetSearch(_searchPattern);
        RefreshSearchStatus();
    }

    /// <summary>
    /// Closes the search: remembers the text as a history term, keeps the last non-empty query for F3 and '/' (with the bar's current
    /// options, even when the field was cleared), clears the target's highlights, collapses the groups the search expanded, and hides the bar.
    /// </summary>
    private void CloseSearch()
    {
        if (_searchPane is not { } pane)
        {
            return;
        }

        _searchBar.RememberQuery();
        SearchQuery query = _searchBar.Query;
        // An empty field keeps the last non-empty text, so that F3 after a clear and a close brings it back.
        _lastQuery = query.Text.Length > 0
            ? query
            : _lastQuery with { CaseSensitive = query.CaseSensitive, WholeWord = query.WholeWord, Regex = query.Regex };
        SearchTargetFor(pane).SetSearch(null);
        _searchPattern = null;
        if (_searchBar.HasFieldFocus)
        {
            pane.SetFocus();
        }

        _searchPane = null;
        RestoreSearchCollapsed();
        SetSearchBarOpen(false);
    }

    /// <summary>Shows or hides the bar. The panes above it are one row shorter while the bar is shown (see <see cref="ApplyLayout"/>).</summary>
    private void SetSearchBarOpen(bool open)
    {
        _searchBar.Visible = open;
        ApplyLayout();
    }

    /// <summary>Moves the focus from the bar back to the target pane; the bar stays open.</summary>
    private void FocusSearchPane() => _searchPane?.SetFocus();

    /// <summary>
    /// Shows the bar's status for the current query and target: empty, invalid, too slow (the pattern was abandoned after a timeout),
    /// no matches, "N of M", or "M matches".
    /// </summary>
    private void RefreshSearchStatus()
    {
        if (_searchPane is null)
        {
            return;
        }

        SearchQuery query = _searchBar.Query;
        if (query.Text.Length == 0)
        {
            _searchBar.SetStatus(string.Empty);
            return;
        }

        if (_searchPattern is null)
        {
            _searchBar.SetStatus("invalid pattern");
            return;
        }

        SearchStatus status = SearchTargetFor(_searchPane).SearchStatus();
        string text = _searchPattern.TimedOut ? "pattern too slow"
            : status.Count == 0
            ? "no matches"
            : status.Active > 0 ? $"{status.Active} of {status.Count}" : $"{status.Count} matches";
        _searchBar.SetStatus(text);
    }

    /// <summary>Every tick while the bar is open: moves the search to the focused pane when it is not the target, and refreshes the status.</summary>
    private void TickSearch()
    {
        if (_searchPane is null)
        {
            return;
        }

        if (FocusedPane() is { } pane && !ReferenceEquals(pane, _searchPane))
        {
            RetargetSearch(pane);
        }

        RefreshSearchStatus();
    }

    /// <summary>The pane that has the focus among the list, the info and the output panes, or null (the bar or nothing).</summary>
    private View? FocusedPane() =>
        _list.HasFocus ? _list
        : _details.HasFocus ? _details
        : _output.HasFocus ? _output
        : null;

    /// <summary>The search of a pane: the list's own search, or the pane itself.</summary>
    private ISearchTarget SearchTargetFor(View pane)
    {
        if (ReferenceEquals(pane, _list))
        {
            return _listSearch;
        }

        return ReferenceEquals(pane, _details) ? _details : _output;
    }

    #endregion Search

    /// <summary>Every row of the session, including those inside collapsed groups.</summary>
    IReadOnlyList<ListRow> IListSearchHost.AllRows => _session.BuildRows();

    /// <summary>The key of the focused row (see <see cref="TreeRows.KeyOf"/>), or null when no row is focused.</summary>
    object? IListSearchHost.FocusedKey => KeyOf(FocusedRow);

    /// <summary>
    /// Expands the collapsed file and groups above the item (its file, then its groups), remembering them for
    /// <see cref="RestoreSearchCollapsed"/>, then focuses the row. A file row is always shown, so it expands nothing.
    /// </summary>
    void IListSearchHost.Reveal(object key)
    {
        bool removed = false;
        if (key is ItemEntry item)
        {
            removed |= RevealCollapsed(item.Source);
            for (GroupEntry? ancestor = item.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                removed |= RevealCollapsed(ancestor);
            }
        }

        if (removed)
        {
            RebuildRows();
            // The top row grows with the list; laid out first, the list reveals the item in its new height instead of scrolling
            // for the old one.
            _window.Layout();
        }

        FocusKey(key);
    }

    /// <summary>Expands a collapsed file or group for the search, remembering it for <see cref="RestoreSearchCollapsed"/>. True when it was collapsed.</summary>
    private bool RevealCollapsed(object key)
    {
        if (!_collapsed.Remove(key))
        {
            return false;
        }

        _searchExpanded.Add(key);
        return true;
    }

    /// <summary>Redraws the command list.</summary>
    void IListSearchHost.RedrawList() => _list.SetNeedsDraw();

    private bool ExtendMarks(int delta)
    {
        int from = FocusedIndex;
        int to = from + delta;
        if (from < 0 || to < 0 || to >= _rows.Count)
        {
            return true;
        }

        FocusRowIndex(to);
        UpdateFocusedViews();

        if (_rows[to] is not CommandRow target)
        {
            return true;
        }

        if (_selection.Group is { } marked && marked != target.Entry.Group)
        {
            // Another group: the marks move to the new row alone (Toggle switches the group).
            _selection.Toggle(target.Entry);
        }
        else if (_rows[from] is CommandRow previous && previous.Entry.Group == target.Entry.Group)
        {
            Mark(previous.Entry);
            Mark(target.Entry);
        }
        else
        {
            Mark(target.Entry);
        }

        _list.SetNeedsDraw();
        return true;
    }

    private void Mark(CommandEntry entry)
    {
        if (!_selection.IsMarked(entry))
        {
            _selection.Toggle(entry);
        }
    }

    /// <summary>Enter: starts the target, or force-kills what is stopping or left over. A null target does nothing.</summary>
    private void Activate(ActionTarget? target)
    {
        if (target is null || CommandsOf(target).Count == 0)
        {
            return;
        }

        try
        {
            // Decided now, when Enter is pressed: the dialog below is modal and processes keep changing while it is open.
            PreparedActivation? prepared = target switch
            {
                GroupTarget group => _session.PrepareActivate(group.Group),
                CommandsTarget commands => _session.PrepareActivate(commands.Commands),
                _ => null,
            };
            if (prepared is null)
            {
                return;
            }

            IReadOnlyDictionary<string, string>? inputs = null;
            if (prepared.Requests.Count > 0)
            {
                inputs = InputDialog.Ask(_app, TargetTitle(target), prepared.Requests);
                if (inputs is null)
                {
                    return;
                }
            }

            string? message = _session.Activate(prepared, inputs);
            if (message is not null)
            {
                ShowMessage(message);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ShowMessage(ex.Message);
        }
    }

    /// <summary>What an input dialog is about: the group's path, the command's name, or "N commands".</summary>
    private static string TargetTitle(ActionTarget target) => target switch
    {
        GroupTarget group => GroupTree.PathText(group.Group, GroupOptions.Separator),
        CommandsTarget { Commands.Count: 1 } commands => commands.Commands[0].Name,
        _ => CommandsOf(target).Count + " commands",
    };

    /// <summary>Ctrl+C: gracefully stops the running commands of the target, without asking. A null target does nothing.</summary>
    private void StopTargets(ActionTarget? target)
    {
        if (target is null)
        {
            return;
        }

        int stoppable = CommandsOf(target).Count(IsStoppable);
        if (stoppable == 0)
        {
            return;
        }

        try
        {
            if (target is GroupTarget group)
            {
                _session.Stop(group.Group);
            }
            else if (target is CommandsTarget commands)
            {
                _session.Stop(commands.Commands);
            }
        }
        catch (ArgumentException ex)
        {
            ShowMessage(ex.Message);
        }
    }

    /// <summary>F5: restarts the target after a confirmation. A null target does nothing.</summary>
    private void RestartTargets(ActionTarget? target)
    {
        if (target is null)
        {
            return;
        }

        IReadOnlyList<CommandEntry> targetCommands = CommandsOf(target);
        int count = targetCommands.Count;
        if (count == 0)
        {
            return;
        }

        // A second F5 while a restart of these commands is still waiting: most likely impatience or a slip, so ask rather
        // than start another restart or cancel the pending one.
        if (AskAboutPendingRestart(targetCommands))
        {
            return;
        }

        if (!Confirm.Ask(_app, "Restart", $"Restart {count} command(s)?"))
        {
            return;
        }

        IReadOnlyDictionary<string, string>? inputs = null;
        PreparedRestart prepared;
        try
        {
            IReadOnlyList<InputRequest> requests = target switch
            {
                GroupTarget group => _session.InputsForRestart(group.Group),
                CommandsTarget commands => _session.InputsForRestart(commands.Commands),
                _ => Array.Empty<InputRequest>(),
            };
            if (requests.Count > 0)
            {
                inputs = InputDialog.Ask(_app, TargetTitle(target), requests);
                if (inputs is null)
                {
                    return;
                }
            }

            // Registered here, on the UI thread, before the background task exists: a Stop, StopAll or quit that runs before
            // the task starts revokes it, so it starts nothing.
            prepared = target is GroupTarget preparedGroup
                ? _session.PrepareRestart(preparedGroup.Group, inputs)
                : _session.PrepareRestart(((CommandsTarget)target).Commands, inputs);
        }
        catch (ArgumentException ex)
        {
            ShowMessage(ex.Message);
            return;
        }

        // Restart waits for processes to exit; it runs off the UI thread and reports back when it completes.
        _ = Task.Run(async () =>
        {
            string? message;
            try
            {
                message = await _session.RunRestartAsync(prepared);
            }
            catch (OperationCanceledException)
            {
                // Revoked by Stop, force kill or quit before anything was started again.
                message = "Restart cancelled";
            }
            catch (Exception ex)
            {
                message = "Restart failed: " + ex.Message;
            }

            if (message is not null)
            {
                Invoke(() => ShowMessage(message));
            }
        });
    }

    /// <summary>
    /// When a pending restart already covers some of <paramref name="commands"/>, asks whether to keep waiting for it (the
    /// default) or to force-kill what it waits for so that it restarts now, and returns true: this F5 is then handled.
    /// Returns false when no restart is pending for them.
    /// </summary>
    private bool AskAboutPendingRestart(IReadOnlyList<CommandEntry> commands)
    {
        IReadOnlyList<PendingRestart> pending;
        try
        {
            pending = _session.PendingRestartsOf(commands);
        }
        catch (ArgumentException ex)
        {
            ShowMessage(ex.Message);
            return true;
        }

        if (pending.Count == 0)
        {
            return false;
        }

        string names = pending.Count == 1 ? $"'{pending[0].Command.Spec.Name}' is" : $"{pending.Count} commands are";
        string them = pending.Count == 1 ? "it" : "them";
        int? choice = Confirm.Choose(
            _app,
            "Restart already pending",
            $"{names} already restarting: waiting for {them} to stop.",
            "_Keep waiting",
            "_Force kill now");
        if (choice == 1)
        {
            try
            {
                _session.ForceKillForPendingRestart(pending);
            }
            catch (ArgumentException ex)
            {
                ShowMessage(ex.Message);
            }
        }

        return true;
    }

    private void ToggleLog(ListRow? focused)
    {
        if (focused is not CommandRow command)
        {
            return;
        }

        try
        {
            _session.ToggleLog(command.Entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            ShowMessage("Log: " + ex.Message);
        }

        UpdateFocusedViews();
    }

    /// <summary>Copies the command line of a command row: POSIX sh quoting, or PowerShell quoting on Windows. Group rows do nothing. Used by the list and the details strip.</summary>
    public void CopyCommandLine(ListRow? focused)
    {
        if (focused is not CommandRow command)
        {
            return;
        }

        bool windows = OperatingSystem.IsWindows();
        string line = CommandLineFormatter.ForCopy(command.Entry, windows, EffectiveEnv.Process(), Path.PathSeparator);
        CopyToClipboard(line, windows ? "Copied command line (PowerShell)" : "Copied command line");
    }

    /// <summary>
    /// Writes the text to the system clipboard with OSC 52 and shows <paramref name="successMessage"/>. Text over
    /// <see cref="Osc52.MaxBytes"/> is not copied.
    /// </summary>
    public void CopyToClipboard(string text, string successMessage)
    {
        if (!Osc52.Fits(text))
        {
            ShowMessage("Selection too large to copy");
            return;
        }

        _app.Driver?.WriteRaw(Osc52.Build(text));
        ShowMessage(successMessage);
    }

    /// <summary>
    /// Alt+Up/Down in edit mode: moves a command or a group (with everything below it) among its siblings. At either end, the
    /// session returns false and nothing happens. The structure change rebuilds the rows and keeps focus on the same entry.
    /// </summary>
    private void MoveItem(ListRow? focused, int delta)
    {
        if (focused is null)
        {
            return;
        }

        ItemEntry item = TreeRows.ItemOf(focused);
        if (RefuseReadOnly(item))
        {
            return;
        }

        try
        {
            _session.MoveItem(item, delta);
        }
        catch (InvalidOperationException ex)
        {
            ShowMessage(ex.Message);
        }
    }

    private void RequestQuit()
    {
        var quit = new QuitFlow(_app, _session, Save);
        if (quit.Run())
        {
            // The quit steps have closed their dialogs, so the current session is the main window.
            _app.RequestStop();
        }
    }

    /// <summary>Saves the configuration. Errors are shown in a message box. Returns true when saved.</summary>
    private bool Save()
    {
        string file = Path.GetFileName(_session.ConfigPath);
        try
        {
            _session.Save();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.ErrorQuery(_app, "Save", "Nothing was saved. Fix these first:\n" + ex.Message, "_OK");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.ErrorQuery(_app, "Save", $"Could not save {file}:\n" + ex.Message, "_OK");
            return false;
        }

        ShowMessage("Saved " + file);
        return true;
    }

    /// <summary>Ctrl+N: a command in the focused row's group. Without groups there is nowhere to put it.</summary>
    private void AddCommand(ListRow? focused)
    {
        // Without a focused row (or with a file row focused) the command goes to the first group of the opened file. A focused base group is refused.
        GroupEntry? group = focused is null or FileRow ? GroupTree.Editable(_session.Groups).FirstOrDefault() : GroupOf(focused);
        if (group is null)
        {
            ShowMessage("Create a group first (Ctrl+G)");
            return;
        }

        if (RefuseReadOnly(group))
        {
            return;
        }

        CommandEntry? added = null;
        using var dialog = new CommandDialog(
            _app,
            _session,
            "Add command",
            group,
            new CommandSpec(),
            editing: null,
            commit: (target, spec) =>
            {
                try
                {
                    added = _session.AddCommand(target, spec);
                    return null;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    return ex.Message;
                }
            });

        if (RunDialog(dialog) && added is not null)
        {
            FocusKey(added);
        }
    }

    /// <summary>Ctrl+G: a new group. Its parent starts as the parent of the focused group or of the focused command's group.</summary>
    private void AddGroup(ListRow? focused)
    {
        // A top-level group is always allowed. A group inside a base group is not: the parent is refused.
        GroupEntry? defaultParent = TreeRows.DefaultParentFor(focused);
        if (defaultParent is not null && RefuseReadOnly(defaultParent))
        {
            return;
        }

        GroupEntry? added = null;
        using var dialog = new GroupDialog(
            _app,
            _session,
            "Add group",
            editing: null,
            initialParent: defaultParent,
            initial: new GroupSettings(string.Empty, null, GroupMode.Parallel, null, null),
            commit: (parent, settings) =>
            {
                try
                {
                    added = _session.AddGroup(parent, settings);
                    return null;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    return ex.Message;
                }
            });

        if (RunDialog(dialog) && added is not null)
        {
            FocusKey(added);
        }
    }

    private void EditFocused(ListRow? focused)
    {
        if (focused is not null && RefuseReadOnly(TreeRows.ItemOf(focused)))
        {
            return;
        }

        switch (focused)
        {
            case CommandRow command:
                EditCommand(command.Entry);
                break;
            case GroupRow group:
                EditGroup(group.Group);
                break;
        }
    }

    private void EditCommand(CommandEntry entry)
    {
        using var dialog = new CommandDialog(
            _app,
            _session,
            "Edit command",
            entry.Group,
            entry.Spec,
            editing: entry,
            commit: (target, spec) =>
            {
                try
                {
                    _session.UpdateCommand(entry, spec, target);
                    return null;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    return ex.Message;
                }
            });

        if (RunDialog(dialog))
        {
            FocusKey(entry);
        }
    }

    private void EditGroup(GroupEntry group)
    {
        using var dialog = new GroupDialog(
            _app,
            _session,
            "Edit group",
            editing: group,
            initialParent: group.Parent,
            initial: new GroupSettings(group.Name, group.Description, group.Mode, group.Cwd, group.Env, group.Expanded),
            commit: (parent, settings) =>
            {
                try
                {
                    _session.UpdateGroup(group, settings, parent);
                    return null;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    return ex.Message;
                }
            });

        if (RunDialog(dialog))
        {
            FocusKey(group);
        }
    }

    /// <summary>
    /// Del in run mode: clears the in-memory output of the target's commands (see <see cref="CommandOutput.ClearOutput"/>), keeping
    /// partial lines, runs and logs. No confirmation, as nothing is lost from the log. A null target does nothing.
    /// </summary>
    private void ClearOutputs(ActionTarget? target)
    {
        if (target is null)
        {
            return;
        }

        foreach (CommandEntry entry in CommandsOf(target))
        {
            entry.Process.Output.ClearOutput();
        }

        _output.SetNeedsDraw();
    }

    /// <summary>Delete, in edit mode: asks first. A base item is refused before that; a refusal from the session is shown as an error.</summary>
    private void DeleteFocused(ListRow? focused)
    {
        if (focused is not null && RefuseReadOnly(TreeRows.ItemOf(focused)))
        {
            return;
        }

        try
        {
            switch (focused)
            {
                case CommandRow command:
                    if (Confirm.Ask(_app, "Delete", $"Delete command '{command.Entry.Spec.Name}'?"))
                    {
                        _session.RemoveCommand(command.Entry);
                    }

                    break;

                case GroupRow group:
                    if (Confirm.Ask(_app, "Delete", GroupTree.DeleteQuestion(group.Group)))
                    {
                        _session.RemoveGroup(group.Group);
                    }

                    break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            MessageBox.ErrorQuery(_app, "Delete", ex.Message, "_OK");
        }
    }

    /// <summary>Runs an add or edit dialog. Returns true when it committed.</summary>
    private bool RunDialog(Dialog<bool> dialog)
    {
        _app.Run(dialog);
        return dialog.Result == true;
    }

    /// <summary>Moves the focus to the row of a group or command, if it is in the list. A hidden row gives the focus to its header.</summary>
    private void FocusKey(object key)
    {
        if (key is ItemEntry item && TreeRows.FocusHeaderFor(item, _collapsed) is { } header)
        {
            key = header;
        }

        int index = IndexOfKey(key);
        if (index >= 0)
        {
            FocusRowIndex(index);
            UpdateFocusedViews();
        }
    }

    /// <summary>Shows the refusal when the item comes from a base file, and returns true. False (nothing shown) for an editable item.</summary>
    private bool RefuseReadOnly(ItemEntry item)
    {
        string? text = item.ReadOnlyMessage;
        if (text is null)
        {
            return false;
        }

        ShowMessage(text);
        return true;
    }

    private void ShowMessage(string text)
    {
        _message = text;
        _messageUntil = DateTimeOffset.UtcNow + MessageDuration;
        // A message replaces the cached hint display even when the hint text itself stays the same.
        // Force ShowHints to redraw it when the message expires.
        _shownHints = null;
        _status.Show(text, message: true);
    }

    /// <summary>Shows the hints unless the text is already shown. The text decides: each label's key is fixed by its text.</summary>
    private void ShowHints()
    {
        Hint left = HintLeftHint();
        if (left.Text == _shownHints)
        {
            return;
        }

        _shownHints = left.Text;
        _status.Show(left, HintsRight);
    }

    /// <summary>
    /// The target the Enter hint describes: the list's target (the marks, or the focused row) when the list has the focus, and
    /// the shown command when the info or output pane has it, as <see cref="OnShownCommandKey"/> acts on. With no pane focused,
    /// null: Enter does not reach the list then, so it does nothing that the hint could name.
    /// </summary>
    private ActionTarget? HintTarget() => FocusedCopyPane() switch
    {
        CopyPane.List => TargetOf(FocusedRow),
        CopyPane.Details or CopyPane.Output => ShownTarget(),
        _ => null,
    };

    /// <summary>The text of <see cref="HintLeftHint"/>.</summary>
    private string HintLeft() => HintLeftHint().Text;

    /// <summary>
    /// The command keys that the focused pane acts on, in the order the bar shows them, or null when it acts on none of them.
    /// The list on a command row acts on all six. The list on a group row acts on Ctrl+C, F5, Ctrl+A (on the whole group) and Del
    /// (clears the output of the whole group), not Space or L, which only a command row takes. The list with no row, a file row, and no
    /// pane focused, act on none. The info and output panes act on Ctrl+C, F5, L and Del for the shown command alone, when a command is
    /// shown, and never Space or Ctrl+A.
    /// </summary>
    private static Hint? CommandKeysOf(CopyPane pane, ListRow? focused)
    {
        switch (pane)
        {
            case CopyPane.List when focused is CommandRow:
                return KeyHints.Join(
                    KeyHints.Bound(Key.C.WithCtrl, "^C Stop"),
                    KeyHints.Bound(Key.F5, "F5 Restart"),
                    KeyHints.Bound(Key.Space, "Space Mark"),
                    KeyHints.Bound(Key.A.WithCtrl, "^A All"),
                    KeyHints.Bound(Key.L, "L Log"),
                    KeyHints.Bound(Key.Delete, "Del Clear"));
            case CopyPane.List when focused is GroupRow:
                return KeyHints.Join(
                    KeyHints.Bound(Key.C.WithCtrl, "^C Stop"),
                    KeyHints.Bound(Key.F5, "F5 Restart"),
                    KeyHints.Bound(Key.A.WithCtrl, "^A All"),
                    KeyHints.Bound(Key.Delete, "Del Clear"));
            case CopyPane.Details or CopyPane.Output when focused is CommandRow:
                return KeyHints.Join(
                    KeyHints.Bound(Key.C.WithCtrl, "^C Stop"),
                    KeyHints.Bound(Key.F5, "F5 Restart"),
                    KeyHints.Bound(Key.L, "L Log"),
                    KeyHints.Bound(Key.Delete, "Del Clear"));
            default:
                return null;
        }
    }

    /// <summary>
    /// The hint left of "^Q Quit", with its click targets. While the find field has the focus, the search keys (see <see cref="SearchHint"/>).
    /// Otherwise the parts that apply to the focused pane and row, joined by the separator, for the session's mode (see <see cref="RunHintLeft"/>
    /// and <see cref="EditHintLeft"/>).
    /// </summary>
    private Hint HintLeftHint()
    {
        if (_searchBar.HasFieldFocus)
        {
            return SearchHint.Build(_searchBar.Query.Text.Length > 0);
        }

        CopyPane pane = FocusedCopyPane();
        string? copy = CopyHint.Describe(pane, hasSelection: SelectionToCopy(), hasCommand: ShownRow is not null, hasLine: _output.HasLines);
        return IsEditMode ? EditHintLeft(pane, copy) : RunHintLeft(pane, copy);
    }

    /// <summary>
    /// The run-mode hint: "Enter Start" / "Enter Kill" for the current targets, when Enter does something; the command keys of the focused
    /// pane (see <see cref="CommandKeysOf"/>); the c hint; "^S Save" while there are unsaved changes; "+ Max"; and "^E Edit".
    /// </summary>
    private Hint RunHintLeft(CopyPane pane, string? copy)
    {
        ActionTarget? target = HintTarget();
        IReadOnlyList<CommandEntry> targets = target is null ? Array.Empty<CommandEntry>() : CommandsOf(target);
        var enterTargets = targets
            .Select(e => new EnterTarget(e.Process.Snapshot.State, e.Process.Snapshot.HasLeftovers, e.Process.CanStart))
            .ToList();
        GroupEntry? hintGroup = target switch
        {
            GroupTarget group => group.Group,
            CommandsTarget when targets.Count > 0 => targets[0].Group,
            _ => null,
        };
        bool sequenceRunning = hintGroup is { Mode: GroupMode.Sequential, IsSequenceRunning: true };

        string? enter = EnterHint.Describe(enterTargets, sequenceRunning);
        Hint? commands = CommandKeysOf(pane, FocusedRow);
        var parts = new List<Hint>();
        if (enter is not null)
        {
            parts.Add(KeyHints.Bound(Key.Enter, "Enter " + enter));
        }

        if (commands is not null)
        {
            parts.Add(commands);
        }

        if (copy is not null)
        {
            parts.Add(KeyHints.Bound(Key.C, copy));
        }

        if (_session.IsDirty)
        {
            parts.Add(SaveHint);
        }

        parts.Add(MaxHint);
        parts.Add(ToEditHint);
        return KeyHints.Join([.. parts]);
    }

    /// <summary>
    /// The edit-mode hint: "Enter Edit" and "Del Delete" when the focused group or command row (the list) or the shown command (the info
    /// pane) can be edited; in the list, "^N Command", "^G Group" and, with a group or command row focused, "Alt+↑↓ Move"; the c hint;
    /// "^S Save"; and "^E Run". A file row has none of the row hints.
    /// </summary>
    private Hint EditHintLeft(CopyPane pane, string? copy)
    {
        bool onList = pane == CopyPane.List;
        bool canEdit = pane switch
        {
            CopyPane.List => FocusedRow is GroupRow or CommandRow,
            CopyPane.Details or CopyPane.Output => ShownRow is not null,
            _ => false,
        };

        var parts = new List<Hint>();
        if (canEdit)
        {
            parts.Add(KeyHints.Bound(Key.Enter, "Enter Edit"));
        }

        if (onList)
        {
            parts.Add(NewCommandHint);
            parts.Add(NewGroupHint);
        }

        if (canEdit)
        {
            parts.Add(KeyHints.Bound(Key.Delete, "Del Delete"));
        }

        if (onList && canEdit)
        {
            parts.Add(MoveHint);
        }

        if (copy is not null)
        {
            parts.Add(KeyHints.Bound(Key.C, copy));
        }

        parts.Add(SaveHint);
        parts.Add(ToRunHint);
        return KeyHints.Join([.. parts]);
    }

    /// <summary>
    /// Shows the hints of the new mode after a switch. A message still shown is dropped: it was about the mode that was left (for
    /// example an earlier refusal to switch), and the bar must show the keys of the new mode at once.
    /// </summary>
    private void RefreshHints()
    {
        _message = null;
        _shownHints = null;
        ShowHints();
    }

    /// <summary>The pane that c acts on: the focused one, or None when neither the list nor the info or output pane has the focus.</summary>
    private CopyPane FocusedCopyPane() =>
        _list.HasFocus ? CopyPane.List
        : _details.HasFocus ? CopyPane.Details
        : _output.HasFocus ? CopyPane.Output
        : CopyPane.None;

    /// <summary>True when the info or output pane has a selection, which c copies first. Other panes have none.</summary>
    private bool SelectionToCopy() => _details.HasFocus ? _details.HasSelection : _output.HasFocus && _output.HasSelection;
}
