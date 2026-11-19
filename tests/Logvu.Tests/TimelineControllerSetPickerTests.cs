using Logvu.Core.Store;
using Logvu.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Logvu.Tests;

/// <summary>
/// Ctrl+O through the controller with a fake picker: a chosen config becomes <c>SwitchTo</c> and stops the window, Esc leaves the window
/// as it was (the detail and the filter stay), and Ctrl+Q stops the window with no switch. Ctrl+O also works with a detail open.
/// </summary>
[Collection("Terminal GUI")]
public sealed class TimelineControllerSetPickerTests : IDisposable
{
    private readonly LogSet _set = new(100);
    private readonly LogSet _next = new(5);
    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly HeldDetailSource _details;
    private readonly PostQueue _posted = new();
    private readonly TimelineController _controller;
    private (TimelineController.PickOutcome Outcome, Logvu.Core.Config.LogvuConfig? Config) _answer;
    private int _picks;
    private bool _disposed;

    public TimelineControllerSetPickerTests()
    {
        _app = Application.Create();
        _app.Init("ansi");
        _store = new LogStore(_set.Config);
        _details = new HeldDetailSource(new LogStoreDetailSource(_store));
        _controller = new TimelineController(_app, _store, _details, _set.Config, _posted.Post, pickSet: Pick);
        _controller.Start();
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count > 0, "the first rows");
        _controller.Model.Resize(3);
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 3, "three visible rows");
    }

    [Fact]
    public void Ctrl_O_calls_the_picker_once_and_is_handled()
    {
        _answer = (TimelineController.PickOutcome.Back, null);

        Assert.True(Send(Key.O.WithCtrl));

        Assert.Equal(1, _picks);
        Assert.Null(_controller.SwitchTo);
    }

    [Fact]
    public void A_chosen_config_becomes_SwitchTo_and_stops_the_window()
    {
        _answer = (TimelineController.PickOutcome.Chosen, _next.Config);

        Assert.True(RunWindow(Key.O.WithCtrl));

        Assert.Same(_next.Config, _controller.SwitchTo);
        Assert.Equal(1, _picks);
    }

    [Fact]
    public void Back_changes_nothing_and_keeps_the_detail_and_the_filter_open()
    {
        Send((Key)'=');
        var filter = _controller.Filter;
        Assert.NotNull(filter);
        _posted.PumpUntil(() => _controller.Model.SelectedRow is not null, "a selected row after the filter");
        Assert.True(_controller.Timeline.HandleKey(Key.Enter));
        Assert.Same(_controller.Detail, _controller.Window.Content);

        _answer = (TimelineController.PickOutcome.Back, null);

        Assert.False(RunWindow(Key.O.WithCtrl));

        Assert.Equal(1, _picks);
        Assert.Null(_controller.SwitchTo);
        Assert.Same(_controller.Detail, _controller.Window.Content);
        Assert.Same(filter, _controller.Filter);
    }

    [Fact]
    public void Quit_stops_the_window_with_no_switch()
    {
        _answer = (TimelineController.PickOutcome.Quit, null);

        Assert.True(RunWindow(Key.O.WithCtrl));

        Assert.Null(_controller.SwitchTo);
        Assert.Equal(1, _picks);
    }

    [Fact]
    public void Ctrl_O_works_while_a_detail_is_open()
    {
        Assert.True(_controller.Timeline.HandleKey(Key.Enter));
        Assert.Same(_controller.Detail, _controller.Window.Content);
        _answer = (TimelineController.PickOutcome.Chosen, _next.Config);

        Assert.True(RunWindow(Key.O.WithCtrl));

        Assert.Same(_next.Config, _controller.SwitchTo);
        Assert.Equal(1, _picks);
    }

    [Fact]
    public void The_status_hints_name_the_open_key()
    {
        Assert.Contains("  ^O open  ", TimelineController.Hints);
        Assert.Contains("  ^O open  ", TimelineController.SelectionHints);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.Dispose();
        _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _app.Dispose();
        _next.Dispose();
        _set.Dispose();
    }

    private (TimelineController.PickOutcome Outcome, Logvu.Core.Config.LogvuConfig? Config) Pick()
    {
        _picks++;
        return _answer;
    }

    /// <summary>Sends a key through the window's routing: the focused view first, then the window. Returns whether it was handled.</summary>
    private bool Send(Key key) => _controller.Window.NewKeyDownEvent(key);

    /// <summary>
    /// Runs the window in the application's loop and sends the key on its first iteration. Returns true when the window stopped by
    /// itself; the loop is bounded, and a window that does not stop is stopped by the bound and the result is false.
    /// </summary>
    private bool RunWindow(Key key)
    {
        const int MaxIterations = 40;
        int iteration = 0;
        bool stoppedItself = true;
        EventHandler<EventArgs<IApplication?>> handler = (_, _) =>
        {
            iteration++;
            if (iteration == 1)
            {
                Send(key);
            }

            if (iteration < MaxIterations)
            {
                return;
            }

            stoppedItself = false;
            _app.RequestStop();
            _app.RequestStop(_controller.Window);
        };

        _app.Iteration += handler;
        _app.Run(_controller.Window);
        _app.Iteration -= handler;
        return stoppedItself;
    }
}
