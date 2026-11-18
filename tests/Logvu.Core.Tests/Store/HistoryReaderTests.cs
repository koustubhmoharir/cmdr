using System.Text;
using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Shared.Core;
using static Logvu.Core.Tests.Store.StoreFixtures;

namespace Logvu.Core.Tests.Store;

public sealed class HistoryReaderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);

    /// <summary>A filter that keeps the events of one level.</summary>
    private sealed class LevelFilter(LogLevel level) : IRowFilter
    {
        public bool Matches(EventRow row, LogEvent ev) => ev.Level == level;
    }

    /// <summary>Records every report synchronously. <see cref="Progress{T}"/> would post its reports to the thread pool.</summary>
    private sealed class Recorder : IProgress<long>
    {
        public List<long> Reports { get; } = [];

        public void Report(long value) => Reports.Add(value);
    }

    /// <summary>
    /// The set of the store tests: an oldest file that starts with an orphan run, an empty file in the middle of the set, a file whose
    /// first time cannot be read, and three files with equal first times that are ordered by rotation rank.
    /// </summary>
    private static (LogvuConfig Config, Catalog Catalog) RealisticSet(ConfigTestDirectory dir)
    {
        dir.Write("SheetKraft5.log", "prose before any header\nsecond prose line\n" + Events(T0, "five", 3));
        dir.Write("SheetKraft4.log", string.Empty);
        dir.Write(
            "SheetKraft7.log",
            "ERROR 2026-13-45 99:99:99,000 [1] SheetKraft.Engine - clock is broken\nINFO  2026-13-45 99:99:99,000 [1] SheetKraft.Engine - still broken\n");
        dir.Write("SheetKraft2.log", Events(T1, "two", 4));
        dir.Write("SheetKraft1.log", Events(T1, "one", 4));
        dir.Write("SheetKraft.log", Events(T1, "active", 4));

        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = CatalogOf(dir, config, "SheetKraft5.log", "SheetKraft4.log", "SheetKraft7.log", "SheetKraft2.log", "SheetKraft1.log", "SheetKraft.log");
        return (config, catalog);
    }

    /// <summary>Three members with distinct times: the oldest is <c>SheetKraft2.log</c>, the active file is the newest.</summary>
    private static (LogvuConfig Config, Catalog Catalog, Member A, Member B, Member C) ThreeMembers(ConfigTestDirectory dir)
    {
        dir.Write("SheetKraft2.log", Events(T0, "alpha", 3));
        dir.Write("SheetKraft1.log", Events(T1, "bravo", 3));
        dir.Write("SheetKraft.log", Events(T2, "charlie", 3));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = CatalogOf(dir, config, "SheetKraft2.log", "SheetKraft1.log", "SheetKraft.log");
        return (config, catalog, catalog.Ordered[0], catalog.Ordered[1], catalog.Ordered[2]);
    }

    private static HistoryReader NewReader(Catalog catalog, LogvuConfig config, int batchEvents = 256, long batchBytes = 4L * 1024 * 1024) =>
        new(catalog, config) { BatchEvents = batchEvents, BatchBytes = batchBytes };

    /// <summary>Pages backward from the newest row until the start of history, and joins the pages oldest first.</summary>
    private static List<EventRow> PageBackward(HistoryReader reader, int pageSize, IRowFilter? filter = null)
    {
        var pages = new List<IReadOnlyList<EventRow>>();
        RowPage page = reader.ReadNewest(pageSize, filter, null, CancellationToken.None);
        while (true)
        {
            Assert.Empty(page.Errors);
            pages.Add(page.Rows);
            if (page.ReachedBoundary)
            {
                break;
            }

            Assert.NotEmpty(page.Rows);
            page = reader.ReadOlder(page.Rows[0].Key, pageSize, filter, null, CancellationToken.None);
        }

        pages.Reverse();
        return pages.SelectMany(rows => rows).ToList();
    }

    /// <summary>Pages forward from the oldest row until the end of history, starting with that row.</summary>
    private static List<EventRow> PageForward(HistoryReader reader, EventRow oldest, int pageSize)
    {
        var rows = new List<EventRow> { oldest };
        RowKey anchor = oldest.Key;
        while (true)
        {
            RowPage page = reader.ReadNewer(anchor, pageSize, null, null, CancellationToken.None);
            Assert.Empty(page.Errors);
            rows.AddRange(page.Rows);
            if (page.ReachedBoundary)
            {
                return rows;
            }

            Assert.NotEmpty(page.Rows);
            anchor = page.Rows[^1].Key;
        }
    }

    [Theory]
    [InlineData(1, 256, 4194304L)]
    [InlineData(3, 256, 4194304L)]
    [InlineData(7, 256, 4194304L)]
    [InlineData(3, 2, 4194304L)]
    [InlineData(7, 1, 1L)]
    public void Paging_backward_from_the_newest_row_gives_the_whole_history_once(int pageSize, int batchEvents, long batchBytes)
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog) = RealisticSet(dir);
        List<EventRow> expected = Expected(catalog, config);
        Assert.Equal(18, expected.Count);

        Assert.Equal(expected, PageBackward(NewReader(catalog, config, batchEvents, batchBytes), pageSize));
    }

    [Theory]
    [InlineData(1, 256, 4194304L)]
    [InlineData(3, 2, 4194304L)]
    [InlineData(7, 1, 1L)]
    public void Paging_forward_from_the_oldest_row_gives_the_whole_history_once(int pageSize, int batchEvents, long batchBytes)
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog) = RealisticSet(dir);
        List<EventRow> expected = Expected(catalog, config);

        Assert.Equal(expected, PageForward(NewReader(catalog, config, batchEvents, batchBytes), expected[0], pageSize));
    }

    [Fact]
    public void Rows_of_the_realistic_set_have_their_orphan_unknown_time_and_rotation_positions()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog) = RealisticSet(dir);
        List<EventRow> rows = Expected(catalog, config);
        Member unknown = catalog.Ordered[0];

        Assert.Equal("SheetKraft7.log", unknown.Name);
        Assert.Equal(MemberState.UnknownTime, unknown.State);
        Assert.Equal(2, rows.Count(row => row.Key.Generation == unknown.Generation));
        Assert.Null(rows[0].Time);
        Assert.Equal(unknown.Generation, rows[0].Key.Generation);

        // The orphan run at the start of the oldest dated file is one raw row at offset 0.
        Member oldest = catalog.Ordered[1];
        Assert.Equal("SheetKraft5.log", oldest.Name);
        EventRow orphan = rows.First(row => row.Key.Generation == oldest.Generation);
        Assert.Equal(new RowKey(oldest.Generation, 0), orphan.Key);
        Assert.True(orphan.Orphan);
        Assert.True(orphan.IsRaw);
        Assert.DoesNotContain(rows, row => row.Key.Generation == catalog.Ordered[2].Generation && row.Orphan);

        // Equal first times: the rotation order decides, the higher number first, then the active file.
        Assert.Equal(new[] { "SheetKraft2.log", "SheetKraft1.log", "SheetKraft.log" }, catalog.Ordered.Skip(2).Select(m => m.Name));
        Assert.Equal(MemberState.Pending, catalog.All.Single(m => m.Name == "SheetKraft4.log").State);
    }

    [Fact]
    public void Member_expired_in_the_middle_is_passed_over_silently_and_its_rows_are_not_returned()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, Member a, Member b, Member c) = ThreeMembers(dir);
        List<EventRow> readable = Expected(catalog, config);
        b.Expire();

        RowPage all = NewReader(catalog, config).ReadNewest(100, null, null, CancellationToken.None);

        Assert.Empty(all.Errors);
        Assert.True(all.ReachedBoundary);
        Assert.Equal(readable.Where(row => row.Key.Generation != b.Generation), all.Rows);
        Assert.Equal(6, all.Rows.Count);
        Assert.Equal(a.Generation, all.Rows[0].Key.Generation);
        Assert.Equal(c.Generation, all.Rows[^1].Key.Generation);
    }

    [Fact]
    public void Older_read_from_an_anchor_in_an_expired_member_starts_with_the_member_before_it()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, Member a, Member b, Member c) = ThreeMembers(dir);
        List<EventRow> readable = Expected(catalog, config);
        b.Expire();
        var anchor = new RowKey(b.Generation, 0);

        RowPage older = NewReader(catalog, config).ReadOlder(anchor, 100, null, null, CancellationToken.None);
        RowPage newer = NewReader(catalog, config).ReadNewer(anchor, 100, null, null, CancellationToken.None);

        Assert.Empty(older.Errors);
        Assert.True(older.ReachedBoundary);
        Assert.Equal(readable.Where(row => row.Key.Generation == a.Generation), older.Rows);
        Assert.Empty(newer.Errors);
        Assert.True(newer.ReachedBoundary);
        Assert.Equal(readable.Where(row => row.Key.Generation == c.Generation), newer.Rows);
    }

    [Fact]
    public void Expired_anchor_is_read_from_the_member_before_it_when_the_page_is_filled()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, Member a, Member b, Member c) = ThreeMembers(dir);
        List<EventRow> readable = Expected(catalog, config);
        c.Expire();

        // The newest row of c's file is paused on; reading older from it must not return c's rows.
        RowPage page = NewReader(catalog, config).ReadOlder(new RowKey(c.Generation, 0), 4, null, null, CancellationToken.None);

        Assert.False(page.ReachedBoundary);
        // The walk starts at b, the member before c, and continues into a for the rest of the page.
        Assert.Equal(readable.Where(row => row.Key.Generation != c.Generation).TakeLast(4), page.Rows);
    }

    [Fact]
    public void Replaced_file_yields_an_error_and_reading_continues_without_its_new_occupant()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, Member a, Member b, Member c) = ThreeMembers(dir);
        List<EventRow> expected = Expected(catalog, config);
        List<EventRow> expectedWithoutB = expected.Where(row => row.Key.Generation != b.Generation).ToList();

        // Keep the old file open while the path is reused: that keeps its identity from being recycled.
        using FileStream original = Open(b.Path);
        File.Delete(b.Path);
        dir.Write("SheetKraft1.log", Events(T1, "replacement", 3));
        Assert.NotEqual(b.Identity, FileIdentity.TryGet(b.Path));

        RowPage page = NewReader(catalog, config).ReadNewest(100, null, null, CancellationToken.None);

        MemberReadError error = Assert.Single(page.Errors);
        Assert.Equal(b.Generation, error.Generation);
        Assert.Equal(b.Path, error.Path);
        Assert.Equal(MemberFile.IdentityChanged, error.Message);
        Assert.Equal(expectedWithoutB, page.Rows);
        Assert.DoesNotContain(page.Rows, row => row.Preview.Contains("replacement", StringComparison.Ordinal));
        Assert.True(page.ReachedBoundary);

        // The same holds for a read that starts in the member after the replaced one.
        RowPage older = NewReader(catalog, config).ReadOlder(new RowKey(c.Generation, 0), 100, null, null, CancellationToken.None);
        Assert.Equal(new MemberReadError(b.Generation, b.Path, MemberFile.IdentityChanged), Assert.Single(older.Errors));
        Assert.Equal(expected.Where(row => row.Key.Generation == a.Generation), older.Rows);
    }

    [Fact]
    public void Missing_file_is_recorded_as_not_found_and_reading_goes_on()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, Member a, Member b, Member c) = ThreeMembers(dir);
        List<EventRow> expected = Expected(catalog, config);
        File.Delete(b.Path);

        RowPage page = NewReader(catalog, config).ReadNewest(100, null, null, CancellationToken.None);

        Assert.Equal(new MemberReadError(b.Generation, b.Path, MemberFile.NotFound), Assert.Single(page.Errors));
        Assert.Equal(expected.Where(row => row.Key.Generation != b.Generation), page.Rows);
    }

    [Fact]
    public void Filter_for_errors_returns_only_errors_across_members_oldest_first_and_complete()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog) = RealisticSet(dir);
        var filter = new LevelFilter(LogLevel.Error);
        List<EventRow> expected = Expected(catalog, config).Where(row => row.Level == LogLevel.Error).ToList();
        Assert.True(expected.Select(row => row.Key.Generation).Distinct().Count() > 1);

        RowPage all = NewReader(catalog, config).ReadNewest(1000, filter, null, CancellationToken.None);

        Assert.Equal(expected, all.Rows);
        Assert.True(all.ReachedBoundary);
        Assert.Empty(all.Errors);
        foreach (int size in new[] { 1, 2, 5 })
        {
            Assert.Equal(expected, PageBackward(NewReader(catalog, config, batchEvents: 3), size, filter));
        }
    }

    [Fact]
    public void Read_cursor_bounds_every_read_and_events_after_it_are_never_returned()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog) = RealisticSet(dir);
        Member member = catalog.Ordered.Single(m => m.Name == "SheetKraft.log");
        EventExtent[] extents = Extents(member);
        long cut = extents[2].End;
        member.ReadCursor = cut;
        List<EventRow> expected = Expected(catalog, config);
        Assert.Equal(3, expected.Count(row => row.Key.Generation == member.Generation));

        HistoryReader reader = NewReader(catalog, config);
        Assert.Equal(expected, reader.ReadNewest(1000, null, null, CancellationToken.None).Rows);
        Assert.Equal(expected, PageBackward(reader, 1));

        // Anchors past the cursor, in the live tail: the read starts at the cursor instead.
        RowPage fromTail = reader.ReadOlder(new RowKey(member.Generation, extents[3].Start), 1000, null, null, CancellationToken.None);
        Assert.Equal(expected.Where(row => row.Key.Generation != member.Generation || row.Key.Offset < cut), fromTail.Rows);

        foreach (EventRow row in expected)
        {
            RowPage newer = reader.ReadNewer(row.Key, 1000, null, null, CancellationToken.None);
            Assert.DoesNotContain(newer.Rows, r => r.Key.Generation == member.Generation && r.Key.Offset >= cut);
            Assert.Equal(expected.SkipWhile(e => e.Key != row.Key).Skip(1), newer.Rows);
        }
    }

    [Fact]
    public void Reached_boundary_is_true_exactly_when_the_start_or_end_of_history_is_hit()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "only", 3));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = CatalogOf(dir, config, "SheetKraft.log");
        List<EventRow> rows = Expected(catalog, config);
        Assert.Equal(3, rows.Count);
        HistoryReader reader = NewReader(catalog, config);
        CancellationToken none = CancellationToken.None;

        // The page is filled: the walk did not reach either end, even though the last row is the oldest or newest.
        Assert.False(reader.ReadNewest(3, null, null, none).ReachedBoundary);
        Assert.False(reader.ReadOlder(rows[2].Key, 2, null, null, none).ReachedBoundary);
        Assert.False(reader.ReadNewer(rows[0].Key, 2, null, null, none).ReachedBoundary);

        // The walk ran into the start or the end of history.
        RowPage newest = reader.ReadNewest(4, null, null, none);
        Assert.True(newest.ReachedBoundary);
        Assert.Equal(rows, newest.Rows);

        RowPage beyondOldest = reader.ReadOlder(rows[0].Key, 2, null, null, none);
        Assert.True(beyondOldest.ReachedBoundary);
        Assert.Empty(beyondOldest.Rows);

        RowPage beforeLast = reader.ReadOlder(rows[1].Key, 5, null, null, none);
        Assert.True(beforeLast.ReachedBoundary);
        Assert.Equal(rows.Take(1), beforeLast.Rows);

        RowPage afterNewest = reader.ReadNewer(rows[2].Key, 1, null, null, none);
        Assert.True(afterNewest.ReachedBoundary);
        Assert.Empty(afterNewest.Rows);

        RowPage afterOldest = reader.ReadNewer(rows[0].Key, 5, null, null, none);
        Assert.True(afterOldest.ReachedBoundary);
        Assert.Equal(rows.Skip(1), afterOldest.Rows);
    }

    [Fact]
    public void Reading_the_newest_rows_of_a_history_with_no_readable_member_reaches_the_boundary_at_once()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", string.Empty);
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = CatalogOf(dir, config, "SheetKraft.log");

        RowPage page = NewReader(catalog, config).ReadNewest(5, null, null, CancellationToken.None);

        Assert.Empty(page.Rows);
        Assert.True(page.ReachedBoundary);
        Assert.Empty(page.Errors);
    }

    [Fact]
    public void Anchor_whose_generation_is_unknown_or_pending_is_rejected()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "only", 2));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = new(config);
        Member pending = catalog.Add(dir.Full("SheetKraft1.log"), "pending-identity");
        HistoryReader reader = NewReader(catalog, config);

        Assert.Throws<ArgumentException>(() => reader.ReadOlder(new RowKey(99, 0), 1, null, null, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => reader.ReadNewer(new RowKey(pending.Generation, 0), 1, null, null, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadNewest(0, null, null, CancellationToken.None));
    }

    [Fact]
    public void Pre_cancelled_token_throws_before_any_read()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog) = RealisticSet(dir);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        HistoryReader reader = NewReader(catalog, config);
        RowKey anchor = Expected(catalog, config)[3].Key;

        Assert.Throws<OperationCanceledException>(() => reader.ReadNewest(3, null, null, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => reader.ReadOlder(anchor, 3, null, null, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => reader.ReadNewer(anchor, 3, null, null, cancelled.Token));
    }

    [Fact]
    public void Scanned_bytes_are_reported_per_batch_and_the_last_report_is_the_bytes_scanned()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog) = RealisticSet(dir);
        var progress = new Recorder();

        RowPage page = NewReader(catalog, config, batchEvents: 2).ReadNewest(3, null, progress, CancellationToken.None);

        // Without a filter every scanned event is a returned row, so the scanned bytes are the bytes of the three rows.
        long expectedBytes = page.Rows.Sum(row => row.Extent.End - row.Extent.Start);
        Assert.Equal(2, progress.Reports.Count);
        Assert.Equal(progress.Reports.OrderBy(x => x), progress.Reports);
        Assert.Equal(expectedBytes, progress.Reports[^1]);
    }

    [Fact]
    public void Cancellation_during_a_scan_throws_at_the_next_batch()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog) = RealisticSet(dir);
        using var cts = new CancellationTokenSource();
        var progress = new CancelOnFirstReport(cts);

        Assert.Throws<OperationCanceledException>(() => NewReader(catalog, config, batchEvents: 1).ReadNewest(10, null, progress, cts.Token));
        Assert.Single(progress.Reports);
    }

    private sealed class CancelOnFirstReport(CancellationTokenSource cts) : IProgress<long>
    {
        public List<long> Reports { get; } = [];

        public void Report(long value)
        {
            Reports.Add(value);
            cts.Cancel();
        }
    }

    [Fact]
    public void Event_larger_than_the_batch_limit_is_read_whole_and_the_walk_continues_past_it()
    {
        using var dir = new ConfigTestDirectory();
        // One event of 5 MiB, between two ordinary ones, all in one file: the walk must not split it or skip past it.
        var huge = new StringBuilder(Line(T0, "ERROR", "huge trace"));
        for (int i = 0; i < 5 * 1024; i++)
        {
            huge.Append('x', 1023).Append('\n');
        }

        dir.Write("SheetKraft1.log", Line(T0.AddMinutes(-5), "INFO", "before") + huge + Line(T0.AddMinutes(1), "WARN", "after"));
        dir.Write("SheetKraft.log", Events(T1, "active", 3));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = CatalogOf(dir, config, "SheetKraft1.log", "SheetKraft.log");
        List<EventRow> expected = Expected(catalog, config);
        EventRow hugeRow = expected.Single(row => row.Preview == "huge trace");
        Assert.True(hugeRow.Extent.End - hugeRow.Extent.Start > 4L * 1024 * 1024);

        Assert.Equal(expected, PageBackward(NewReader(catalog, config), 1));
        Assert.Equal(expected, PageBackward(NewReader(catalog, config), 3));
        Assert.Equal(expected, PageBackward(NewReader(catalog, config, batchEvents: 1, batchBytes: 1000), 2));
        Assert.Equal(expected, PageForward(NewReader(catalog, config, batchEvents: 1, batchBytes: 1000), expected[0], 1));
        Assert.Equal(expected, PageForward(NewReader(catalog, config), expected[0], 3));
    }

    [Fact]
    public void Slog_json_set_pages_completely_in_both_directions()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("api1.log", SlogLines(T0, "one", 3) + "\n" + SlogLines(T0.AddMinutes(10), "two", 2) + "{\"time\":\"2026-11-18T09:20:00Z\",\"level\":\"WARN\",\"msg\":\"cut");
        dir.Write("api.log", SlogLines(T1, "three", 4) + "{\"time\":\"2026-11-18T10:30:00Z\",\"level\":\"ERROR\",\"msg\":\"broken\",\"err\":{\"message\":\"db\",\"cause\":[\"dial\"]}}\n");
        LogvuConfig config = StoreTestConfig.Create(dir, prefix: "api", pattern: @"^api(?:[0-9]+)?[.]log$", activeFile: "api.log");
        Catalog catalog = CatalogOf(dir, config, "api1.log", "api.log");
        List<EventRow> expected = Expected(catalog, config);
        Assert.Contains(expected, row => row.IsRaw && row.Preview.StartsWith("{\"time\":\"2026-11-18T09:20:00Z\"", StringComparison.Ordinal));

        foreach (int size in new[] { 1, 2, 5 })
        {
            Assert.Equal(expected, PageBackward(NewReader(catalog, config, batchEvents: 2), size));
            Assert.Equal(expected, PageForward(NewReader(catalog, config), expected[0], size));
        }
    }

    private static string SlogLines(DateTimeOffset start, string tag, int count)
    {
        var text = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            string time = start.AddSeconds(i * 3).ToString("yyyy-MM-ddTHH:mm:ss.fffK", System.Globalization.CultureInfo.InvariantCulture);
            string level = i % 2 == 0 ? "INFO" : "DEBUG-4";
            text.Append("{\"time\":\"" + time + "\",\"level\":\"" + level + "\",\"msg\":\"" + tag + " " + i + "\",\"logger\":\"api\"}\n");
        }

        return text.ToString();
    }
}
