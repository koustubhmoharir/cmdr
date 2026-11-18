using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using static Logvu.Core.Tests.Store.StoreFixtures;

namespace Logvu.Core.Tests.Store;

/// <summary>Time-window skipping in <see cref="HistoryReader"/>: which members are opened, and that the results equal an unskipped read.</summary>
public sealed class HistoryTimeWindowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);

    /// <summary>A filter that keeps the events whose time lies in its window. A row without a time is kept only when no bound is set.</summary>
    private sealed class WindowFilter(DateTimeOffset? from, DateTimeOffset? to) : IRowFilter
    {
        public (DateTimeOffset? From, DateTimeOffset? To) TimeWindow => (from, to);

        public bool Matches(EventRow row, LogEvent ev, string text)
        {
            if (from is null && to is null)
            {
                return true;
            }

            if (row.Time is not { } time)
            {
                return false;
            }

            return (from is not { } start || time >= start) && (to is not { } end || time <= end);
        }
    }

    /// <summary>The same predicate as <see cref="WindowFilter"/> with no time window, so that no member is skipped.</summary>
    private sealed class Unwindowed(IRowFilter inner) : IRowFilter
    {
        public bool Matches(EventRow row, LogEvent ev, string text) => inner.Matches(row, ev, text);
    }

    /// <summary>Records every report synchronously.</summary>
    private sealed class Recorder : IProgress<long>
    {
        public List<long> Reports { get; } = [];

        public void Report(long value) => Reports.Add(value);

        public long Last => Reports.LastOrDefault();
    }

    private static HistoryReader NewReader(LogvuConfig config) => new(config);

    /// <summary>The event bytes of a member, as a read that reaches it scans them.</summary>
    private static long BytesOf(Member member) => Extents(member).Sum(extent => extent.End - extent.Start);

    /// <summary>Three dated members, one hour apart, each with three events seven seconds apart. The unknown-time group is absent.</summary>
    private static (LogvuConfig Config, Catalog Catalog, Member A, Member B, Member C) ThreeDated(ConfigTestDirectory dir)
    {
        dir.Write("SheetKraft2.log", Events(T0, "alpha", 3));
        dir.Write("SheetKraft1.log", Events(T1, "bravo", 3));
        dir.Write("SheetKraft.log", Events(T2, "charlie", 3));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = CatalogOf(dir, config, "SheetKraft2.log", "SheetKraft1.log", "SheetKraft.log");
        return (config, catalog, catalog.Ordered[0], catalog.Ordered[1], catalog.Ordered[2]);
    }

    /// <summary>Pages backward from the newest row, through the whole history, and joins the pages oldest first.</summary>
    private static List<EventRow> AllBackward(HistoryReader reader, CatalogSnapshot snapshot, int pageSize, IRowFilter filter)
    {
        var pages = new List<IReadOnlyList<EventRow>>();
        RowPage page = reader.ReadNewest(snapshot, pageSize, filter, null, CancellationToken.None);
        while (true)
        {
            Assert.Empty(page.Errors);
            pages.Add(page.Rows);
            if (page.ReachedBoundary)
            {
                break;
            }

            page = reader.ReadOlder(snapshot, page.Rows[0].Key, pageSize, filter, null, CancellationToken.None);
        }

        pages.Reverse();
        return pages.SelectMany(rows => rows).ToList();
    }

    /// <summary>Pages forward from the oldest event of history, starting with it, through the whole history.</summary>
    private static List<EventRow> AllForward(HistoryReader reader, CatalogSnapshot snapshot, int pageSize, IRowFilter filter)
    {
        var rows = new List<EventRow>();
        RowKey anchor = new(snapshot.Ordered[0].Generation, 0);
        bool inclusive = true;
        while (true)
        {
            RowPage page = reader.ReadNewer(snapshot, anchor, pageSize, filter, null, CancellationToken.None, inclusive);
            Assert.Empty(page.Errors);
            rows.AddRange(page.Rows);
            if (page.ReachedBoundary)
            {
                return rows;
            }

            anchor = page.Rows[^1].Key;
            inclusive = false;
        }
    }

    [Fact]
    public void A_window_inside_the_middle_member_skips_the_others_and_scans_only_its_bytes()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, _, Member b, _) = ThreeDated(dir);
        var filter = new WindowFilter(T1.AddSeconds(1), T1.AddSeconds(10));
        var progress = new Recorder();

        RowPage page = NewReader(config).ReadNewest(catalog.Snapshot(), 100, filter, progress, CancellationToken.None);

        Assert.Empty(page.Errors);
        Assert.Equal(new[] { "bravo event 1" }, page.Rows.Select(row => row.Preview));
        Assert.Equal(BytesOf(b), progress.Last);

        RowPage reference = NewReader(config).ReadNewest(catalog.Snapshot(), 100, new Unwindowed(filter), null, CancellationToken.None);
        Assert.Equal(reference.Rows, page.Rows);
    }

    [Fact]
    public void Without_the_window_every_member_is_scanned()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, Member a, Member b, Member c) = ThreeDated(dir);
        var progress = new Recorder();

        NewReader(config).ReadNewest(catalog.Snapshot(), 100, new Unwindowed(new WindowFilter(T1.AddSeconds(1), T1.AddSeconds(10))), progress, CancellationToken.None);

        Assert.Equal(BytesOf(a) + BytesOf(b) + BytesOf(c), progress.Last);
    }

    [Fact]
    public void A_bound_equal_to_a_members_first_time_keeps_that_member_and_its_predecessors_events_at_that_time()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft2.log", Line(T0, "INFO", "alpha early") + Line(T1, "INFO", "alpha at T1"));
        dir.Write("SheetKraft1.log", Line(T1, "INFO", "bravo at T1") + Line(T1.AddSeconds(7), "INFO", "bravo later"));
        dir.Write("SheetKraft.log", Line(T2, "INFO", "charlie"));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = CatalogOf(dir, config, "SheetKraft2.log", "SheetKraft1.log", "SheetKraft.log");
        Member a = catalog.Ordered[0];
        Member b = catalog.Ordered[1];
        Member c = catalog.Ordered[2];
        var filter = new WindowFilter(T1, T1);
        var progress = new Recorder();

        RowPage page = NewReader(config).ReadNewest(catalog.Snapshot(), 100, filter, progress, CancellationToken.None);

        Assert.Equal(new[] { "alpha at T1", "bravo at T1" }, page.Rows.Select(row => row.Preview));
        Assert.Equal(new[] { a.Generation, b.Generation }, page.Rows.Select(row => row.Key.Generation));
        Assert.Equal(BytesOf(a) + BytesOf(b), progress.Last);
        Assert.DoesNotContain(page.Rows, row => row.Key.Generation == c.Generation);
        Assert.Equal(NewReader(config).ReadNewest(catalog.Snapshot(), 100, new Unwindowed(filter), null, CancellationToken.None).Rows, page.Rows);
    }

    [Fact]
    public void A_window_ending_at_the_first_time_of_the_next_member_keeps_the_previous_member()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft2.log", Line(T0, "INFO", "alpha early") + Line(T1, "INFO", "alpha at T1"));
        dir.Write("SheetKraft1.log", Line(T1, "INFO", "bravo at T1"));
        dir.Write("SheetKraft.log", Line(T2, "INFO", "charlie"));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        CatalogSnapshot snapshot = CatalogOf(dir, config, "SheetKraft2.log", "SheetKraft1.log", "SheetKraft.log").Snapshot();

        var filter = new WindowFilter(null, T1);

        RowPage page = NewReader(config).ReadNewest(snapshot, 100, filter, null, CancellationToken.None);

        Assert.Equal(new[] { "alpha early", "alpha at T1", "bravo at T1" }, page.Rows.Select(row => row.Preview));
        Assert.Equal(NewReader(config).ReadNewest(snapshot, 100, new Unwindowed(filter), null, CancellationToken.None).Rows, page.Rows);
    }

    [Fact]
    public void An_unknown_time_member_is_never_skipped()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft7.log", "ERROR 2026-13-45 99:99:99,000 [1] SheetKraft.Engine - clock is broken\nINFO  2026-13-45 99:99:99,000 [1] SheetKraft.Engine - still broken\n");
        dir.Write("SheetKraft2.log", Events(T0, "alpha", 3));
        dir.Write("SheetKraft1.log", Events(T1, "bravo", 3));
        dir.Write("SheetKraft.log", Events(T2, "charlie", 3));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = CatalogOf(dir, config, "SheetKraft7.log", "SheetKraft2.log", "SheetKraft1.log", "SheetKraft.log");
        Member unknown = catalog.Ordered[0];
        Assert.Equal(MemberState.UnknownTime, unknown.State);
        Member b = catalog.Ordered[2];
        var filter = new WindowFilter(T1.AddSeconds(1), T1.AddMinutes(1));
        var progress = new Recorder();

        RowPage page = NewReader(config).ReadNewest(catalog.Snapshot(), 100, filter, progress, CancellationToken.None);

        Assert.Equal(new[] { "bravo event 1", "bravo event 2" }, page.Rows.Select(row => row.Preview));
        Assert.Equal(BytesOf(unknown) + BytesOf(b), progress.Last);
    }

    [Fact]
    public void The_newest_member_is_open_ended_and_is_scanned_whatever_its_last_event()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, _, _, Member c) = ThreeDated(dir);
        var progress = new Recorder();
        var filter = new WindowFilter(T2.AddDays(1), null);

        RowPage page = NewReader(config).ReadNewest(catalog.Snapshot(), 100, filter, progress, CancellationToken.None);

        Assert.Empty(page.Rows);
        Assert.True(page.ReachedBoundary);
        Assert.Equal(BytesOf(c), progress.Last);
    }

    [Fact]
    public void A_window_starting_inside_the_newest_member_reads_only_that_member()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, _, _, Member c) = ThreeDated(dir);
        var progress = new Recorder();

        RowPage page = NewReader(config).ReadNewest(catalog.Snapshot(), 100, new WindowFilter(T2.AddSeconds(10), null), progress, CancellationToken.None);

        Assert.Equal(new[] { "charlie event 2" }, page.Rows.Select(row => row.Preview));
        Assert.Equal(BytesOf(c), progress.Last);
    }

    [Fact]
    public void A_window_that_no_member_reaches_scans_nothing()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, _, _, _) = ThreeDated(dir);
        var progress = new Recorder();

        RowPage page = NewReader(config).ReadNewest(catalog.Snapshot(), 100, new WindowFilter(T0.AddDays(-2), T0.AddDays(-1)), progress, CancellationToken.None);

        Assert.Empty(page.Rows);
        Assert.True(page.ReachedBoundary);
        Assert.Empty(progress.Reports);
    }

    public static IEnumerable<object?[]> Windows()
    {
        yield return new object?[] { T1.AddSeconds(1), T1.AddSeconds(10) };
        yield return new object?[] { T1, T1 };
        yield return new object?[] { T0.AddSeconds(20), null };
        yield return new object?[] { null, T1.AddSeconds(7) };
        yield return new object?[] { T2.AddSeconds(10), null };
        yield return new object?[] { T2.AddDays(1), null };
        yield return new object?[] { T0.AddDays(-2), T0.AddDays(-1) };
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void Windowed_reads_in_both_directions_give_the_same_rows_as_an_unskipped_read(DateTimeOffset? from, DateTimeOffset? to)
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, _, _, _) = ThreeDated(dir);
        CatalogSnapshot snapshot = catalog.Snapshot();
        var filter = new WindowFilter(from, to);
        var unwindowed = new Unwindowed(filter);
        List<EventRow> reference = AllBackward(NewReader(config), snapshot, 100, unwindowed);

        foreach (int pageSize in new[] { 1, 2, 5, 100 })
        {
            Assert.Equal(reference, AllBackward(NewReader(config), snapshot, pageSize, filter));
            Assert.Equal(reference, AllForward(NewReader(config), snapshot, pageSize, filter));
        }
    }

    [Fact]
    public void Windowed_reads_from_an_anchor_in_the_middle_match_the_unskipped_read()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, Catalog catalog, _, Member b, _) = ThreeDated(dir);
        CatalogSnapshot snapshot = catalog.Snapshot();
        var filter = new WindowFilter(T0.AddSeconds(20), T1.AddSeconds(10));
        var unwindowed = new Unwindowed(filter);
        RowKey anchor = new(b.Generation, Extents(b)[1].Start);

        Assert.Equal(
            NewReader(config).ReadOlder(snapshot, anchor, 100, unwindowed, null, CancellationToken.None).Rows,
            NewReader(config).ReadOlder(snapshot, anchor, 100, filter, null, CancellationToken.None).Rows);
        Assert.Equal(
            NewReader(config).ReadNewer(snapshot, anchor, 100, unwindowed, null, CancellationToken.None).Rows,
            NewReader(config).ReadNewer(snapshot, anchor, 100, filter, null, CancellationToken.None).Rows);
        Assert.Equal(
            NewReader(config).ReadNewer(snapshot, anchor, 100, unwindowed, null, CancellationToken.None, inclusive: true).Rows,
            NewReader(config).ReadNewer(snapshot, anchor, 100, filter, null, CancellationToken.None, inclusive: true).Rows);
    }
}
