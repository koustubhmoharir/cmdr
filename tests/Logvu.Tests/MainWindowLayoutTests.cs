using Logvu.Core.Store;
using Logvu.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Logvu.Tests;

/// <summary>
/// The window's rows as drawn: the status bar on the first row, the timeline's header below it, the key hints on the last row, and the search
/// bar directly above the hints while a search is open, which takes a row from the content.
/// </summary>
[Collection("Terminal GUI")]
public sealed class MainWindowLayoutTests : IDisposable
{
    private const int Width = 120;
    private const int Height = 10;

    private readonly LogSet _set = new(100);
    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly HeldDetailSource _details;
    private readonly PostQueue _posted = new();
    private readonly TimelineController _controller;
    private readonly List<string> _written = [];
    private bool _disposed;

    public MainWindowLayoutTests()
    {
        _app = Application.Create();
        _app.Init("ansi");
        _app.Driver!.SetScreenSize(Width, Height);
        _store = new LogStore(_set.Config);
        _details = new HeldDetailSource(new LogStoreDetailSource(_store));
        _controller = new TimelineController(_app, _store, _details, _set.Config, _posted.Post);
        _controller.WriteRaw = _written.Add;
        _controller.Start();
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count > 0, "the first rows");
    }

    [Fact]
    public void The_status_text_is_on_the_first_row_and_the_key_hints_on_the_last()
    {
        TerminalScreen screen = Run()[0];

        Assert.Equal(_controller.StatusLeft, screen.Text(0));
        Assert.Equal(_controller.StatusHints, screen.Text(Height - 1));
        Assert.StartsWith("f filter", screen.Text(Height - 1), StringComparison.Ordinal);
    }

    [Fact]
    public void The_timeline_header_is_on_the_second_row_and_the_rows_fill_the_rows_between()
    {
        // The first step loads the rows for the laid-out height; the newest event is on the last row above the hints.
        TerminalScreen screen = Run(() => _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == Height - 3, "the rows"))[^1];

        Assert.Contains("Message", screen.Text(1), StringComparison.Ordinal);
        Assert.Contains("Level", screen.Text(1), StringComparison.Ordinal);
        Assert.Contains("r99", screen.Text(Height - 2), StringComparison.Ordinal);
        Assert.Equal(Height - 2, _controller.Timeline.Viewport.Height);
    }

    [Fact]
    public void The_status_bar_is_inverted_like_the_hints()
    {
        TerminalScreen screen = Run()[0];

        Assert.Equal(screen.Normal.Background, screen.Cells[0][0].Attribute.Foreground);
        Assert.Equal(screen.Normal.Background, screen.Cells[Height - 1][0].Attribute.Foreground);
    }

    [Fact]
    public void With_the_search_bar_open_it_is_on_the_row_above_the_hints_and_the_content_shrinks_by_one()
    {
        List<TerminalScreen> screens = Run(
            () => _controller.Window.NewKeyDownEvent(Key.F.WithCtrl),
            () => _posted.Pump());

        TerminalScreen open = screens[1];

        Assert.StartsWith("Find:", open.Text(Height - 2), StringComparison.Ordinal);
        Assert.StartsWith("f filter", open.Text(Height - 1), StringComparison.Ordinal);
        Assert.Contains("Message", open.Text(1), StringComparison.Ordinal);
        Assert.Equal(Height - 3, _controller.Timeline.Viewport.Height);
    }

    [Fact]
    public void A_notice_is_shown_on_the_status_bar_and_not_on_the_key_hints()
    {
        _details.Hold = false;

        List<TerminalScreen> screens = Run(
            () => _controller.Timeline.HandleKey(Key.C),
            () => _posted.PumpUntil(() => _controller.StatusLeft.Contains("Copied", StringComparison.Ordinal), "the copy"));

        TerminalScreen copied = screens[^1];
        Assert.Contains("Copied", copied.Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("Copied", copied.Text(Height - 1), StringComparison.Ordinal);
        Assert.Equal(_controller.StatusHints, copied.Text(Height - 1));
    }

    [Fact]
    public void The_detail_shows_its_status_on_the_first_row_and_its_hints_on_the_last()
    {
        _details.Hold = false;

        List<TerminalScreen> screens = Run(
            () => _controller.Timeline.HandleKey(Key.Enter),
            () => _posted.PumpUntil(() => _controller.Detail!.Document is not null, "the detail"));

        TerminalScreen detail = screens[^1];
        Assert.Equal("DETAIL · app.log", detail.Text(0));
        Assert.StartsWith("f filter on value", detail.Text(Height - 1), StringComparison.Ordinal);
        Assert.Equal(Height - 2, _controller.Detail!.Viewport.Height);
    }

    /// <summary>Runs the controller's window at the test size, with the steps after the first draw, and returns the screens.</summary>
    private List<TerminalScreen> Run(params Action[] steps) => TerminalHarness.RunWindow(_app, _controller.Window, Width, Height, steps);

    /// <inheritdoc/>
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
        _set.Dispose();
    }
}
