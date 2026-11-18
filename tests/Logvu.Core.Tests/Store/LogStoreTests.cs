using System.Diagnostics;
using System.Globalization;
using System.Text;
using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Shared.Core;
using static Logvu.Core.Tests.Store.StoreFixtures;

namespace Logvu.Core.Tests.Store;

/// <summary>
/// The threaded store over real directories, with short intervals: its updates, its reads checked against an independent framing of the
/// files, rotation, concurrent reads during rotation, and shutdown. The collection keeps these timing-sensitive tests from running beside
/// each other.
/// </summary>
[Collection("LogStore timing")]
public sealed class LogStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);

    private static readonly CancellationToken NoCancel = CancellationToken.None;

    [Fact]
    public async Task The_initial_update_holds_the_live_row_and_the_newest_reads_cross_the_file_boundary()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft2.log", Events(T0, "two", 3));
        dir.Write("SheetKraft1.log", Events(T0.AddHours(1), "one", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(2), "live", 6));
        await using Harness h = Harness.Create(dir);
        h.Start();

        StoreUpdate initial = await h.WaitFor(u => u.Batch.Initial);

        Assert.Single(initial.Batch.Added);
        Assert.True(initial.Batch.Added[0].Provisional);
        Assert.Equal("live event 5", initial.Batch.Added[0].Preview);
        List<RowKey> expected = TimelineKeys(h.Store.Snapshot);
        Assert.Equal(12, expected.Count);

        RowPage newest = await h.Store.ReadNewestAsync(5, null, null, NoCancel);
        Assert.Equal(expected.TakeLast(5), newest.Rows.Select(row => row.Key));
        Assert.True(newest.Rows[^1].Provisional);
        Assert.False(newest.ReachedBoundary);

        // The oldest of the five is in the live file, so the next page crosses into SheetKraft1.log.
        RowKey oldest = newest.Rows[0].Key;
        RowPage older = await h.Store.ReadOlderAsync(oldest, 4, null, null, NoCancel);
        int index = expected.IndexOf(oldest);
        Assert.Equal(expected.Skip(index - 4).Take(4), older.Rows.Select(row => row.Key));
        Assert.Contains(older.Rows, row => row.Key.Generation != oldest.Generation);

        RowPage everything = await h.Store.ReadOlderAsync(oldest, 100, null, null, NoCancel);
        Assert.Equal(expected.Take(index), everything.Rows.Select(row => row.Key));
        Assert.True(everything.ReachedBoundary);
    }

    [Fact]
    public async Task Appended_events_arrive_as_added_rows_and_the_provisional_row_is_completed_and_grows()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft1.log", Events(T0, "one", 2));
        dir.Write("SheetKraft.log", Events(T0.AddHours(1), "live", 2));
        await using Harness h = Harness.Create(dir);
        h.Start();
        StoreUpdate initial = await h.WaitFor(u => u.Batch.Initial);
        RowKey previous = initial.Batch.Added[0].Key;

        int mark = h.Count;
        AppendText(dir, "SheetKraft.log",
            Line(T0.AddHours(1).AddMinutes(1), "INFO", "appended one")
            + Line(T0.AddHours(1).AddMinutes(2), "ERROR", "appended two")
            + "   at Growing.Frame() in C:\\src\\Growing.cs:line 1\n");

        // A reader may see the append in pieces, so the checks are on the final state of each row, not on the update that carried it.
        StoreUpdate arrived = await h.WaitFor(u => u.Batch.Added.Any(row => row.Preview == "appended two"), mark);
        IReadOnlyList<StoreUpdate> since = h.Since(mark);
        Assert.Contains(since.SelectMany(u => u.Batch.Updated), row => row.Key == previous && !row.Provisional);
        Assert.Contains(since.SelectMany(u => u.Batch.Added.Concat(u.Batch.Updated)), row => row.Preview == "appended one" && !row.Provisional);
        Assert.Contains(arrived.Batch.Added, row => row.Preview == "appended two" && row.Provisional);

        // The provisional row grows in place: its extent is updated, its key is not new. It is complete when its extent ends at the file end.
        mark = h.Count;
        AppendText(dir, "SheetKraft.log", "   at Growing.Second() in C:\\src\\Growing.cs:line 2\n");
        long fileLength = new FileInfo(dir.Full("SheetKraft.log")).Length;
        StoreUpdate grown = await h.WaitFor(
            u => u.Batch.Updated.Any(row => row.Preview == "appended two" && row.Extent.End == fileLength), mark);
        Assert.Empty(grown.Batch.Added);
        RowPage newest = await h.Store.ReadNewestAsync(3, null, null, NoCancel);
        Assert.Equal(new[] { "live event 1", "appended one", "appended two" }, newest.Rows.Select(row => row.Preview));
        Assert.True(newest.Rows[^1].Provisional);
        Assert.True(newest.Rows[^1].Extent.End > newest.Rows[^2].Extent.End);
        RowKey last = newest.Rows[^1].Key;
        Assert.Equal(last, grown.Batch.Updated.Single(row => row.Preview == "appended two").Key);
        Assert.Equal(fileLength, newest.Rows[^1].Extent.End);
    }

    [Fact]
    public async Task Numbered_rotation_expires_the_deleted_member_and_keeps_detail_and_history_reads_exact()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft2.log", Events(T0, "two", 4));
        dir.Write("SheetKraft1.log", Events(T0.AddHours(1), "one", 4));
        dir.Write("SheetKraft.log", Events(T0.AddHours(2), "live", 4));
        await using Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);

        RowPage before = await h.Store.ReadNewestAsync(100, null, null, NoCancel);
        int genA = Generation(h, "SheetKraft2.log");
        int genB = Generation(h, "SheetKraft1.log");
        int genC = Generation(h, "SheetKraft.log");
        RowKey fromDeleted = before.Rows.First(row => row.Key.Generation == genA).Key;
        RowKey fromRenamed = before.Rows.Last(row => row.Key.Generation == genB && row.Level == LogLevel.Error).Key;

        int mark = h.Count;
        File.Delete(dir.Full("SheetKraft2.log"));
        File.Move(dir.Full("SheetKraft1.log"), dir.Full("SheetKraft2.log"));
        File.Move(dir.Full("SheetKraft.log"), dir.Full("SheetKraft1.log"));
        dir.Write("SheetKraft.log", Events(T0.AddHours(3), "new", 2));

        StoreUpdate rotated = await h.WaitFor(u => u.Batch.Expired.Contains(genA), mark);
        Assert.Equal(new[] { genA }, rotated.Batch.Expired);
        await h.WaitFor(u => u.Batch.Added.Any(row => row.Preview == "new event 1"), mark);
        int genD = Generation(h, "SheetKraft.log");
        Assert.NotEqual(genC, genD);

        RowPage newest = await h.Store.ReadNewestAsync(100, null, null, NoCancel);
        RowKey firstOfNew = newest.Rows.First(row => row.Key.Generation == genD).Key;
        // Reading back from the new generation crosses the renamed members. The expired member's rows stay in the timeline while the
        // ring holds them, so they come first, as they were before the rotation.
        RowPage older = await h.Store.ReadOlderAsync(firstOfNew, 100, null, null, NoCancel);
        List<RowKey> expectedOlder = before.Rows.Where(row => row.Key.Generation == genA).Select(row => row.Key)
            .Concat(TimelineKeys(h.Store.Snapshot).Where(key => key.Generation == genB || key.Generation == genC))
            .ToList();
        Assert.Equal(expectedOlder, older.Rows.Select(row => row.Key));

        // Reading back from an expired row finds nothing older: it is the oldest member of the timeline.
        RowPage beforeExpired = await h.Store.ReadOlderAsync(fromDeleted, 10, null, null, NoCancel);
        Assert.Empty(beforeExpired.Rows);
        Assert.True(beforeExpired.ReachedBoundary);

        EventDetail gone = await h.Store.ReadDetailAsync(fromDeleted, NoCancel);
        var unavailable = Assert.IsType<EventDetail.Unavailable>(gone);
        Assert.Equal("Source file expired", unavailable.Reason);

        EventDetail moved = await h.Store.ReadDetailAsync(fromRenamed, NoCancel);
        var loaded = Assert.IsType<EventDetail.Loaded>(moved);
        Assert.Equal(dir.Full("SheetKraft2.log"), loaded.Path);
        Assert.Equal(fromRenamed.Offset, loaded.Offset);
        Assert.Equal(MultiLineText(Events(T0.AddHours(1), "one", 4)), loaded.Text);
        Assert.Equal(File.ReadAllBytes(dir.Full("SheetKraft2.log")).Length - loaded.Offset, loaded.Length);
        Assert.Equal(fromRenamed, loaded.Row.Key);
    }

    [Fact]
    public async Task Detail_of_a_multi_line_exception_returns_the_full_text_with_its_path_offset_and_length()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "live", 4));
        await using Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);

        RowPage page = await h.Store.ReadNewestAsync(4, null, null, NoCancel);
        RowKey key = page.Rows[^1].Key;
        Assert.Equal(LogLevel.Error, page.Rows[^1].Level);
        Assert.True(page.Rows[^1].Provisional);

        EventDetail detail = await h.Store.ReadDetailAsync(key, NoCancel);

        var loaded = Assert.IsType<EventDetail.Loaded>(detail);
        string expected = MultiLineText(Events(T0, "live", 4));
        Assert.Equal(expected, loaded.Text);
        Assert.False(loaded.Truncated);
        Assert.Equal(dir.Full("SheetKraft.log"), loaded.Path);
        Assert.Equal(key.Offset, loaded.Offset);
        // The length is the event's bytes in the file, its final line terminator included; the text drops that terminator.
        Assert.Equal(File.ReadAllBytes(dir.Full("SheetKraft.log")).Length - key.Offset, loaded.Length);
        Assert.Equal(LogLevel.Error, loaded.Event.Level);
        Assert.Contains("live failed 3", loaded.Event.Message, StringComparison.Ordinal);
        Assert.Contains("at SheetKraft.Engine.Runner.Execute", loaded.Text, StringComparison.Ordinal);
        Assert.Equal(key, loaded.Row.Key);
    }

    [Fact]
    public async Task A_filtered_newest_read_includes_the_provisional_error_row()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft1.log", Events(T0, "one", 4));
        dir.Write("SheetKraft.log", Events(T0.AddHours(1), "live", 4));
        await using Harness h = Harness.Create(dir);
        h.Start();
        StoreUpdate initial = await h.WaitFor(u => u.Batch.Initial);
        RowKey provisional = initial.Batch.Added.Single().Key;

        RowPage errors = await h.Store.ReadNewestAsync(100, new LevelFilter(LogLevel.Error), null, NoCancel);

        Assert.Equal(provisional, errors.Rows[^1].Key);
        Assert.True(errors.Rows[^1].Provisional);
        HashSet<RowKey> errorKeys = ErrorKeys(h.Store.Snapshot);
        List<RowKey> expected = TimelineKeys(h.Store.Snapshot).Where(errorKeys.Contains).ToList();
        Assert.Equal(expected, errors.Rows.Select(row => row.Key));
        Assert.True(errors.ReachedBoundary);
    }

    [Fact]
    public async Task Reads_running_during_repeated_rotations_never_fail_and_return_rows_whose_detail_matches()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "start", 3));
        await using Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);
        LogStore store = h.Store;

        using var stop = new CancellationTokenSource();
        Task[] readers = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            int checkedDetails = 0;
            while (!stop.IsCancellationRequested)
            {
                RowPage page = await store.ReadNewestAsync(40, null, null, NoCancel);
                if (page.Rows.Count == 0)
                {
                    continue;
                }

                RowPage older = await store.ReadOlderAsync(page.Rows[0].Key, 40, null, null, NoCancel);
                Assert.True(older.Rows.Count <= 40);
                // From the newest row back: the live rows are the ones whose detail is loaded.
                for (int i = page.Rows.Count - 1; i >= 0; i -= 5)
                {
                    EventRow row = page.Rows[i];
                    EventDetail detail = await store.ReadDetailAsync(row.Key, NoCancel);
                    if (detail is EventDetail.Loaded loaded)
                    {
                        Assert.Equal(row.Key, loaded.Row.Key);
                        Assert.Equal(row.Preview, loaded.Row.Preview);
                        checkedDetails++;
                    }
                }
            }

            Assert.True(checkedDetails > 0);
        })).ToArray();

        int round = 1;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(2))
        {
            Rotate(dir, round);
            AppendText(dir, "SheetKraft.log", Line(T0.AddMinutes(round).AddSeconds(30), "INFO", $"rot{round} late"));
            round++;
            await Task.Delay(30);
        }

        await h.WaitFor(u => u.Batch.Added.Any(row => row.Preview == $"rot{round - 1} late"));
        await stop.CancelAsync();
        await Task.WhenAll(readers);
        Assert.Empty(h.Latest.Status.RecentFailures);
    }

    [Fact]
    public async Task A_throwing_update_handler_does_not_stop_later_updates()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "live", 2));
        await using Harness h = Harness.Create(dir, first: _ => throw new InvalidOperationException("handler broke"));
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);

        int mark = h.Count;
        AppendText(dir, "SheetKraft.log", Line(T0.AddMinutes(5), "INFO", "after the handler failed"));
        await h.WaitFor(u => u.Batch.Added.Any(row => row.Preview == "after the handler failed"), mark);

        mark = h.Count;
        AppendText(dir, "SheetKraft.log", Line(T0.AddMinutes(6), "INFO", "and again"));
        StoreUpdate later = await h.WaitFor(u => u.Batch.Added.Any(row => row.Preview == "and again"), mark);
        Assert.Contains(later.Status.RecentFailures, failure => failure.Contains("handler broke", StringComparison.Ordinal));
        Assert.Single(later.Status.RecentFailures);
    }

    [Fact]
    public async Task An_inclusive_newer_read_starts_with_the_anchor_whether_it_is_in_history_or_in_the_ring()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft2.log", Events(T0, "two", 3));
        dir.Write("SheetKraft1.log", Events(T0.AddHours(1), "one", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(2), "live", 6));
        await using Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);
        List<RowKey> expected = TimelineKeys(h.Store.Snapshot);

        foreach (int index in new[] { 0, 3, 7, 11 })
        {
            RowPage inclusive = await h.Store.ReadNewerAsync(expected[index], 100, null, null, NoCancel, inclusive: true);
            Assert.Equal(expected.Skip(index), inclusive.Rows.Select(row => row.Key));
            Assert.True(inclusive.ReachedBoundary);

            RowPage exclusive = await h.Store.ReadNewerAsync(expected[index], 100, null, null, NoCancel);
            Assert.Equal(expected.Skip(index + 1), exclusive.Rows.Select(row => row.Key));
        }
    }

    [Fact]
    public async Task An_inclusive_newer_read_with_a_filter_leaves_out_an_anchor_that_the_filter_rejects()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft2.log", Events(T0, "two", 3));
        dir.Write("SheetKraft1.log", Events(T0.AddHours(1), "one", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(2), "live", 6));
        await using Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);
        List<RowKey> expected = TimelineKeys(h.Store.Snapshot);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);
        RowKey anchor = expected[4];

        RowPage rejected = await h.Store.ReadNewerAsync(anchor, 100, new ExceptPreview(all.Rows[4].Preview), null, NoCancel, inclusive: true);
        Assert.Equal(expected.Skip(5), rejected.Rows.Select(row => row.Key));

        RowPage accepted = await h.Store.ReadNewerAsync(anchor, 100, new ExceptPreview(all.Rows[9].Preview), null, NoCancel, inclusive: true);
        Assert.Equal(expected.Skip(4).Where((_, i) => i != 5), accepted.Rows.Select(row => row.Key));
    }

    [Fact]
    public async Task Find_older_from_the_newest_row_finds_the_nearest_older_match_across_a_file_boundary()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = await StartSearchSet(dir);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);
        RowKey newest = all.Rows[^1].Key;

        // The newest row is the live file's provisional event. The nearest older "one event" is the last event of SheetKraft1.log.
        SearchResult found = await h.Store.FindAsync(newest, SearchDirection.Older, Compile("one event"), null, null, NoCancel);

        Assert.True(found.IsFound);
        Assert.Equal(KeyOf(all, "one event 3"), found.Row!.Key);
        Assert.Equal("one event 3", found.Row.Preview);
        Assert.NotEqual(newest.Generation, found.Row.Key.Generation);
        Assert.Empty(found.Errors);

        // Within the live file, the nearest older match is in the same file.
        SearchResult sameFile = await h.Store.FindAsync(newest, SearchDirection.Older, Compile("live event 1"), null, null, NoCancel);
        Assert.Equal("live event 1", sameFile.Row!.Preview);
        Assert.Equal(newest.Generation, sameFile.Row.Key.Generation);
    }

    [Fact]
    public async Task Find_newer_from_an_old_row_finds_the_next_newer_match_across_a_file_boundary()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = await StartSearchSet(dir);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);

        SearchResult next = await h.Store.FindAsync(KeyOf(all, "two event 1"), SearchDirection.Newer, Compile("event"), null, null, NoCancel);
        Assert.Equal(KeyOf(all, "two event 2"), next.Row!.Key);

        SearchResult across = await h.Store.FindAsync(KeyOf(all, "two event 2"), SearchDirection.Newer, Compile("one event"), null, null, NoCancel);
        Assert.Equal(KeyOf(all, "one event 0"), across.Row!.Key);

        SearchResult again = await h.Store.FindAsync(KeyOf(all, "one event 0"), SearchDirection.Newer, Compile("event"), null, null, NoCancel);
        Assert.Equal(KeyOf(all, "one event 1"), again.Row!.Key);
    }

    [Fact]
    public async Task Find_matches_text_that_is_only_in_a_stack_trace_continuation_line()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = await StartSearchSet(dir);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);

        // "one failed 3" is only in the continuation line of the ERROR event "one event 3", not in its header.
        SearchResult found = await h.Store.FindAsync(all.Rows[^1].Key, SearchDirection.Older, Compile("one failed 3"), null, null, NoCancel);

        Assert.Equal(KeyOf(all, "one event 3"), found.Row!.Key);
        Assert.Equal(LogLevel.Error, found.Row.Level);
        Assert.DoesNotContain("failed", found.Row.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_matches_a_json_event_by_its_rendered_message_when_the_template_is_not_in_the_text()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft1.log", SerilogLine(41) + SerilogLine(42) + SerilogLine(43));
        dir.Write("SheetKraft.log", SerilogLine(44) + SerilogLine(45));
        await using Harness h = Harness.Create(dir, format: "serilog-json");
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);

        // The raw text holds "Order {OrderId}" and "OrderId":42, never "Order 42".
        SearchResult found = await h.Store.FindAsync(all.Rows[^1].Key, SearchDirection.Older, Compile("Order 42"), null, null, NoCancel);

        Assert.Equal(KeyOf(all, "Order 42"), found.Row!.Key);
        Assert.Equal("Order 42", found.Row.Preview);
    }

    [Fact]
    public async Task Find_is_case_insensitive_by_default_and_case_sensitive_on_request()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = await StartSearchSet(dir);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);
        RowKey newest = all.Rows[^1].Key;

        SearchResult insensitive = await h.Store.FindAsync(newest, SearchDirection.Older, Compile("ONE EVENT 2"), null, null, NoCancel);
        Assert.Equal(KeyOf(all, "one event 2"), insensitive.Row!.Key);

        SearchResult sensitive = await h.Store.FindAsync(newest, SearchDirection.Older, Compile("ONE EVENT 2", caseSensitive: true), null, null, NoCancel);
        Assert.False(sensitive.IsFound);
    }

    [Fact]
    public async Task Find_with_a_regex_matches_the_pattern_against_the_text()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = await StartSearchSet(dir);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);

        // The header of an ERROR event does not end its text, so only events whose text ends with "event N" match.
        SearchPattern regex = Compile("event [13]$", regex: true);
        SearchResult first = await h.Store.FindAsync(KeyOf(all, "two event 0"), SearchDirection.Newer, regex, null, null, NoCancel);
        Assert.Equal(KeyOf(all, "two event 1"), first.Row!.Key);

        SearchResult second = await h.Store.FindAsync(first.Row.Key, SearchDirection.Newer, regex, null, null, NoCancel);
        Assert.Equal(KeyOf(all, "one event 1"), second.Row!.Key);
    }

    [Fact]
    public async Task Find_with_whole_word_skips_matches_inside_a_word()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = await StartSearchSet(dir);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);
        RowKey newest = all.Rows[^1].Key;

        SearchResult inside = await h.Store.FindAsync(newest, SearchDirection.Older, Compile("vent", wholeWord: true), null, null, NoCancel);
        Assert.False(inside.IsFound);

        SearchResult literal = await h.Store.FindAsync(newest, SearchDirection.Older, Compile("vent"), null, null, NoCancel);
        Assert.True(literal.IsFound);

        SearchResult word = await h.Store.FindAsync(newest, SearchDirection.Older, Compile("event", wholeWord: true), null, null, NoCancel);
        Assert.True(word.IsFound);
    }

    [Fact]
    public async Task Find_reports_the_boundary_when_no_further_match_exists_in_either_direction()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = await StartSearchSet(dir);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);

        SearchResult older = await h.Store.FindAsync(KeyOf(all, "two event 0"), SearchDirection.Older, Compile("event"), null, null, NoCancel);
        Assert.False(older.IsFound);
        Assert.Null(older.Row);

        SearchResult newer = await h.Store.FindAsync(all.Rows[^1].Key, SearchDirection.Newer, Compile("event"), null, null, NoCancel);
        Assert.False(newer.IsFound);
        Assert.Null(newer.Row);
    }

    [Fact]
    public async Task Find_finds_the_live_provisional_row_when_it_is_the_next_match_or_the_only_one()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = await StartSearchSet(dir);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);
        RowKey provisional = all.Rows[^1].Key;
        Assert.True(all.Rows[^1].Provisional);

        // The row before the provisional one: its next newer match is the provisional row itself.
        SearchResult next = await h.Store.FindAsync(KeyOf(all, "live event 4"), SearchDirection.Newer, Compile("live event 5"), null, null, NoCancel);
        Assert.Equal(provisional, next.Row!.Key);
        Assert.True(next.Row.Provisional);

        // "live event 5" exists only in the provisional row, so it is the only match from the oldest row.
        SearchResult only = await h.Store.FindAsync(all.Rows[0].Key, SearchDirection.Newer, Compile("live event 5"), null, null, NoCancel);
        Assert.Equal(provisional, only.Row!.Key);

        SearchResult beforeIt = await h.Store.FindAsync(provisional, SearchDirection.Older, Compile("live event 5"), null, null, NoCancel);
        Assert.False(beforeIt.IsFound);
    }

    [Fact]
    public async Task Find_applies_the_filter_as_well_as_the_pattern()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = await StartSearchSet(dir);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);
        RowKey newest = all.Rows[^1].Key;
        var errorsOnly = new LevelFilter(LogLevel.Error);

        SearchResult info = await h.Store.FindAsync(newest, SearchDirection.Older, Compile("live event 4"), null, null, NoCancel);
        Assert.Equal("live event 4", info.Row!.Preview);

        SearchResult excluded = await h.Store.FindAsync(newest, SearchDirection.Older, Compile("live event 4"), errorsOnly, null, NoCancel);
        Assert.False(excluded.IsFound);

        SearchResult error = await h.Store.FindAsync(newest, SearchDirection.Older, Compile("event"), errorsOnly, null, NoCancel);
        Assert.Equal("live event 3", error.Row!.Preview);
        Assert.Equal(LogLevel.Error, error.Row.Level);
    }

    [Fact]
    public async Task Find_throws_when_cancelled_before_it_starts()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = await StartSearchSet(dir);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Store.FindAsync(all.Rows[^1].Key, SearchDirection.Older, Compile("event"), null, null, cancelled.Token));
    }

    [Fact]
    public async Task Find_reports_the_bytes_it_scans_in_history()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft1.log", Events(T0, "bulk", 2000));
        dir.Write("SheetKraft.log", Events(T1, "live", 2));
        await using Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);
        var progress = new Recorder();

        SearchResult found = await h.Store.FindAsync(all.Rows[^1].Key, SearchDirection.Older, Compile("no such text"), null, progress, NoCancel);

        Assert.False(found.IsFound);
        Assert.True(progress.Reports.Count > 1, $"{progress.Reports.Count} reports");
        Assert.True(progress.Reports.Zip(progress.Reports.Skip(1)).All(pair => pair.Second > pair.First));
        Assert.True(progress.Reports[^1] > 100_000);
    }

    [Fact]
    public async Task Find_reports_a_pattern_that_times_out_as_timed_out_not_as_the_boundary()
    {
        using var dir = new ConfigTestDirectory();
        string catastrophic = new string('x', 40) + "y";
        dir.Write("SheetKraft1.log", Line(T0, "INFO", catastrophic) + Line(T0.AddSeconds(1), "INFO", catastrophic) + Line(T0.AddSeconds(2), "INFO", catastrophic));
        dir.Write("SheetKraft.log", Line(T1, "INFO", "live"));
        await using Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);
        RowPage all = await h.Store.ReadNewestAsync(100, null, null, NoCancel);

        // A backreference forces the backtracking engine, and "(x+x+)+y" then backtracks exponentially on the text.
        SearchPattern pattern = Compile(@"(x+x+)+y\1", regex: true);
        SearchResult found = await h.Store.FindAsync(all.Rows[^1].Key, SearchDirection.Older, pattern, null, null, NoCancel);

        Assert.False(found.IsFound);
        Assert.True(found.IsTimedOut);
        Assert.True(pattern.TimedOut);
    }

    [Fact]
    public async Task Dispose_stops_the_worker_and_its_watcher_so_no_update_follows()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "stop", 3));
        Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);
        Assert.True(h.Store.HasWatcher);

        await h.DisposeAsync();

        Assert.False(h.Store.HasWatcher);
        int count = h.Count;
        Directory.Delete(dir.Path, recursive: true);

        // Negative check: with the worker gone, the deletion produces no update. The wait is a bounded quiet period, several rescan intervals long.
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        Assert.Equal(count, h.Count);
    }

    [Fact]
    public async Task Steady_appends_trigger_no_rescan_while_a_created_member_does()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "live", 2));
        await using Harness h = Harness.Create(dir, rescanInterval: TimeSpan.FromHours(1));
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);
        int rescans = h.Store.RescanCount;

        // The live file grows; the tail polls read it, and the watcher is not asked to rescan. The waits pace the writer only.
        for (int i = 0; i < 10; i++)
        {
            AppendText(dir, "SheetKraft.log", Line(T0.AddMinutes(1 + i), "INFO", $"steady {i}"));
            await Task.Delay(30);
        }

        await h.WaitFor(u => u.Batch.Added.Any(row => row.Preview == "steady 9"));
        Assert.Equal(rescans, h.Store.RescanCount);

        // A created member is a rescan request, and does not wait for the one-hour interval.
        int mark = h.Count;
        dir.Write("SheetKraft1.log", Events(T0.AddHours(-1), "backup", 2));
        await h.WaitFor(u => u.Batch.Added.Any(row => row.Preview == "backup event 1"), mark);
        Assert.True(h.Store.RescanCount > rescans);
    }

    [Fact]
    public async Task An_injected_run_failure_raises_a_resync_and_later_reads_see_the_appended_rows()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "live", 2));
        await using Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);

        int mark = h.Count;
        h.Store.BeforeRun = FailOnce();
        StoreUpdate failed = await h.WaitFor(u => u.Resync && u.Batch.IsEmpty, mark);
        Assert.False(failed.Batch.Initial);
        Assert.Contains(failed.Status.RecentFailures, failure => failure.Contains("injected run failure", StringComparison.Ordinal));

        AppendText(dir, "SheetKraft.log", Line(T0.AddMinutes(5), "INFO", "after the failure"));
        StoreUpdate resynced = await h.WaitFor(u => u.Resync && u.Batch.Added.Any(row => row.Preview == "after the failure"), mark);
        Assert.Contains(resynced.Batch.Updated, row => row.Preview == "live event 1" && !row.Provisional);

        RowPage newest = await h.Store.ReadNewestAsync(3, null, null, NoCancel);
        Assert.Equal(new[] { "live event 0", "live event 1", "after the failure" }, newest.Rows.Select(row => row.Preview));
        Assert.True(newest.Rows[^1].Provisional);
    }

    [Fact]
    public async Task A_failed_initial_run_is_followed_by_an_initial_update_that_is_also_a_resync()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "live", 2));
        await using Harness h = Harness.Create(dir);
        h.Store.BeforeRun = FailOnce();
        h.Start();

        StoreUpdate failed = await h.WaitFor(u => u.Resync && u.Batch.IsEmpty);
        Assert.False(failed.Batch.Initial);

        StoreUpdate initial = await h.WaitFor(u => u.Batch.Initial);
        Assert.True(initial.Resync);
        Assert.Equal("live event 1", initial.Batch.Added.Single().Preview);
        Assert.Contains(initial.Status.RecentFailures, failure => failure.Contains("injected run failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Read_errors_repeated_on_every_rescan_are_not_raised_again_but_stay_in_the_status()
    {
        // The test needs a file the process cannot read. It is written and then made unreadable, which needs a Unix file mode.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0.AddHours(1), "live", 2));
        string backup = dir.Write("SheetKraft1.log", Events(T0, "backup", 2));
        File.SetUnixFileMode(backup, UnixFileMode.None);
        try
        {
            if (CanRead(backup))
            {
                return;
            }

            await using Harness h = Harness.Create(dir);
            h.Start();
            StoreUpdate initial = await h.WaitFor(u => u.Batch.Initial);
            Assert.Contains(initial.Batch.Errors, error => error.Path == backup);

            int raised = h.Count;
            int rescans = h.Store.RescanCount;
            await h.WaitUntil(() => h.Store.RescanCount >= rescans + 3);

            // Every rescan read the same error again: it is in the status, and no update was raised for it.
            Assert.Equal(raised, h.Count);
            Assert.Contains(h.Store.CurrentStatus.RecentErrors, error => error.Path == backup);
            Assert.Single(h.Store.CurrentStatus.RecentErrors);

            // Updates still flow for real changes.
            AppendText(dir, "SheetKraft.log", Line(T0.AddHours(2), "INFO", "still flowing"));
            await h.WaitFor(u => u.Batch.Added.Any(row => row.Preview == "still flowing"), raised);

            // Once a run reads the backup, its error is no longer current, and the status drops it.
            File.SetUnixFileMode(backup, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await h.WaitUntil(() => h.Store.CurrentStatus.RecentErrors.Count == 0);
            Assert.Empty(h.Store.CurrentStatus.RecentErrors);
        }
        finally
        {
            File.SetUnixFileMode(backup, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task A_rotation_surfaces_no_read_error_in_the_status()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft1.log", Events(T0, "one", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(1), "live", 4));
        await using Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);

        int mark = h.Count;
        File.Move(dir.Full("SheetKraft1.log"), dir.Full("SheetKraft2.log"));
        File.Move(dir.Full("SheetKraft.log"), dir.Full("SheetKraft1.log"));

        // Tail polls run while the live path is empty. Those runs find nothing at the path, which is the rotation in progress.
        int rescans = h.Store.RescanCount;
        await h.WaitUntil(() => h.Store.RescanCount >= rescans + 2);
        dir.Write("SheetKraft.log", Events(T0.AddHours(3), "new", 2));
        await h.WaitFor(u => u.Batch.Added.Any(row => row.Preview == "new event 1"), mark);

        Assert.Empty(h.Since(mark).SelectMany(update => update.Batch.Errors));
        Assert.Empty(h.Store.CurrentStatus.RecentErrors);
        Assert.Equal("SheetKraft.log", h.Store.CurrentStatus.LiveMemberName);
    }

    private static bool CanRead(string path)
    {
        try
        {
            File.OpenRead(path).Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A run hook that throws once, on the first run after it is set.</summary>
    private static Action FailOnce()
    {
        int fired = 0;
        return () =>
        {
            if (Interlocked.Exchange(ref fired, 1) == 0)
            {
                throw new InvalidOperationException("injected run failure");
            }
        };
    }

    [Fact]
    public async Task A_missing_directory_gives_an_empty_initial_update_and_members_appear_when_it_is_created()
    {
        using var dir = new ConfigTestDirectory();
        await using Harness h = Harness.Create(dir, logsDirectory: "logs");
        h.Start();

        StoreUpdate initial = await h.WaitFor(u => u.Batch.Initial);

        Assert.Empty(initial.Batch.Added);
        Assert.Empty(h.Store.Snapshot.Ordered);
        Assert.Equal(0, initial.Status.MemberCount);
        Assert.Null(initial.Status.DirectoryError);
        Assert.False(h.Store.HasWatcher);

        int mark = h.Count;
        dir.Write("logs/SheetKraft.log", Events(T0, "late", 3));
        StoreUpdate found = await h.WaitFor(u => u.Batch.Added.Any(row => row.Preview == "late event 1"), mark);

        Assert.Equal(1, found.Status.MemberCount);
        Assert.Null(found.Status.DirectoryError);
        Assert.Equal("SheetKraft.log", found.Status.LiveMemberName);
        Assert.True(h.Store.HasWatcher);
    }

    /// <summary>The text of the last event of a file, as the store reads it: from its header to its last line, without the final terminator.</summary>
    private static string MultiLineText(string content)
    {
        int start = content.LastIndexOf("ERROR", StringComparison.Ordinal);
        return content[start..^1];
    }

    private static int Generation(Harness h, string name) =>
        h.Store.Snapshot.Ordered.Single(member => member.Name == name).Generation;

    /// <summary>The keys of every event of the timeline, framed from each file: each member's events in timeline order.</summary>
    private static List<RowKey> TimelineKeys(CatalogSnapshot snapshot) =>
        snapshot.Ordered.SelectMany(KeysOf).ToList();

    /// <summary>The keys of the events of the timeline whose level is ERROR, framed from each file.</summary>
    private static HashSet<RowKey> ErrorKeys(CatalogSnapshot snapshot)
    {
        var errors = new HashSet<RowKey>();
        foreach (MemberInfo member in snapshot.Ordered)
        {
            using FileStream stream = Open(member.Path);
            foreach (EventExtent extent in new EventFramer(stream, member.Scheme, live: member.IsLive).ReadForward(0))
            {
                if (EventLevel(stream, member, extent) == LogLevel.Error)
                {
                    errors.Add(new RowKey(member.Generation, extent.Start));
                }
            }
        }

        return errors;
    }

    private static LogLevel EventLevel(FileStream stream, MemberInfo member, EventExtent extent)
    {
        (string text, _) = EventText.Read(stream, extent, EventText.DefaultCap);
        return member.Scheme.Parse(text, new ParseContext(TimeZoneInfo.Utc)).Level;
    }

    /// <summary>The keys of every event of a member's file, framed from the start as the store frames it.</summary>
    private static IEnumerable<RowKey> KeysOf(MemberInfo member)
    {
        if (!member.IsReadable)
        {
            return [];
        }

        using FileStream stream = Open(member.Path);
        return new EventFramer(stream, member.Scheme, live: member.IsLive)
            .ReadForward(0)
            .Select(extent => new RowKey(member.Generation, extent.Start))
            .ToList();
    }

    /// <summary>Rotates the numbered set one step: SheetKraft2 is deleted, SheetKraft1 becomes SheetKraft2, the active file becomes SheetKraft1, and a new active file is written.</summary>
    private static void Rotate(ConfigTestDirectory dir, int round)
    {
        if (File.Exists(dir.Full("SheetKraft2.log")))
        {
            File.Delete(dir.Full("SheetKraft2.log"));
        }

        if (File.Exists(dir.Full("SheetKraft1.log")))
        {
            File.Move(dir.Full("SheetKraft1.log"), dir.Full("SheetKraft2.log"));
        }

        if (File.Exists(dir.Full("SheetKraft.log")))
        {
            File.Move(dir.Full("SheetKraft.log"), dir.Full("SheetKraft1.log"));
        }

        dir.Write("SheetKraft.log", Events(T0.AddMinutes(round), $"rot{round}", 3));
    }

    private static void AppendText(ConfigTestDirectory dir, string name, string text) =>
        File.AppendAllText(dir.Full(name), text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>Compiles a search pattern for a test. The text is never empty.</summary>
    private static SearchPattern Compile(string text, bool caseSensitive = false, bool wholeWord = false, bool regex = false) =>
        SearchPattern.Create(new SearchQuery(text, caseSensitive, wholeWord, regex), out _)!;

    /// <summary>The key of the row with a preview, in a page of rows.</summary>
    private static RowKey KeyOf(RowPage page, string preview) => page.Rows.Single(row => row.Preview == preview).Key;

    /// <summary>
    /// Three files, oldest first: <c>SheetKraft2.log</c> with "two" events 0 to 2, <c>SheetKraft1.log</c> with "one" events 0 to 3 (event 3 an
    /// ERROR with a stack trace), and the live <c>SheetKraft.log</c> with "live" events 0 to 5 (event 5 provisional). Started, with the initial
    /// update awaited.
    /// </summary>
    private static async Task<Harness> StartSearchSet(ConfigTestDirectory dir)
    {
        dir.Write("SheetKraft2.log", Events(T0, "two", 3));
        dir.Write("SheetKraft1.log", Events(T1, "one", 4));
        dir.Write("SheetKraft.log", Events(T2, "live", 6));
        Harness h = Harness.Create(dir);
        h.Start();
        await h.WaitFor(u => u.Batch.Initial);
        return h;
    }

    /// <summary>One Serilog JSON line whose template is "Order {OrderId}", with the order's number as its property.</summary>
    private static string SerilogLine(int orderId) =>
        "{\"Timestamp\":\"" + T0.AddSeconds(orderId).ToString("O", CultureInfo.InvariantCulture)
        + "\",\"Level\":\"Information\",\"MessageTemplate\":\"Order {OrderId}\",\"Properties\":{\"OrderId\":" + orderId + "}}\n";

    /// <summary>A filter that rejects one row by its preview.</summary>
    private sealed class ExceptPreview(string preview) : IRowFilter
    {
        public bool Matches(EventRow row, LogEvent ev, string text) => row.Preview != preview;
    }

    /// <summary>Records every report synchronously.</summary>
    private sealed class Recorder : IProgress<long>
    {
        public List<long> Reports { get; } = [];

        public void Report(long value) => Reports.Add(value);
    }

    private sealed class LevelFilter(LogLevel level) : IRowFilter
    {
        public bool Matches(EventRow row, LogEvent ev, string text) => ev.Level == level;
    }

    /// <summary>A store over a directory, with every update recorded in order and a way to wait for one that matches.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly List<StoreUpdate> _updates = [];
        private TaskCompletionSource _changed = NewSignal();

        private Harness(LogStore store)
        {
            Store = store;
        }

        public LogStore Store { get; }

        /// <summary>Creates a store with short intervals. <paramref name="first"/> is subscribed before the recorder.</summary>
        public static Harness Create(ConfigTestDirectory dir, string logsDirectory = ".", Action<StoreUpdate>? first = null, TimeSpan? rescanInterval = null, string? format = null)
        {
            LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log", logsDirectory: logsDirectory, format: format);
            var store = new LogStore(config)
            {
                TailInterval = TimeSpan.FromMilliseconds(20),
                RescanInterval = rescanInterval ?? TimeSpan.FromMilliseconds(200),
                CoalesceDelay = TimeSpan.FromMilliseconds(20),
            };
            var harness = new Harness(store);
            if (first is not null)
            {
                store.Updated += first;
            }

            store.Updated += harness.Record;
            return harness;
        }

        public void Start() => Store.Start();

        /// <summary>The number of updates recorded so far. A mark for <see cref="WaitFor"/>.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _updates.Count;
                }
            }
        }

        public StoreUpdate Latest
        {
            get
            {
                lock (_gate)
                {
                    return _updates[^1];
                }
            }
        }

        /// <summary>The updates recorded from a mark on, in order.</summary>
        public IReadOnlyList<StoreUpdate> Since(int mark)
        {
            lock (_gate)
            {
                return _updates.Skip(mark).ToList();
            }
        }

        /// <summary>Waits until an update recorded from <paramref name="from"/> on matches, and returns the first that does.</summary>
        /// <exception cref="TimeoutException">No update matched within ten seconds.</exception>
        public async Task<StoreUpdate> WaitFor(Func<StoreUpdate, bool> match, int from = 0)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                Task changed;
                lock (_gate)
                {
                    for (int i = from; i < _updates.Count; i++)
                    {
                        if (match(_updates[i]))
                        {
                            return _updates[i];
                        }
                    }

                    changed = _changed.Task;
                }

                try
                {
                    await changed.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("No store update matched within ten seconds.");
                }
            }
        }

        /// <summary>Waits until a condition on the store holds. Polls; the condition is not timed.</summary>
        /// <exception cref="TimeoutException">The condition did not hold within ten seconds.</exception>
        public async Task WaitUntil(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("The condition did not hold within ten seconds.");
                }

                await Task.Delay(10);
            }
        }

        public ValueTask DisposeAsync() => Store.DisposeAsync();

        private void Record(StoreUpdate update)
        {
            lock (_gate)
            {
                _updates.Add(update);
                _changed.TrySetResult();
                _changed = NewSignal();
            }
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
