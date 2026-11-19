using System.Diagnostics;
using System.Globalization;
using System.Text;
using Logvu.Core.Config;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Logvu.Core.Tests.Store;
using Shared.Core;
using Xunit.Abstractions;

namespace Logvu.Core.Tests.Perf;

/// <summary>
/// Scan throughput of the history reader, measured on generated sets. The tests return at once unless the environment variable
/// <c>LOGVU_PERF=1</c> is set, so ordinary test runs never pay for the data or the timings. Run them in Release:
/// <c>LOGVU_PERF=1 dotnet test tests/Logvu.Core.Tests -c Release --filter FullyQualifiedName~ScanThroughput --logger "console;verbosity=detailed"</c>.
/// </summary>
/// <remarks>
/// The data is generated once per test process, in a temporary directory that is deleted when the process exits. The log4net-core set
/// is about 200 MB in six files, two events in three of them one line long and the rest three-line exceptions; the slog set is about
/// 100 MB of JSON lines in three files. The set is written just before the scans, so the OS cache is warm. Each scan runs three times and
/// the best run is reported, with the bytes it allocated and the gen0 collections it caused.
/// </remarks>
public sealed class ScanThroughputTests(ITestOutputHelper output)
{
    private const string EnableVariable = "LOGVU_PERF";
    private const long Log4NetTargetBytes = 200L * 1024 * 1024;
    private const long SlogTargetBytes = 100L * 1024 * 1024;
    private const int Log4NetFiles = 6;
    private const int SlogFiles = 3;

    /// <summary>A literal that appears in no generated line, so a search scans every event.</summary>
    private const string NeverMatches = "zq-no-such-text-xj";

    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Lazy<PerfSets> Sets = new(PerfSets.Generate);

    private static bool Enabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    [Fact]
    public void Backward_literal_scan_of_log4net_set()
    {
        if (!Enabled)
        {
            return;
        }

        PerfSet set = Sets.Value.Log4Net;
        IRowFilter filter = NeverMatchingFilter();
        var reader = new HistoryReader(set.Config);
        (RowPage page, TimeSpan elapsed) = Time(() => reader.ReadNewest(set.Snapshot(), 1, filter, null, CancellationToken.None));

        Report("(a) log4net backward literal scan", set, elapsed);
        Assert.Empty(page.Rows);
        Assert.True(page.ReachedBoundary);
        Assert.Empty(page.Errors);
    }

    [Fact]
    public void Forward_literal_scan_of_log4net_set()
    {
        if (!Enabled)
        {
            return;
        }

        PerfSet set = Sets.Value.Log4Net;
        IRowFilter filter = NeverMatchingFilter();
        var reader = new HistoryReader(set.Config);
        CatalogSnapshot snapshot = set.Snapshot();
        RowKey oldest = new(snapshot.Ordered[0].Generation, 0);
        (RowPage page, TimeSpan elapsed) = Time(() => reader.ReadNewer(snapshot, oldest, 1, filter, null, CancellationToken.None, inclusive: true));

        Report("(b) log4net forward literal scan", set, elapsed);
        Assert.Empty(page.Rows);
        Assert.True(page.ReachedBoundary);
        Assert.Empty(page.Errors);
    }

    [Fact]
    public void Backward_level_filter_scan_of_log4net_set()
    {
        if (!Enabled)
        {
            return;
        }

        PerfSet set = Sets.Value.Log4Net;
        var filter = new EventFilter(new FilterSpec(MinLevel: LogLevel.Error));
        var reader = new HistoryReader(set.Config);
        (RowPage page, TimeSpan elapsed) = Time(() => reader.ReadNewest(set.Snapshot(), int.MaxValue, filter, null, CancellationToken.None));

        Report("(c) log4net backward level >= ERROR scan", set, elapsed);
        Assert.Equal(Sets.Value.Log4NetErrors, page.Rows.Count);
        Assert.True(page.ReachedBoundary);
        Assert.Empty(page.Errors);
    }

    [Fact]
    public void Backward_literal_scan_of_slog_set()
    {
        if (!Enabled)
        {
            return;
        }

        PerfSet set = Sets.Value.Slog;
        IRowFilter filter = NeverMatchingFilter();
        var reader = new HistoryReader(set.Config);
        (RowPage page, TimeSpan elapsed) = Time(() => reader.ReadNewest(set.Snapshot(), 1, filter, null, CancellationToken.None));

        Report("(d) slog-json backward literal scan", set, elapsed);
        Assert.Empty(page.Rows);
        Assert.True(page.ReachedBoundary);
        Assert.Empty(page.Errors);
    }

    /// <summary>The search predicate of the UI's default search: a case-insensitive literal, with its rendered-message rule for JSON.</summary>
    private static IRowFilter NeverMatchingFilter()
    {
        SearchPattern pattern = SearchPattern.Create(new SearchQuery(NeverMatches, CaseSensitive: false, WholeWord: false, Regex: false), out _)!;
        return new EventSearchFilter(null, pattern);
    }

    /// <summary>The number of timed runs of each scan. The best is reported: the machine's other load only ever makes a run slower.</summary>
    private const int Runs = 3;

    /// <summary>The allocation and collection counts taken before the first timed run, which <see cref="Report"/> subtracts.</summary>
    private static (long Bytes, int Collections) _mark;

    private static (T Result, TimeSpan Elapsed) Time<T>(Func<T> scan)
    {
        _mark = (GC.GetTotalAllocatedBytes(precise: false), GC.CollectionCount(0));
        T result = default!;
        TimeSpan best = TimeSpan.MaxValue;
        for (int run = 0; run < Runs; run++)
        {
            var watch = Stopwatch.StartNew();
            result = scan();
            watch.Stop();
            best = TimeSpan.FromTicks(Math.Min(best.Ticks, watch.Elapsed.Ticks));
        }

        return (result, best);
    }

    private void Report(string name, PerfSet set, TimeSpan elapsed)
    {
        double megabytes = set.TotalBytes / (1024.0 * 1024.0);
        double seconds = elapsed.TotalSeconds;
        double allocated = (GC.GetTotalAllocatedBytes(precise: false) - _mark.Bytes) / (1024.0 * 1024.0) / Runs;
        int collections = (GC.CollectionCount(0) - _mark.Collections) / Runs;
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: {megabytes:F1} MB in {seconds:F2} s (best of {Runs}) = {megabytes / seconds:F1} MB/s ({set.Files} files; allocated {allocated:F0} MB, {collections} gen0 GCs per run)"));
    }

    /// <summary>One generated set: its configuration, its directory and its total size.</summary>
    private sealed class PerfSet(LogvuConfig config, Catalog catalog, int files, long totalBytes)
    {
        public LogvuConfig Config { get; } = config;

        public int Files { get; } = files;

        public long TotalBytes { get; } = totalBytes;

        private Catalog Catalog { get; } = catalog;

        /// <summary>The timeline as the store would publish it: every member probed, read cursors at the ends of the files.</summary>
        public CatalogSnapshot Snapshot() => Catalog.Snapshot();
    }

    /// <summary>The generated sets, with the directories they live in. Both are deleted when the process exits.</summary>
    private sealed class PerfSets(PerfSet log4Net, PerfSet slog, int log4NetErrors)
    {
        public PerfSet Log4Net { get; } = log4Net;

        public PerfSet Slog { get; } = slog;

        /// <summary>The number of ERROR events written to the log4net set, which the level scan must find.</summary>
        public int Log4NetErrors { get; } = log4NetErrors;

        public static PerfSets Generate()
        {
            var log4NetDirectory = new ConfigTestDirectory();
            var slogDirectory = new ConfigTestDirectory();
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                log4NetDirectory.Dispose();
                slogDirectory.Dispose();
            };

            (string[] log4NetNames, int errors) = WriteLog4Net(log4NetDirectory, Log4NetFiles, Log4NetTargetBytes);
            LogvuConfig log4NetConfig = StoreTestConfig.Create(
                log4NetDirectory,
                prefix: "SheetKraft",
                pattern: StoreTestConfig.SheetKraftPattern,
                activeFile: "SheetKraft.log",
                format: "log4net-core");
            PerfSet log4Net = Build(log4NetConfig, log4NetDirectory, log4NetNames);

            string[] slogNames = WriteSlog(slogDirectory, SlogFiles, SlogTargetBytes);
            LogvuConfig slogConfig = StoreTestConfig.Create(
                slogDirectory,
                prefix: "Slog",
                pattern: @"^Slog(?:[0-9]+)?[.]log$",
                activeFile: "Slog.log",
                format: "slog-json");
            PerfSet slog = Build(slogConfig, slogDirectory, slogNames);

            return new PerfSets(log4Net, slog, errors);
        }

        private static PerfSet Build(LogvuConfig config, ConfigTestDirectory dir, string[] names)
        {
            Catalog catalog = StoreFixtures.CatalogOf(dir, config, names);
            long total = names.Sum(name => new FileInfo(dir.Full(name)).Length);
            return new PerfSet(config, catalog, names.Length, total);
        }

        /// <summary>
        /// Writes the log4net-core files, oldest first, the active file last. Each event is one line of about 120 bytes, except every
        /// ERROR, which has two continuation lines: a three-line exception.
        /// </summary>
        private static (string[] Names, int Errors) WriteLog4Net(ConfigTestDirectory dir, int files, long targetBytes)
        {
            var random = new Random(20261118);
            long perFile = targetBytes / files;
            string[] loggers = ["Acme.Orders.OrderService", "Acme.Orders.Pricing", "Acme.Billing.Invoices", "Acme.Web.Http", "Acme.Sync.Worker", "Acme.Data.Repository"];
            string[] messages =
            [
                "Order {0} accepted for customer {1}, 3 lines, total {2}",
                "Cache hit for key sku:{1} in region eu-west-{0}",
                "Retrying request {0} after a transient failure; attempt {2} of 5",
                "Batch {0} flushed: {2} rows written in {1} ms",
            ];
            int errors = 0;
            var names = new string[files];
            DateTimeOffset time = Start;
            for (int file = 0; file < files; file++)
            {
                names[file] = file == files - 1 ? "SheetKraft.log" : $"SheetKraft{files - 1 - file}.log";
                using var writer = new StreamWriter(dir.Full(names[file]), false, new UTF8Encoding(false), 1 << 20);
                long written = 0;
                while (written < perFile)
                {
                    time = time.AddMilliseconds(random.Next(1, 30));
                    int roll = random.Next(100);
                    string level = roll < 72 ? "INFO" : roll < 86 ? "DEBUG" : roll < 92 ? "WARN" : "ERROR";
                    string logger = loggers[random.Next(loggers.Length)];
                    string message = string.Format(
                        CultureInfo.InvariantCulture,
                        messages[random.Next(messages.Length)],
                        random.Next(100000, 999999),
                        random.Next(1, 500),
                        random.Next(1, 40));
                    string header = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{level,-5} {time:yyyy-MM-dd HH:mm:ss,fff} [Thread {random.Next(1, 64)}] {logger} - {message}");
                    writer.Write(header);
                    writer.Write('\n');
                    written += Encoding.UTF8.GetByteCount(header) + 1;
                    if (level == "ERROR")
                    {
                        errors++;
                        string exception = $"System.InvalidOperationException: Order {random.Next(100000, 999999)} rejected by the pricing rules\n";
                        string frame = $"   at Acme.Orders.Pricing.Quote.Compute(Int32 id) in C:\\src\\Quote.cs:line {random.Next(10, 400)}\n";
                        writer.Write(exception);
                        writer.Write(frame);
                        written += Encoding.UTF8.GetByteCount(exception) + Encoding.UTF8.GetByteCount(frame);
                    }
                }
            }

            return (names, errors);
        }

        /// <summary>Writes the slog JSON files, oldest first, the active file last: about 150 bytes per line, one in 200 an error object.</summary>
        private static string[] WriteSlog(ConfigTestDirectory dir, int files, long targetBytes)
        {
            var random = new Random(20261119);
            long perFile = targetBytes / files;
            string[] regions = ["eu-west", "us-east", "ap-south"];
            var names = new string[files];
            DateTimeOffset time = Start;
            for (int file = 0; file < files; file++)
            {
                names[file] = file == files - 1 ? "Slog.log" : $"Slog{files - 1 - file}.log";
                using var writer = new StreamWriter(dir.Full(names[file]), false, new UTF8Encoding(false), 1 << 20);
                long written = 0;
                while (written < perFile)
                {
                    time = time.AddMilliseconds(random.Next(1, 30));
                    int roll = random.Next(200);
                    string level = roll == 0 ? "ERROR" : random.Next(10) < 7 ? "INFO" : "DEBUG";
                    string line = roll == 0
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"{{\"time\":\"{time:yyyy-MM-ddTHH:mm:ss.fffffffK}\",\"level\":\"{level}\",\"msg\":\"charge failed\",\"logger\":\"billing\",\"orderId\":{random.Next(100000, 999999)},\"err\":{{\"message\":\"card declined\",\"attrs\":{{\"code\":\"51\"}}}}}}")
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"{{\"time\":\"{time:yyyy-MM-ddTHH:mm:ss.fffffffK}\",\"level\":\"{level}\",\"msg\":\"order processed\",\"logger\":\"orders\",\"orderId\":{random.Next(100000, 999999)},\"durationMs\":{random.Next(1, 900)}.{random.Next(10)},\"region\":\"{regions[random.Next(regions.Length)]}\",\"items\":{random.Next(1, 20)}}}");
                    writer.Write(line);
                    writer.Write('\n');
                    written += Encoding.UTF8.GetByteCount(line) + 1;
                }
            }

            return names;
        }
    }
}
