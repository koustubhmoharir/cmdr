using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;

namespace Logvu.Core.Tests.Timeline;

/// <summary>Arrivals, scan state and unseen arrivals of a <see cref="TimelineModel"/> with a filter. The rows of the fake source are filtered by the fake's reads.</summary>
public sealed class FilteredArrivalTests
{
    /// <summary>A filter that matches every row and reports that its pattern timed out, as a filter whose pattern timed out does.</summary>
    private sealed class AbandonedFilter : IRowFilter
    {
        public bool Matches(EventRow row, LogEvent ev, string text) => true;

        public bool Abandoned => true;
    }

    /// <summary>A filter that keeps the rows whose preview satisfies a predicate.</summary>
    private sealed class PreviewFilter(Func<string, bool> keep) : IRowFilter
    {
        public bool Matches(EventRow row, LogEvent ev, string text) => keep(row.Preview);
    }

    [Fact]
    public void Follow_with_a_filter_reads_arrivals_through_the_filter_and_follows_the_newest_match()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        Assert.Equal("r94 r96 [r98]", Render(model));
        Assert.False(model.NoMatches);
        Assert.False(model.IsScanning);

        source.Rows.AddRange([Row(100), Row(101)]);
        model.Arrive([Row(100), Row(101)], []);

        Assert.Equal("r96 r98 [r100]", Render(model));
        Assert.Equal(Key(100), model.SelectedRow!.Key);
        Assert.Equal(TimelineMode.Follow, model.Mode);
        Assert.Equal(0, model.NewCount);
        Assert.False(model.HasUnseenArrivals);
    }

    [Fact]
    public void An_arrival_that_matches_nothing_leaves_the_window_and_the_selection_unchanged()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        string before = Render(model);

        source.Rows.Add(Row(101));
        model.Arrive([Row(101)], []);

        Assert.Equal(before, Render(model));
        Assert.Equal("r94 r96 [r98]", Render(model));
        Assert.Equal(0, model.NewCount);
    }

    [Fact]
    public void Paused_at_the_newest_end_counts_only_the_matching_arrivals_and_keeps_the_viewport()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        model.Execute(TimelineCommand.Up);
        Assert.Equal("r94 [r96] r98", Render(model));

        source.Rows.AddRange([Row(100), Row(101), Row(102), Row(103)]);
        model.Arrive([Row(100), Row(101), Row(102), Row(103)], []);

        Assert.Equal(2, model.NewCount);
        Assert.Equal("r94 [r96] r98", Render(model));
        Assert.True(model.AtEnd);
        Assert.False(model.HasUnseenArrivals);
    }

    [Fact]
    public void Resume_shows_the_matching_arrivals_and_clears_the_count()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        model.Execute(TimelineCommand.Up);
        source.Rows.AddRange([Row(100), Row(101), Row(102)]);
        model.Arrive([Row(100), Row(101), Row(102)], []);
        Assert.Equal(2, model.NewCount);

        model.Execute(TimelineCommand.End);

        Assert.Equal("r98 r100 [r102]", Render(model));
        Assert.Equal(0, model.NewCount);
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    [Fact]
    public void A_burst_larger_than_a_page_is_read_in_pages_and_every_match_is_counted()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        model.Pause();

        // Thirty matching rows, with a page of twelve: the arrival reads continue until the newest end is reached.
        var burst = Enumerable.Range(100, 60).Select(index => Row(index)).ToList();
        source.Rows.AddRange(burst);
        model.Arrive(burst, []);

        // Every match is counted. The window is then trimmed back from the newest end, which the viewport does not need, and a later
        // scroll loads the rows again without counting them twice.
        Assert.Equal(30, model.NewCount);
        Assert.False(model.HasUnseenArrivals);
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void Paused_away_from_the_newest_end_sets_HasUnseenArrivals_without_a_count_and_leaves_the_window()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        model.JumpTo(Key(50));
        Assert.False(model.AtEnd);
        string before = Render(model);

        source.Rows.Add(Row(100));
        model.Arrive([Row(100)], []);

        Assert.Equal(0, model.NewCount);
        Assert.True(model.HasUnseenArrivals);
        Assert.Equal(before, Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void Unseen_arrivals_clear_when_the_window_reaches_the_newest_end_by_a_load_issued_after_them()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        model.JumpTo(Key(50));
        source.Rows.Add(Row(100));
        model.Arrive([Row(100)], []);
        Assert.True(model.HasUnseenArrivals);

        while (!model.AtEnd)
        {
            model.Execute(TimelineCommand.PageDown);
        }

        Assert.False(model.HasUnseenArrivals);
        Assert.Equal(TimelineMode.Paused, model.Mode);

        while (!model.VisibleRows.Any(row => row.Preview == "r100"))
        {
            model.Execute(TimelineCommand.PageDown);
        }

        Assert.False(model.HasUnseenArrivals);
    }

    [Fact]
    public void Resume_clears_HasUnseenArrivals_and_shows_the_newest_matches()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        model.JumpTo(Key(50));
        source.Rows.Add(Row(100));
        model.Arrive([Row(100)], []);

        model.Execute(TimelineCommand.End);

        Assert.False(model.HasUnseenArrivals);
        Assert.Equal("r96 r98 [r100]", Render(model));
    }

    [Fact]
    public void A_loaded_row_that_changed_reloads_the_window_so_that_a_row_that_no_longer_matches_disappears()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new PreviewFilter(preview => !preview.StartsWith("draft", StringComparison.Ordinal)));
        Assert.Equal("r97 r98 [r99]", Render(model));

        source.Rows[99] = Row(99, "draft r99");
        model.Arrive([], [Row(99, "draft r99")]);

        Assert.Equal("r96 r97 [r98]", Render(model));
        Assert.DoesNotContain(model.VisibleRows, row => row.Preview.StartsWith("draft", StringComparison.Ordinal));
    }

    [Fact]
    public void An_updated_row_that_is_not_loaded_does_not_reload_the_window()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        source.Held.Add(ReadKind.Newest);
        source.Held.Add(ReadKind.Newer);
        source.Held.Add(ReadKind.Older);

        model.Arrive([], [Row(5, "changed")]);

        Assert.Equal(0, source.Waiting(ReadKind.Newest));
        Assert.Equal(0, source.Waiting(ReadKind.Newer));
        Assert.Equal(0, source.Waiting(ReadKind.Older));
    }

    [Fact]
    public void A_scan_is_in_progress_until_its_load_completes_and_then_an_empty_window_is_no_matches()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        source.Held.Add(ReadKind.Newest);

        model.SetFilter(new PreviewFilter(_ => false));

        Assert.True(model.IsScanning);
        Assert.False(model.NoMatches);
        Assert.Equal(0, model.ScannedBytes);

        source.Report(ReadKind.Newest, 4096);
        Assert.Equal(4096, model.ScannedBytes);
        Assert.True(model.IsScanning);

        source.CompleteOldest(ReadKind.Newest);

        Assert.False(model.IsScanning);
        Assert.True(model.NoMatches);
        Assert.Equal(0, model.ScannedBytes);
        Assert.Empty(model.VisibleRows);
        Assert.True(model.AtStart);
        Assert.True(model.AtEnd);
    }

    [Fact]
    public void Scanned_bytes_are_the_latest_report_of_the_outstanding_load_and_are_cleared_when_it_completes()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        model.Execute(TimelineCommand.Up);
        source.Held.Add(ReadKind.Older);
        model.Execute(TimelineCommand.PageUp);
        model.Execute(TimelineCommand.PageUp);
        Assert.True(model.IsLoading);

        source.Report(ReadKind.Older, 500);
        source.Report(ReadKind.Older, 900);
        Assert.Equal(900, model.ScannedBytes);

        source.CompleteOldest(ReadKind.Older);
        Assert.Equal(0, model.ScannedBytes);
    }

    [Fact]
    public void A_filtered_window_with_no_matches_takes_the_first_match_as_newest_and_counts_it_when_paused()
    {
        var source = new FakeTimelineSource(Enumerable.Range(0, 100).Select(index => Row(index, "miss")));
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new PreviewFilter(preview => preview.StartsWith("hit", StringComparison.Ordinal)));
        Assert.True(model.NoMatches);
        model.Pause();

        source.Rows.AddRange([Row(100, "hit a"), Row(101, "hit b")]);
        model.Arrive([Row(100, "hit a"), Row(101, "hit b")], []);

        Assert.False(model.NoMatches);
        Assert.Equal(2, model.NewCount);
        Assert.Equal("hit a [hit b]", Render(model));
    }

    [Fact]
    public void Follow_that_is_not_at_the_newest_end_reloads_from_the_newest_end()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        source.Held.Add(ReadKind.Newest);
        model.SetFilter(new EvenRowFilter());
        source.Rows.Add(Row(100));

        model.Arrive([Row(100)], []);
        Assert.False(model.HasUnseenArrivals);

        source.CompleteOldest(ReadKind.Newest);
        Assert.Empty(model.VisibleRows);
        source.CompleteOldest(ReadKind.Newest);
        Assert.Equal("r96 r98 [r100]", Render(model));
        Assert.False(model.HasUnseenArrivals);
    }

    [Fact]
    public void Filter_changes_restart_the_count_of_new_rows()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new EvenRowFilter());
        model.Execute(TimelineCommand.Up);
        source.Rows.Add(Row(100));
        model.Arrive([Row(100)], []);
        Assert.Equal(1, model.NewCount);

        model.SetFilter(new OddRowFilter());

        Assert.Equal(0, model.NewCount);
    }

    [Fact]
    public void Steady_arrivals_under_a_filter_read_only_from_the_provisional_row()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new PreviewFilter(preview => !preview.StartsWith("draft", StringComparison.Ordinal)));
        source.Requests.Clear();

        // Each event finalizes the previous provisional row (an updated row) and adds the next provisional row.
        for (int i = 0; i < 5; i++)
        {
            source.Rows[99 + i] = Row(99 + i, $"final {99 + i}");
            source.Rows.Add(Row(100 + i));
            model.Arrive([Row(100 + i)], [Row(99 + i, $"final {99 + i}")]);
        }

        Assert.NotEmpty(source.Requests);
        Assert.All(source.Requests, request => Assert.Equal(ReadKind.Newer, request.Kind));
        Assert.All(source.Requests, request => Assert.True(request.Anchor.Offset >= Key(99).Offset, "a read reached before the provisional row"));
        Assert.Equal(Key(104), model.SelectedRow!.Key);
        Assert.Equal(0, model.NewCount);
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    [Fact]
    public void A_provisional_row_that_stops_matching_disappears_after_its_update()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new PreviewFilter(preview => !preview.StartsWith("draft", StringComparison.Ordinal)));
        Assert.Equal("r97 r98 [r99]", Render(model));

        source.Rows[99] = Row(99, "draft r99");
        model.Arrive([], [Row(99, "draft r99")]);

        Assert.Equal("r96 r97 [r98]", Render(model));
        Assert.DoesNotContain(model.VisibleRows, row => row.Preview.StartsWith("draft", StringComparison.Ordinal));
        Assert.Equal(Key(98), model.SelectedRow!.Key);
    }

    [Fact]
    public void A_finalized_row_already_counted_while_paused_is_not_counted_again()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new PreviewFilter(_ => true));
        model.Pause();

        source.Rows.Add(Row(100, "provisional"));
        model.Arrive([Row(100, "provisional")], []);
        Assert.Equal(1, model.NewCount);

        source.Rows[100] = Row(100, "final");
        source.Rows.Add(Row(101));
        model.Arrive([Row(101)], [Row(100, "final")]);

        Assert.Equal(2, model.NewCount);
        Assert.True(model.AtEnd);
        Assert.True(model.SelectedRow!.Key == Key(99), "the selection was not on the arrivals, so it stays where it was");
    }

    [Fact]
    public void A_selected_provisional_row_that_is_refilled_while_paused_stays_selected()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new PreviewFilter(_ => true));
        model.Pause();
        Assert.Equal(Key(99), model.SelectedRow!.Key);

        source.Rows[99] = Row(99, "final");
        model.Arrive([], [Row(99, "final")]);

        Assert.Equal(Key(99), model.SelectedRow!.Key);
        Assert.Equal(0, model.NewCount);
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void A_selected_provisional_row_that_stops_matching_falls_to_the_nearest_row_before_it()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.SetFilter(new PreviewFilter(preview => !preview.StartsWith("draft", StringComparison.Ordinal)));
        model.Pause();

        source.Rows[99] = Row(99, "draft r99");
        model.Arrive([], [Row(99, "draft r99")]);

        Assert.Equal(Key(98), model.SelectedRow!.Key);
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void A_filter_whose_pattern_timed_out_reports_FilterTimedOut_until_the_filter_changes()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        Assert.False(model.FilterTimedOut);

        model.SetFilter(new AbandonedFilter());
        Assert.True(model.FilterTimedOut);

        model.SetFilter(null);
        Assert.False(model.FilterTimedOut);
    }

    private static EventRow Row(int index, string? preview = null) => new(
        new RowKey(1, index * 100L),
        new EventExtent(0, 0, 0, false, false),
        null,
        null,
        LogLevel.Unknown,
        null,
        preview ?? $"r{index}",
        false,
        false,
        false);

    private static RowKey Key(int index) => Row(index).Key;

    private static FakeTimelineSource Store(int count = 100) => new(Enumerable.Range(0, count).Select(index => Row(index)));

    private static TimelineModel Model(FakeTimelineSource source) =>
        new(source, action => action(), FakeTimelineSource.CompareKeys);

    /// <summary>Renders the viewport with the selected row in brackets, for example "r7 r8 [r9]".</summary>
    private static string Render(TimelineModel model) =>
        string.Join(' ', model.VisibleRows.Select((row, screenRow) => screenRow == model.SelectedScreenRow ? $"[{row.Preview}]" : row.Preview));
}
