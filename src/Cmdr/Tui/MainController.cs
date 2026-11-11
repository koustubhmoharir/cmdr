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

    private IReadOnlyList<ListRow> _rows = Array.Empty<ListRow>();
    private object? _shownKey;
    private string? _outputTitle;
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
        _source = new CommandListSource(_selection);
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

        _details = new DetailsView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _output = new OutputView(this) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

        _listFrame = new FrameView { Title = "Commands", X = 0, Y = 0, Height = Dim.Fill(1) };
        _listFrame.Width = Dim.Func(view => Math.Max(32, (view?.SuperView?.Viewport.Width ?? 0) * 40 / 100), _listFrame);
        _listFrame.Add(_list);

        _detailsFrame = new FrameView { Title = string.Empty, X = Pos.Right(_listFrame), Y = 0, Width = Dim.Fill(), Height = 6 };
        _detailsFrame.Add(_details);

        _outputFrame = new FrameView { Title = "Output", X = Pos.Right(_listFrame), Y = Pos.Bottom(_detailsFrame), Width = Dim.Fill(), Height = Dim.Fill(1) };
        _outputFrame.Add(_output);

        _status = new StatusLine { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        ShowHints();

        _window = new MainWindow(this) { Title = BuildTitle(), X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _window.Add(_listFrame, _detailsFrame, _outputFrame, _status);

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
            ToggleFocusPane();
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
            AddGroup();
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
            MoveCommand(focused, -1);
            return true;
        }

        if (key == Key.CursorDown.WithAlt)
        {
            MoveCommand(focused, 1);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tab and Shift+Tab switch between the list and the output. Handled here rather than by Terminal.Gui's focus
    /// traversal, which did not move focus between these views in this setup.
    /// </summary>
    public void ToggleFocusPane()
    {
        if (_list.HasFocus)
        {
            _output.SetFocus();
        }
        else
        {
            _list.SetFocus();
        }
    }

    /// <summary>Ctrl+click on a row toggles its mark. Called after the click has moved focus to the row.</summary>
    public void OnCtrlClick()
    {
        if (FocusedRow is CommandRow command)
        {
            _selection.Toggle(command.Entry);
            _list.SetNeedsDraw();
        }
    }

    private ListRow? FocusedRow
    {
        get
        {
            int? index = _list.SelectedItem;
            return index is { } i && i >= 0 && i < _rows.Count ? _rows[i] : null;
        }
    }

    private IReadOnlyList<CommandEntry> TargetsOf(ListRow? focused) =>
        focused is null ? Array.Empty<CommandEntry>() : _selection.Targets(focused);

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
        object? focusKey = KeyOf(FocusedRow);
        int previousIndex = _list.SelectedItem ?? 0;

        _rows = _session.BuildRows();
        _source.Rows = _rows;
        _selection.Prune();

        int index = -1;
        if (focusKey is not null)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (ReferenceEquals(KeyOf(_rows[i]), focusKey))
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0 && _rows.Count > 0)
        {
            index = Math.Clamp(previousIndex, 0, _rows.Count - 1);
        }

        _list.SelectedItem = index >= 0 ? index : null;
        _list.EnsureSelectedItemVisible();
        _window.Title = BuildTitle();
        UpdateFocusedViews();
    }

    /// <summary>Points the details and output views at the focused row. Called when the focus moves or the rows change.</summary>
    private void UpdateFocusedViews()
    {
        ListRow? row = FocusedRow;
        _shownKey = KeyOf(row);

        _detailsFrame.Title = row switch
        {
            CommandRow command => command.Entry.Group.Name + " / " + command.Entry.Spec.Name,
            GroupRow group => group.Group.Name,
            _ => string.Empty,
        };
        _details.Show(row);
        _output.Show((row as CommandRow)?.Entry);
        UpdateOutputTitle();
        _list.SetNeedsDraw();
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
        "cmdr — " + Path.GetFileName(_session.ConfigPath) + (_session.IsDirty ? " *" : string.Empty);

    private bool ExtendMarks(int delta)
    {
        int from = _list.SelectedItem ?? -1;
        int to = from + delta;
        if (from < 0 || to < 0 || to >= _rows.Count)
        {
            return true;
        }

        _list.SelectedItem = to;
        _list.EnsureSelectedItemVisible();
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
        IReadOnlyList<CommandEntry> targets = TargetsOf(focused);
        if (targets.Count == 0)
        {
            return;
        }

        try
        {
            string? message = _session.Activate(targets);
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

    private void StopTargets(ListRow? focused, bool confirm)
    {
        IReadOnlyList<CommandEntry> targets = TargetsOf(focused);
        int stoppable = targets.Count(IsStoppable);
        if (stoppable == 0)
        {
            return;
        }

        if (confirm && MessageBox.Query(_app, "Stop", $"Stop {stoppable} command(s)?", "_Yes", "_No") != 0)
        {
            return;
        }

        try
        {
            _session.Stop(targets);
        }
        catch (ArgumentException ex)
        {
            ShowMessage(ex.Message);
        }
    }

    private void RestartTargets(ListRow? focused)
    {
        IReadOnlyList<CommandEntry> targets = TargetsOf(focused);
        if (targets.Count == 0)
        {
            return;
        }

        if (MessageBox.Query(_app, "Restart", $"Restart {targets.Count} command(s)?", "_Yes", "_No") != 0)
        {
            return;
        }

        // Restart waits for processes to exit; it runs off the UI thread and reports back when it completes.
        _ = Task.Run(async () =>
        {
            string? message;
            try
            {
                message = await _session.RestartAsync(targets);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message = "Restart failed: " + ex.Message;
            }

            if (message is not null)
            {
                Invoke(() => ShowMessage(message));
            }
        });
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

    /// <summary>Copies the command line of a command row, quoted for this platform. Group rows do nothing.</summary>
    private void CopyCommandLine(ListRow? focused)
    {
        if (focused is not CommandRow command)
        {
            return;
        }

        var spec = command.Entry.Spec;
        string line = CommandLineFormatter.Format(spec.Program, spec.Args, OperatingSystem.IsWindows());
        CopyToClipboard(line, "Copied command line");
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

    private void MoveCommand(ListRow? focused, int delta)
    {
        if (focused is not CommandRow command)
        {
            return;
        }

        if (command.Entry.Group.IsSequenceRunning)
        {
            ShowMessage("Cannot move commands while the group's sequence is running.");
            return;
        }

        // The structure change rebuilds the rows and keeps focus on the same entry.
        _session.MoveCommand(command.Entry, delta);
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
        if (_session.Groups.Count == 0)
        {
            ShowMessage("Create a group first (Ctrl+G)");
            return;
        }

        GroupEntry group = focused is null ? _session.Groups[0] : GroupOf(focused);
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

    private void AddGroup()
    {
        GroupEntry? added = null;
        using var dialog = new GroupDialog(
            _app,
            _session,
            "Add group",
            editing: null,
            name: string.Empty,
            description: null,
            mode: GroupMode.Parallel,
            commit: (name, description, mode) =>
            {
                try
                {
                    added = _session.AddGroup(name, description, mode);
                    return null;
                }
                catch (ArgumentException ex)
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
            name: group.Name,
            description: group.Description,
            mode: group.Mode,
            commit: (name, description, mode) =>
            {
                try
                {
                    _session.UpdateGroup(group, name, description, mode);
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
        try
        {
            switch (focused)
            {
                case CommandRow command:
                    if (MessageBox.Query(_app, "Delete", $"Delete command '{command.Entry.Spec.Name}'?", "_Yes", "_No") == 0)
                    {
                        _session.RemoveCommand(command.Entry);
                    }

                    break;

                case GroupRow group:
                    int count = group.Group.Commands.Count;
                    string text = $"Delete group '{group.Group.Name}' and its {count} command(s)?";
                    if (MessageBox.Query(_app, "Delete", text, "_Yes", "_No") == 0)
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

    /// <summary>Moves the focus to the row of a group or command, if it is in the list.</summary>
    private void FocusKey(object key)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (ReferenceEquals(KeyOf(_rows[i]), key))
            {
                _list.SelectedItem = i;
                _list.EnsureSelectedItemVisible();
                UpdateFocusedViews();
                return;
            }
        }
    }

    private void ShowMessage(string text)
    {
        _message = text;
        _messageUntil = DateTimeOffset.UtcNow + MessageDuration;
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
        IReadOnlyList<CommandEntry> targets = TargetsOf(FocusedRow);
        var enterTargets = targets
            .Select(e => new EnterTarget(e.Process.Snapshot.State, e.Process.Snapshot.LeftoverCount, e.Process.CanStart))
            .ToList();
        bool sequenceRunning = targets.Count > 0 && targets[0].Group.Mode == GroupMode.Sequential && targets[0].Group.IsSequenceRunning;

        string? enter = EnterHint.Describe(enterTargets, sequenceRunning);
        return (enter is null ? string.Empty : "Enter " + enter + "  ") + HintsRest;
    }
}
