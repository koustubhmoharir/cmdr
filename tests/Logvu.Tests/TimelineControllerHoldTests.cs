using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Logvu.Tui;
using Terminal.Gui.App;

namespace Logvu.Tests;

/// <summary>
/// The held reload through the controller over the app loop: the held page is dimmed only once the hold has lasted the dim delay, a
/// reload that settles sooner is never dimmed, and the dim ends with the hold.
/// </summary>
[Collection("Terminal GUI")]
public sealed class TimelineControllerHoldTests : IDisposable
{
    private const int Rows = 10;

    private readonly LogSet _set = new(0);
    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly FakeTimelineSource _source;
    private readonly PostQueue _posted = new();
    private readonly TimelineController _controller;
    private bool _disposed;

    public TimelineControllerHoldTests()
    {
        _app = Application.Create();
        _app.Init("ansi");
        _app.Driver!.SetScreenSize(60, 10);
        _store = new LogStore(_set.Config);
        _source = new FakeTimelineSource(Enumerable.Range(0, Rows).Select(index => Row(index)));
        _controller = new TimelineController(
            _app,
            _store,
            new LogStoreDetailSource(_store),
            _set.Config,
            _posted.Post,
            _source,
            FakeTimelineSource.CompareKeys,
            _ => null);
        _controller.Start();
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count > 0, "the first rows");
        _controller.Model.Resize(3);
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 3, "three visible rows");
    }

    [Fact]
    public void A_held_page_is_dimmed_only_after_the_delay_and_the_dim_ends_with_the_hold()
    {
        _source.Held.Add(ReadKind.Newest);
        _controller.Model.Reset();
        Assert.True(_controller.Model.IsHolding);
        Assert.False(_controller.Timeline.DimHeld);

        bool? dimmedEarly = null;
        bool? dimmedLater = null;
        bool? holdingAfterRelease = null;
        bool? dimmedAfterRelease = null;
        _app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            dimmedEarly = _controller.Timeline.DimHeld;
            return false;
        });
        _app.AddTimeout(TimeSpan.FromMilliseconds(1000), () =>
        {
            dimmedLater = _controller.Timeline.DimHeld;
            _source.CompleteOldest(ReadKind.Newest);
            _posted.Pump();
            return false;
        });
        _app.AddTimeout(TimeSpan.FromMilliseconds(1100), () =>
        {
            holdingAfterRelease = _controller.Model.IsHolding;
            dimmedAfterRelease = _controller.Timeline.DimHeld;
            _app.RequestStop(_controller.Window);
            return false;
        });

        _app.Run(_controller.Window);

        Assert.False(dimmedEarly);
        Assert.True(dimmedLater);
        Assert.False(holdingAfterRelease);
        Assert.False(dimmedAfterRelease);
    }

    [Fact]
    public void A_reload_that_settles_before_the_delay_is_never_dimmed()
    {
        _source.Held.Add(ReadKind.Newest);
        _controller.Model.Reset();
        Assert.True(_controller.Model.IsHolding);

        _app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            _source.CompleteOldest(ReadKind.Newest);
            _posted.Pump();
            return false;
        });

        bool? holdingLater = null;
        bool? dimmedLater = null;
        _app.AddTimeout(TimeSpan.FromMilliseconds(1000), () =>
        {
            holdingLater = _controller.Model.IsHolding;
            dimmedLater = _controller.Timeline.DimHeld;
            _app.RequestStop(_controller.Window);
            return false;
        });

        _app.Run(_controller.Window);

        Assert.False(holdingLater);
        Assert.False(dimmedLater);
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
        _set.Dispose();
    }

    private static EventRow Row(int index) => new(
        new RowKey(1, index * 100L),
        new EventExtent(0, 0, 0, Provisional: false, Orphan: false),
        new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero).AddSeconds(index),
        null,
        LogLevel.Unknown,
        null,
        $"r{index}",
        IsRaw: false,
        Orphan: false,
        Provisional: false);
}
