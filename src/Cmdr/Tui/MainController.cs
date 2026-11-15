using System.IO;
using System.Threading;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
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
internal sealed class MainController : IDisposable
{
    private static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(5);
    // The hint bar shows the keys for using commands. The Enter part depends on the targets (see HintLeft).
    // "^Q Quit" stays visible at the right edge when the bar is narrow.
    private const string HintsRest = "Esc/^C Stop  F5 Restart  Space Mark  ^A All  L Log  c Copy  ^S Save";
    private const string HintsRight = "  ^Q Quit";

    private readonly IApplication _app;
    private readonly Session _session;
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly Selection _selection = new();
    private readonly CommandListSource _source;
    private int _refreshPending;
    private volatile bool _closed;
    private bool _loadErrorsShown;
    private string? _shownHints;

    // Groups collapsed in the list. UI state only: not saved, and not part of the session. Compared by identity.
    private readonly HashSet<GroupEntry> _collapsed = new(ReferenceEqualityComparer.Instance);

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
    private FrameView _outputFrame = null!;
    private CommandListView _list = null!;
    private DetailsView _details = null!;
    private OutputView _output = null!;
    private StatusLine _status = null!;

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

        _details = new DetailsView(this) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _output = new OutputView(this) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

        _listFrame = new FrameView { Title = BuildTitle(), X = 0, Y = 0, Height = Dim.Fill(1) };
        _listFrame.Width = Dim.Func(view => Math.Max(32, (view?.SuperView?.Viewport.Width ?? 0) * 40 / 100), _listFrame);
        _listFrame.Add(_list);

        _detailsFrame = new FrameView { Title = string.Empty, X = Pos.Right(_listFrame), Y = 0, Width = Dim.Fill(), Height = 6 };
        _detailsFrame.Add(_details);

        _outputFrame = new FrameView { Title = "Output", X = Pos.Right(_listFrame), Y = Pos.Bottom(_detailsFrame), Width = Dim.Fill(), Height = Dim.Fill(1) };
        _outputFrame.Add(_output);

        _status = new StatusLine { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        ShowHints();

        _window = new MainWindow(this) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _window.Add(_listFrame, _detailsFrame, _outputFrame, _status);

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

        return false;
    }

    /// <summary>Keys of the command list. Returns true when handled. Esc is always handled, so it never quits.</summary>
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

        if (key == Key.N.WithCtrl)
        {
            AddCommand(focused);
            return true;
        }

        if (key == Key.G.WithCtrl)
        {
            AddGroup(focused);
            return true;
        }

        if (key == Key.F2)
        {
            EditFocused(focused);
            return true;
        }

        if (key == Key.Delete)
        {
            DeleteFocused(focused);
            return true;
        }

        if (key == Key.Enter)
        {
            Activate(focused);
            return true;
        }

        if (key == Key.Esc)
        {
            StopTargets(focused, confirm: true);
            return true;
        }

        if (key == Key.C.WithCtrl)
        {
            StopTargets(focused, confirm: false);
            return true;
        }

        if (key == Key.F5)
        {
            RestartTargets(focused);
            return true;
        }

        if (key.NoShift == Key.L && !key.IsCtrl && !key.IsAlt)
        {
            ToggleLog(focused);
            return true;
        }

        if (key == Key.CursorUp.WithAlt)
        {
            MoveItem(focused, -1);
            return true;
        }

        if (key == Key.CursorDown.WithAlt)
        {
            MoveItem(focused, 1);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tab moves the focus through the list, the details strip and the output, and Shift+Tab backward. Handled here rather
    /// than by Terminal.Gui's focus traversal, which did not move focus between these views in this setup.
    /// </summary>
    public void CycleFocusPane(bool backward)
    {
        View[] panes = { _list, _details, _output };
        panes[FocusCycle.Next(Array.FindIndex(panes, pane => pane.HasFocus), panes.Length, backward)].SetFocus();
    }

    /// <summary>Called by the details view after its scroll position changed. Updates the range shown in its title.</summary>
    public void OnDetailsScrolled() => UpdateDetailsTitle();

    /// <summary>
    /// Ctrl+click toggles the mark of the row clicked (an index into the shown rows). Without a row, the focused row is used.
    /// Group headers have no mark.
    /// </summary>
    public void OnCtrlClick(int rowIndex)
    {
        int index = rowIndex >= 0 ? rowIndex : FocusedIndex;
        if (index >= 0 && index < _rows.Count && _rows[index] is CommandRow command)
        {
            _selection.Toggle(command.Entry);
            _list.SetNeedsDraw();
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

    private static object? KeyOf(ListRow? row) => row switch
    {
        GroupRow group => group.Group,
        CommandRow command => command.Entry,
        _ => null,
    };

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

        RedrawLists();
        return true;
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
        _collapsed.RemoveWhere(group => !_session.AllGroups.Any(existing => ReferenceEquals(existing, group)));
        if (focusedRow is not null && TreeRows.FocusHeaderFor(TreeRows.ItemOf(focusedRow), _collapsed) is { } header)
        {
            focusKey = header;
        }

        _rows = VisibleRows(_session.BuildRows());
        _source.Rows = _rows;
        _selection.Prune();

        int index = focusKey is null ? -1 : IndexOfKey(focusKey);
        if (index < 0 && _rows.Count > 0)
        {
            index = Math.Clamp(previousIndex, 0, _rows.Count - 1);
        }

        FocusRowIndex(index);
        _listFrame.Title = BuildTitle();
        UpdateFocusedViews();
    }

    /// <summary>The rows shown: everything below a collapsed group is left out, at any depth.</summary>
    private IReadOnlyList<ListRow> VisibleRows(IReadOnlyList<ListRow> all) => TreeRows.Visible(all, _collapsed);

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
            _outputFrame.Title = title;
        }
    }

    private string BuildTitle() =>
        Path.GetFileName(_session.ConfigPath) + (_session.IsDirty ? " *" : string.Empty);

    /// <summary>
    /// Left: on a command, collapses its group (the focus moves to the header). On an expanded header, collapses it. On a
    /// collapsed header, moves the focus to the header of its parent (nothing at top level).
    /// </summary>
    private void CollapseOrFocusHeader(ListRow? focused)
    {
        switch (focused)
        {
            case GroupRow group when _collapsed.Contains(group.Group):
                if (group.Group.Parent is { } parent)
                {
                    FocusKey(parent);
                }

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

    /// <summary>Right: expands a collapsed group header; the focus stays on it. Otherwise nothing.</summary>
    private void ExpandHeader(ListRow? focused)
    {
        if (focused is GroupRow group && _collapsed.Remove(group.Group))
        {
            RebuildRows();
        }
    }

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

    private void Activate(ListRow? focused)
    {
        ActionTarget? target = TargetOf(focused);
        if (target is null || CommandsOf(target).Count == 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<InputPlaceholders.InputRequest> requests = target switch
            {
                GroupTarget group => _session.InputsForActivate(group.Group),
                CommandsTarget commands => _session.InputsForActivate(commands.Commands),
                _ => Array.Empty<InputPlaceholders.InputRequest>(),
            };
            IReadOnlyDictionary<string, string>? inputs = null;
            if (requests.Count > 0)
            {
                inputs = InputDialog.Ask(_app, TargetTitle(target), requests);
                if (inputs is null)
                {
                    return;
                }
            }

            string? message = target switch
            {
                GroupTarget group => _session.Activate(group.Group, inputs),
                CommandsTarget commands => _session.Activate(commands.Commands, inputs),
                _ => null,
            };
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

    private void StopTargets(ListRow? focused, bool confirm)
    {
        ActionTarget? target = TargetOf(focused);
        if (target is null)
        {
            return;
        }

        int stoppable = CommandsOf(target).Count(IsStoppable);
        if (stoppable == 0)
        {
            return;
        }

        if (confirm && !Confirm.Ask(_app, "Stop", $"Stop {stoppable} command(s)?"))
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

    private void RestartTargets(ListRow? focused)
    {
        ActionTarget? target = TargetOf(focused);
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
            IReadOnlyList<InputPlaceholders.InputRequest> requests = target switch
            {
                GroupTarget group => _session.InputsForRestart(group.Group),
                CommandsTarget commands => _session.InputsForRestart(commands.Commands),
                _ => Array.Empty<InputPlaceholders.InputRequest>(),
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
        string line = CommandLineFormatter.ForEntry(command.Entry, powerShell: windows);
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
    /// Alt+Up/Down: moves a command or a group (with everything below it) among its siblings. The session refuses without
    /// saying why; at either end nothing happens, otherwise the refusal is because a sequence is running.
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

        // The structure change rebuilds the rows and keeps focus on the same entry.
        try
        {
            if (_session.MoveItem(item, delta))
            {
                return;
            }
        }
        catch (InvalidOperationException ex)
        {
            ShowMessage(ex.Message);
            return;
        }

        // At top level, only the groups of the opened file count: moves stop at its first group, as the session does.
        IReadOnlyList<ItemEntry> siblings = item.Parent is { } parent ? parent.Items : GroupTree.Editable(_session.Groups);
        int index = siblings.ToList().IndexOf(item);
        if (!TreeRows.CanMoveWithin(index, siblings.Count, delta))
        {
            return;
        }

        string kind = item is GroupEntry ? "groups" : "commands";
        ShowMessage($"Cannot move {kind} while a sequence they belong to is running.");
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
        // Without a focused row the command goes to the first group of the opened file. A focused base group is refused.
        GroupEntry? group = focused is null ? GroupTree.Editable(_session.Groups).FirstOrDefault() : GroupOf(focused);
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

        bool running = entry.IsActive;
        if (RunDialog(dialog))
        {
            FocusKey(entry);
            if (running)
            {
                ShowMessage("Changes apply on next start.");
            }
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

    /// <summary>Delete: asks first. The session refuses to remove running, queued or sequenced commands; that is shown as an error.</summary>
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

    /// <summary>Shows the hints unless the text is already shown.</summary>
    private void ShowHints()
    {
        string left = HintLeft();
        if (left == _shownHints)
        {
            return;
        }

        _shownHints = left;
        _status.Show(left, message: false, right: HintsRight);
    }

    /// <summary>The hint text left of "^Q Quit": "Enter Start" / "Enter Kill" for the current targets, when Enter does something.</summary>
    private string HintLeft()
    {
        ActionTarget? target = TargetOf(FocusedRow);
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
        return (enter is null ? string.Empty : "Enter " + enter + "  ") + HintsRest;
    }
}
