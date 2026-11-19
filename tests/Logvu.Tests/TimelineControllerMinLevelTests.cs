using Logvu.Core.Config;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Logvu.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Logvu.Tests;

/// <summary>
/// The minimum level the timeline starts with, from <c>minLevel</c>: the filter is active from the start, the first load already uses it,
/// the status line shows it, and the level and clear keys change it as they do for any filter.
/// </summary>
[Collection("Terminal GUI")]
public sealed class TimelineControllerMinLevelTests
{
    // The level repeats debug, info, warn by index. A minimum of info shows the rows 1, 2, 4, 5, 7 and 8, and the newest three of them
    // are 5, 7 and 8. Without a minimum, or with debug, the newest three are 6, 7 and 8, and row 6 is the first debug row shown.
    private const int Rows = 9;

    [Fact]
    public void Info_minimum_is_the_filter_from_the_start_and_the_status_shows_it()
    {
        using Session session = Start("info");

        Assert.NotNull(session.Controller.Filter);
        Assert.Equal(LogLevel.Info, session.Controller.Filter.MinLevel);
        Assert.Contains("Filter: level ≥ INFO", session.Controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_load_is_filtered_and_no_event_below_the_minimum_is_ever_shown()
    {
        using Session session = Start("info");

        // Every read the model made, the first included, was given the minimum: there was no unfiltered load to take back.
        Assert.NotEmpty(session.Source.Reads);
        Assert.Equal(ReadKind.Newest, session.Source.Reads[0].Kind);
        Assert.All(session.Source.Reads, read => Assert.NotNull(read.Filter));

        Assert.NotEmpty(session.Controller.Model.VisibleRows);
        Assert.All(session.Controller.Model.VisibleRows, row => Assert.True(row.Level >= LogLevel.Info, row.Preview));
        Assert.Equal(Key(8), session.Controller.Model.SelectedRow!.Key);
        Assert.DoesNotContain(session.Controller.Model.VisibleRows, row => row.Level == LogLevel.Debug);
    }

    [Fact]
    public void Minus_lowers_the_minimum_to_debug_and_the_rows_below_it_appear()
    {
        using Session session = Start("info");

        Assert.True(session.Send((Key)'-'));
        session.Posted.PumpUntil(() => session.Controller.Model.VisibleRows.Any(row => row.Level == LogLevel.Debug), "the debug rows");

        Assert.Equal(LogLevel.Debug, session.Controller.Filter!.MinLevel);
        Assert.Contains("Filter: level ≥ DEBUG", session.Controller.StatusLeft, StringComparison.Ordinal);
        Assert.Equal(Key(8), session.Controller.Model.SelectedRow!.Key);
    }

    [Fact]
    public void F_clears_the_minimum_and_every_row_is_shown_again()
    {
        using Session session = Start("info");

        Assert.True(session.Send((Key)'F'));
        session.Posted.PumpUntil(() => session.Controller.Model.VisibleRows.Any(row => row.Level == LogLevel.Debug), "the unfiltered rows");

        Assert.Null(session.Controller.Filter);
        Assert.DoesNotContain("Filter:", session.Controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Any_starts_with_no_filter_and_every_row_is_shown()
    {
        using Session session = Start("any");

        Assert.Null(session.Controller.Filter);
        Assert.DoesNotContain("Filter:", session.Controller.StatusLeft, StringComparison.Ordinal);
        Assert.Equal(Key(8), session.Controller.Model.SelectedRow!.Key);
        Assert.Contains(session.Controller.Model.VisibleRows, row => row.Level == LogLevel.Debug);
    }

    /// <summary>Starts a controller over a log set whose <c>minLevel</c> is <paramref name="minLevel"/>, with rows of three levels.</summary>
    private Session Start(string minLevel)
    {
        var session = new Session(minLevel);
        session.Controller.Start();
        session.Posted.PumpUntil(() => session.Controller.Model.VisibleRows.Count > 0, "the first rows");
        session.Controller.Model.Resize(3);
        session.Posted.PumpUntil(() => session.Controller.Model.VisibleRows.Count == 3, "three visible rows");
        return session;
    }

    private static RowKey Key(int index) => Row(index, LevelOf(index)).Key;

    private static LogLevel LevelOf(int index) => (index % 3) switch
    {
        0 => LogLevel.Debug,
        1 => LogLevel.Info,
        _ => LogLevel.Warn,
    };

    private static EventRow Row(int index, LogLevel level) => new(
        new RowKey(1, index * 100L),
        new EventExtent(0, 0, 0, Provisional: false, Orphan: false),
        new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero).AddSeconds(index),
        null,
        level,
        null,
        $"row {index} {level}",
        IsRaw: false,
        Orphan: false,
        Provisional: false);

    /// <summary>A controller over a log set and a fake source of rows, with its own application, and everything it needs to be disposed.</summary>
    private sealed class Session : IDisposable
    {
        private readonly IApplication _app;
        private readonly LogStore _store;
        private readonly LogSet _set;
        private bool _disposed;

        public Session(string minLevel)
        {
            _set = new LogSet(0, minLevel);
            _app = Application.Create();
            _app.Init("ansi");
            _store = new LogStore(_set.Config);
            Source = new FakeTimelineSource(Enumerable.Range(0, Rows).Select(index => Row(index, LevelOf(index))));
            Controller = new TimelineController(
                _app,
                _store,
                new LogStoreDetailSource(_store),
                _set.Config,
                Posted.Post,
                Source,
                FakeTimelineSource.CompareKeys);
        }

        public PostQueue Posted { get; } = new();

        public FakeTimelineSource Source { get; }

        public TimelineController Controller { get; }

        /// <summary>Sends a key through the window's routing, as the terminal does. Returns whether it was handled.</summary>
        public bool Send(Key key) => Controller.Window.NewKeyDownEvent(key);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Controller.Dispose();
            _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _app.Dispose();
            _set.Dispose();
        }
    }
}
