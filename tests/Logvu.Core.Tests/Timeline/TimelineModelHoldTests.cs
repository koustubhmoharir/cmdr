using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;

namespace Logvu.Core.Tests.Timeline;

/// <summary>
/// The page a reload holds while it is outstanding: the rows the owner keeps showing, released when the reload has rows or has settled
/// with none, and the generation that counts the holds.
/// </summary>
public sealed class TimelineModelHoldTests
{
    [Fact]
    public void A_reload_holds_the_page_it_was_showing_until_the_new_page_is_placed()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        source.Held.Add(ReadKind.Newest);

        model.Reset();

        Assert.True(model.IsHolding);
        Assert.Empty(model.VisibleRows);
        Assert.Equal("r97 r98 [r99]", Shown(model));
        Assert.Equal(2, model.ShownSelectedScreenRow);

        source.CompleteOldest(ReadKind.Newest);

        Assert.False(model.IsHolding);
        Assert.Equal("r97 r98 [r99]", Shown(model));
        Assert.Equal(model.VisibleRows.Select(row => row.Key), model.ShownRows.Select(row => row.Key));
    }

    [Fact]
    public void A_filtered_reload_that_settles_with_no_matches_releases_the_hold()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        source.Held.Add(ReadKind.Newest);

        model.SetFilter(new NoRowFilter());

        Assert.True(model.IsHolding);
        Assert.False(model.NoMatches);
        Assert.Equal("r97 r98 [r99]", Shown(model));

        source.CompleteOldest(ReadKind.Newest);

        Assert.True(model.NoMatches);
        Assert.False(model.IsHolding);
        Assert.Empty(model.ShownRows);
        Assert.Equal(-1, model.ShownSelectedScreenRow);
    }

    [Fact]
    public void A_reload_that_starts_while_holding_keeps_the_held_page_and_the_generation()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        source.Held.Add(ReadKind.Newest);
        model.Reset();
        Assert.Equal(1, model.HoldGeneration);

        model.Reset();

        Assert.Equal(1, model.HoldGeneration);
        Assert.True(model.IsHolding);
        Assert.Equal("r97 r98 [r99]", Shown(model));

        // The first reload's read is superseded, so its result is dropped and the hold stays until the second one settles.
        source.CompleteOldest(ReadKind.Newest);
        Assert.True(model.IsHolding);
        source.CompleteOldest(ReadKind.Newest);
        Assert.False(model.IsHolding);
    }

    [Fact]
    public void HoldGeneration_counts_each_hold_and_a_reload_that_settles_at_once_holds_nothing()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        Assert.Equal(0, model.HoldGeneration);

        model.Reset();
        Assert.False(model.IsHolding);
        Assert.Equal(1, model.HoldGeneration);

        source.Held.Add(ReadKind.Newest);
        model.Reset();
        Assert.True(model.IsHolding);
        Assert.Equal(2, model.HoldGeneration);

        source.CompleteOldest(ReadKind.Newest);
        Assert.False(model.IsHolding);
        Assert.Equal(2, model.HoldGeneration);

        model.Reset();
        Assert.True(model.IsHolding);
        Assert.Equal(3, model.HoldGeneration);
    }

    [Fact]
    public void Without_a_hold_the_shown_rows_are_the_visible_rows()
    {
        var model = Model(Store());
        model.Start(3);
        model.Execute(TimelineCommand.Up);

        Assert.False(model.IsHolding);
        Assert.Equal(model.VisibleRows.Select(row => row.Key), model.ShownRows.Select(row => row.Key));
        Assert.Equal(model.SelectedScreenRow, model.ShownSelectedScreenRow);
    }

    private static EventRow Row(int index) => new(
        new RowKey(1, index * 100L),
        new EventExtent(0, 0, 0, false, false),
        null,
        null,
        LogLevel.Unknown,
        null,
        $"r{index}",
        false,
        false,
        false);

    private static FakeTimelineSource Store(int count = 100) => new(Enumerable.Range(0, count).Select(Row));

    private static TimelineModel Model(FakeTimelineSource source) =>
        new(source, action => action(), FakeTimelineSource.CompareKeys);

    /// <summary>Renders the rows the owner shows, with the selected row in brackets, for example "r7 r8 [r9]".</summary>
    private static string Shown(TimelineModel model) =>
        string.Join(' ', model.ShownRows.Select((row, screenRow) => screenRow == model.ShownSelectedScreenRow ? $"[{row.Preview}]" : row.Preview));

    /// <summary>A filter that keeps no row.</summary>
    private sealed class NoRowFilter : IRowFilter
    {
        public bool Matches(EventRow row, LogEvent ev, string text) => false;
    }
}
