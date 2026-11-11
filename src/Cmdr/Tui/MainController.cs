using System.IO;
using System.Threading;
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
    private const string Hints = "Enter Start/Kill  Esc Stop  F5 Restart  Space Mark  ^A All  L Log  c Copy  ^Q Quit";

    private readonly IApplication _app;
    private readonly Session _session;
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly Selection _selection = new();
    private readonly CommandListSource _source;
    private int _refreshPending;
    private volatile bool _closed;

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

        object? key = KeyOf(FocusedRow);
        if (!ReferenceEquals(key, _shownKey))
        {
            UpdateFocusedViews();
        }

        _output.Tick();
        UpdateOutputTitle();

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
        IReadOnlyList<CommandEntry> active = _session.ActiveEntries;
        if (active.Count == 0 && !_session.IsDirty)
        {
            _app.RequestStop();
            return;
        }

        // TODO(step 7): replaced by the full quit flow (stop gracefully and wait, save, cancel).
        var reasons = new List<string>();
        if (active.Count > 0)
        {
            reasons.Add($"{active.Count} command(s) are still running or have leftover processes.");
        }

        if (_session.IsDirty)
        {
            reasons.Add("There are unsaved changes.");
        }

        string message = string.Join(" ", reasons) + "\nQuit anyway (force kill)?";
        if (MessageBox.Query(_app, "Quit", message, "Quit anyway (force kill)", "Cancel") == 0)
        {
            _session.ForceKillAll();
            _app.RequestStop();
        }
    }

    private void ShowMessage(string text)
    {
        _message = text;
        _messageUntil = DateTimeOffset.UtcNow + MessageDuration;
        _status.Show(text, message: true);
    }

    private void ShowHints() => _status.Show(Hints, message: false);
}
