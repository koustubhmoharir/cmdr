using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;

namespace Logvu.Core.Tests.Timeline;

/// <summary>The filter a <see cref="TimelineModel"/> starts with: its first load is already filtered.</summary>
public sealed class TimelineModelStartFilterTests
{
    [Fact]
    public void Start_with_a_filter_loads_only_matching_rows_from_the_first_load()
    {
        var source = new FakeTimelineSource(Enumerable.Range(0, 100).Select(Row));
        var model = Model(source);

        model.Start(3, new EvenRowFilter());

        Assert.Equal("r94 r96 [r98]", Render(model));
        Assert.Equal(ReadKind.Newest, source.Requests.Single().Kind);
    }

    [Fact]
    public void Start_without_a_filter_loads_every_row()
    {
        var model = Model(new FakeTimelineSource(Enumerable.Range(0, 100).Select(Row)));

        model.Start(3);

        Assert.Equal("r97 r98 [r99]", Render(model));
    }

    [Fact]
    public void A_filter_given_to_start_is_replaced_by_set_filter_null()
    {
        var model = Model(new FakeTimelineSource(Enumerable.Range(0, 100).Select(Row)));
        model.Start(3, new EvenRowFilter());

        model.SetFilter(null);

        Assert.Equal("r97 r98 [r99]", Render(model));
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

    private static TimelineModel Model(FakeTimelineSource source) =>
        new(source, action => action(), FakeTimelineSource.CompareKeys);

    /// <summary>Renders the viewport with the selected row in brackets, for example "r7 r8 [r9]".</summary>
    private static string Render(TimelineModel model) =>
        string.Join(' ', model.VisibleRows.Select((row, screenRow) => screenRow == model.SelectedScreenRow ? $"[{row.Preview}]" : row.Preview));
}
