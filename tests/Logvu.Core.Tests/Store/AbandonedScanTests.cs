using Logvu.Core.Config;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Shared.Core;
using static Logvu.Core.Tests.Store.StoreFixtures;

namespace Logvu.Core.Tests.Store;

/// <summary>A history walk stops when its filter is abandoned, which a timed-out text pattern does.</summary>
public sealed class AbandonedScanTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Records every report synchronously.</summary>
    private sealed class Recorder : IProgress<long>
    {
        public List<long> Reports { get; } = [];

        public void Report(long value) => Reports.Add(value);

        public long Last => Reports.LastOrDefault();
    }

    /// <summary>Four rotated files of forty events each, every message a run of thirty a's, which the pattern below backtracks over.</summary>
    private static (LogvuConfig Config, CatalogSnapshot Snapshot, long TotalBytes) MultiFileSet(ConfigTestDirectory dir)
    {
        string message = new('a', 30);
        string[] names = ["SheetKraft3.log", "SheetKraft2.log", "SheetKraft1.log", "SheetKraft.log"];
        for (int file = 0; file < names.Length; file++)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < 40; i++)
            {
                text.Append(Line(T0.AddHours(file).AddSeconds(i), "INFO", message));
            }

            dir.Write(names[file], text.ToString());
        }

        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = CatalogOf(dir, config, names);
        long total = catalog.Ordered.Where(member => member.IsReadable).Sum(member => Extents(member).Sum(extent => extent.End - extent.Start));
        return (config, catalog.Snapshot(), total);
    }

    [Fact]
    public void A_catastrophic_pattern_stops_the_walk_after_its_first_timeout_and_the_page_is_abandoned()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, CatalogSnapshot snapshot, long total) = MultiFileSet(dir);
        var filter = new EventFilter(new FilterSpec(Text: new SearchQuery("(a+)+\\1b", CaseSensitive: false, WholeWord: false, Regex: true)));
        var progress = new Recorder();

        RowPage page = new HistoryReader(config).ReadNewest(snapshot, 1000, filter, progress, CancellationToken.None);

        Assert.True(filter.TimedOut);
        Assert.True(page.Abandoned);
        Assert.True(page.ReachedBoundary, "an abandoned walk does not continue, so it reports the boundary");
        Assert.Empty(page.Rows);
        Assert.True(progress.Last > 0);
        Assert.True(progress.Last * 10 < total, $"scanned {progress.Last} of {total} bytes; the walk should stop at the first timed-out event");
    }

    [Fact]
    public void A_later_read_with_the_abandoned_filter_stops_at_its_first_event()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, CatalogSnapshot snapshot, long total) = MultiFileSet(dir);
        var filter = new EventFilter(new FilterSpec(Text: new SearchQuery("(a+)+\\1b", CaseSensitive: false, WholeWord: false, Regex: true)));
        new HistoryReader(config).ReadNewest(snapshot, 1000, filter, null, CancellationToken.None);
        var progress = new Recorder();

        RowPage page = new HistoryReader(config).ReadOlder(snapshot, new RowKey(snapshot.Ordered[^1].Generation, snapshot.Ordered[^1].ReadCursor), 1000, filter, progress, CancellationToken.None);

        Assert.True(page.Abandoned);
        Assert.True(progress.Last * 10 < total);
    }

    [Fact]
    public void A_filter_that_is_not_abandoned_scans_the_whole_history_and_is_not_marked()
    {
        using var dir = new ConfigTestDirectory();
        (LogvuConfig config, CatalogSnapshot snapshot, long total) = MultiFileSet(dir);
        var filter = new EventFilter(new FilterSpec(MinLevel: LogLevel.Info));
        var progress = new Recorder();

        RowPage page = new HistoryReader(config).ReadNewest(snapshot, 1000, filter, progress, CancellationToken.None);

        Assert.False(page.Abandoned);
        Assert.False(filter.Abandoned);
        Assert.Equal(160, page.Rows.Count);
        Assert.Equal(total, progress.Last);
    }
}
