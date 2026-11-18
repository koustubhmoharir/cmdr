using System.IO;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace Logvu.Tui;

/// <summary>
/// Connects the store to the timeline. It owns the store subscription and the <see cref="TimelineModel"/>, applies the store's
/// updates on the UI thread, and keeps the status line current.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threads.</b> The store raises its updates on its worker thread and the model's loads complete on the thread pool. Both post
/// their work to the UI thread with <see cref="IApplication.Invoke(Action)"/>. Everything else runs on the UI thread.
/// </para>
/// <para>
/// <b>Start.</b> The timeline starts with the first update that is the initial run or a resync (a failed first run is followed by a
/// resync). Until then the view shows "Loading…".
/// </para>
/// </remarks>
internal sealed class TimelineController : IDisposable
{
    /// <summary>The status line's key hints.</summary>
    internal const string Hints = "Space pause/follow  End newest  ^Q quit";

    /// <summary>How long a new retention gap stays on the status line. The gap stays in the store's status.</summary>
    internal static readonly TimeSpan GapShownFor = TimeSpan.FromSeconds(10);

    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly TimeZoneInfo _zone;
    private readonly TimelineModel _model;
    private readonly TimelineView _view;
    private readonly StatusLine _statusLine;
    private StoreStatus? _storeStatus;
    private RetentionGap? _latestGap;
    private bool _gapShown;
    private int _gapEpoch;
    private bool _started;
    private volatile bool _disposed;

    /// <summary>Creates the controller and the window. Call <see cref="Start"/> to subscribe to the store and start it.</summary>
    /// <param name="app">The initialized application.</param>
    /// <param name="store">The store to show. It must not be started yet.</param>
    /// <param name="zone">The time zone that times are shown in.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public TimelineController(IApplication app, LogStore store, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(zone);

        _app = app;
        _store = store;
        _zone = zone;
        _model = new TimelineModel(new LogStoreTimelineSource(store), Post, store.Compare);
        _view = new TimelineView(_model, zone) { Placeholder = "Loading…" };
        _statusLine = new StatusLine();
        Window = new MainWindow(() => _app.RequestStop(Window), _view, _statusLine);
    }

    /// <summary>The main window. Run it with the application.</summary>
    public MainWindow Window { get; }

    /// <summary>Subscribes to the store and to the model, focuses the timeline and starts the store.</summary>
    /// <exception cref="InvalidOperationException">The store is already started.</exception>
    public void Start()
    {
        _store.Updated += OnStoreUpdated;
        _model.Changed += OnModelChanged;
        _view.ViewportRowsChanged += OnViewportRowsChanged;
        _view.SetFocus();
        _store.Start();
        RefreshStatus();
    }

    /// <summary>Unsubscribes from the store and the model. Updates that arrive after this one are ignored.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Updated -= OnStoreUpdated;
        _model.Changed -= OnModelChanged;
        _view.ViewportRowsChanged -= OnViewportRowsChanged;
    }

    /// <summary>Runs on the store's worker thread. Hands the update to the UI thread.</summary>
    private void OnStoreUpdated(StoreUpdate update) => Post(() => Apply(update));

    /// <summary>Applies a store update on the UI thread.</summary>
    private void Apply(StoreUpdate update)
    {
        _storeStatus = update.Status;
        ShowNewGap(update.Status);
        if (!_started)
        {
            if (update.Batch.Initial || update.Resync)
            {
                _started = true;
                _view.Placeholder = "Waiting for events…";
                _model.Start(TimelineView.RowsFor(_view.Viewport.Height));
            }

            RefreshStatus();
            return;
        }

        if (update.Resync || update.Batch.RingReset)
        {
            _model.Reset();
        }
        else
        {
            _model.Arrive(update.Batch.Added, update.Batch.Updated);
        }

        RefreshStatus();
    }

    /// <summary>
    /// Shows a gap that appeared since the last update for <see cref="GapShownFor"/>. A timeout on the UI thread hides it again, and a
    /// newer gap restarts the time: the timeout of an older gap does nothing.
    /// </summary>
    private void ShowNewGap(StoreStatus status)
    {
        RetentionGap? latest = status.Gaps.Count > 0 ? status.Gaps[^1] : null;
        if (latest is null || latest == _latestGap)
        {
            return;
        }

        _latestGap = latest;
        _gapShown = true;
        int epoch = ++_gapEpoch;
        _app.AddTimeout(GapShownFor, () =>
        {
            if (!_disposed && epoch == _gapEpoch)
            {
                _gapShown = false;
                RefreshStatus();
            }

            return false;
        });
    }

    private void OnModelChanged()
    {
        _view.SetNeedsDraw();
        RefreshStatus();
    }

    private void OnViewportRowsChanged(int rows)
    {
        if (_started && rows != _model.Height)
        {
            _model.Resize(rows);
        }
    }

    /// <summary>Runs a continuation on the UI thread. Work posted after the application stopped is dropped.</summary>
    private void Post(Action action)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _app.Invoke(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The application has stopped or been disposed; there is nothing left to update.
        }
    }

    private void RefreshStatus() => _statusLine.Show(LeftText(), message: false, right: Hints);

    /// <summary>The left part of the status line: the mode, the page day, the live file and the latest problem.</summary>
    private string LeftText()
    {
        if (!_started)
        {
            return "Loading…";
        }

        var parts = new List<string> { ModeText() };
        if (TimelineLayout.PageDate(_model.VisibleRows, _zone) is { } day)
        {
            parts.Add(day);
        }

        if (_storeStatus?.LiveMemberName is { } live)
        {
            parts.Add(live);
        }

        if (Problem(_storeStatus, _gapShown) is { } problem)
        {
            parts.Add(problem);
        }

        return string.Join(" · ", parts);
    }

    private string ModeText()
    {
        if (_model.Mode == TimelineMode.Follow)
        {
            return "FOLLOW";
        }

        return _model.NewCount > 0 ? $"PAUSED · {_model.NewCount} new" : "PAUSED";
    }

    /// <summary>
    /// The most important problem of the store, short: the directory error first, since it is current; then the latest read error,
    /// the latest retention gap while it is shown, and the latest failure. The store lists read errors only while they are current.
    /// </summary>
    private static string? Problem(StoreStatus? status, bool showGap)
    {
        if (status is null)
        {
            return null;
        }

        if (status.DirectoryError is { } directory)
        {
            return "directory: " + directory;
        }

        if (status.RecentErrors.Count > 0)
        {
            MemberReadError error = status.RecentErrors[^1];
            return $"{Path.GetFileName(error.Path)}: {error.Message}";
        }

        if (showGap && status.Gaps.Count > 0)
        {
            RetentionGap gap = status.Gaps[^1];
            return $"gap: {gap.Name} lost {gap.LostBytes} bytes";
        }

        return status.RecentFailures.Count > 0 ? status.RecentFailures[^1] : null;
    }
}
