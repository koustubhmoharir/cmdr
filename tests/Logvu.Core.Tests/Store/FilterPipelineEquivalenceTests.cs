using System.Globalization;
using System.Text;
using System.Text.Json;
using Logvu.Core.Config;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Shared.Core;
using static Logvu.Core.Tests.Store.StoreFixtures;

namespace Logvu.Core.Tests.Store;

/// <summary>
/// The filtered history walk takes shortcuts: it checks the header line and the raw text before it parses an event, and it reads the text from
/// the framer's own blocks. These tests hold every page of such a walk to a brute-force reference, which frames every event, parses it, and
/// keeps the rows that the full filter matches. Random sets cover BOMs, CRLF, orphan runs, blank lines, matches that lie only in continuation
/// lines, escapes in JSON, Serilog messages that are rendered from templates, cursors inside files, and batch sizes of one and more.
/// </summary>
public sealed class FilterPipelineEquivalenceTests
{
    private const int Cases = 20;
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] Vocabulary =
    [
        "alpha", "Beta", "gamma", "timeout", "x\\y", "Hello World", "café", "a.b", "ERROR", "warn", "K", "zz-end", "quote\"d", "tab\there",
    ];

    private static readonly string[] Levels = ["INFO", "DEBUG", "WARN", "ERROR", "FATAL", "Info", "error", "TRACE", "ALERT", "NOTICE"];
    private static readonly string[] Loggers = ["Acme.Orders", "Acme.Billing.Invoices", "Sync", "Acme.Web"];
    private static readonly string[] Threads = ["Thread 1", "main", "worker-2"];

    /// <summary>A random set of files: its configuration and its catalog.</summary>
    private sealed record RandomSet(LogvuConfig Config, Catalog Catalog);

    /// <summary>One reference row: the row, its parsed event and its text, as the full path builds them.</summary>
    private readonly record struct Reference(EventRow Row, LogEvent Event, string Text);

    [Fact]
    public void Log4net_pages_with_random_filters_match_the_brute_force_reference()
    {
        for (int i = 0; i < Cases; i++)
        {
            using var dir = new ConfigTestDirectory();
            var rng = new Random(1000 + i);
            RandomSet set = WriteSet(dir, "log4net-core", rng);
            CheckSet(set, i, 1000 + i);
        }
    }

    [Fact]
    public void Slog_pages_with_random_filters_match_the_brute_force_reference()
    {
        for (int i = 0; i < Cases; i++)
        {
            using var dir = new ConfigTestDirectory();
            var rng = new Random(2000 + i);
            RandomSet set = WriteSet(dir, "slog-json", rng);
            CheckSet(set, i, 2000 + i);
        }
    }

    [Fact]
    public void Serilog_pages_with_random_filters_match_the_brute_force_reference()
    {
        for (int i = 0; i < Cases; i++)
        {
            using var dir = new ConfigTestDirectory();
            var rng = new Random(3000 + i);
            RandomSet set = WriteSet(dir, "serilog-json", rng);
            CheckSet(set, i, 3000 + i);
        }
    }

    /// <summary>Checks the pages of one random set under several filters, reader batch sizes and page sizes.</summary>
    private static void CheckSet(RandomSet set, int caseIndex, int seed)
    {
        var rng = new Random(seed * 7 + 1);
        List<Reference> all = ReferenceRows(set.Catalog, set.Config);
        CatalogSnapshot snapshot = set.Catalog.Snapshot();

        for (int round = 0; round < 4; round++)
        {
            IRowFilter filter = RandomFilter(all, snapshot, rng);
            IRowFilter bound = filter is IBindableRowFilter bindable ? bindable.Bind(snapshot) : filter;
            List<int> matches = [];
            for (int index = 0; index < all.Count; index++)
            {
                Reference row = all[index];
                if (bound.Matches(row.Row, row.Event, row.Text))
                {
                    matches.Add(index);
                }
            }

            var reader = new HistoryReader(set.Config) { BatchEvents = rng.Next(1, 6), BatchBytes = rng.Next(1, 400) };
            if (rng.Next(3) == 0)
            {
                reader.BatchEvents = 4096;
                reader.BatchBytes = 16L * 1024 * 1024;
            }

            string label = $"case {caseIndex}, round {round}, filter {DescribeFilter(filter)}, batch {reader.BatchEvents}/{reader.BatchBytes}";
            try
            {
                CheckNewest(reader, snapshot, all, matches, bound, rng);
                CheckOlder(reader, snapshot, all, matches, bound, rng);
                CheckNewer(reader, snapshot, all, matches, bound, rng);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(label, ex);
            }
        }
    }

    private static void CheckNewest(HistoryReader reader, CatalogSnapshot snapshot, List<Reference> all, List<int> matches, IRowFilter filter, Random rng)
    {
        foreach (int count in new[] { 1, rng.Next(2, 9), all.Count + 5 })
        {
            RowPage page = reader.ReadNewest(snapshot, count, filter, null, CancellationToken.None);
            List<EventRow> expected = matches.TakeLast(count).Select(index => all[index].Row).ToList();
            Assert.Empty(page.Errors);
            Assert.Equal(expected, page.Rows);
            Assert.Equal(matches.Count < count, page.ReachedBoundary);
        }
    }

    private static void CheckOlder(HistoryReader reader, CatalogSnapshot snapshot, List<Reference> all, List<int> matches, IRowFilter filter, Random rng)
    {
        for (int probe = 0; probe < 4 && all.Count > 0; probe++)
        {
            int anchor = rng.Next(all.Count);
            int count = rng.Next(1, 7);
            RowPage page = reader.ReadOlder(snapshot, all[anchor].Row.Key, count, filter, null, CancellationToken.None);
            List<EventRow> expected = matches.Where(index => index < anchor).TakeLast(count).Select(index => all[index].Row).ToList();
            Assert.Empty(page.Errors);
            Assert.Equal(expected, page.Rows);
            Assert.Equal(matches.Count(index => index < anchor) < count, page.ReachedBoundary);
        }
    }

    private static void CheckNewer(HistoryReader reader, CatalogSnapshot snapshot, List<Reference> all, List<int> matches, IRowFilter filter, Random rng)
    {
        for (int probe = 0; probe < 4 && all.Count > 0; probe++)
        {
            int anchor = rng.Next(all.Count);
            int count = rng.Next(1, 7);
            bool inclusive = rng.Next(2) == 0;
            RowPage page = reader.ReadNewer(snapshot, all[anchor].Row.Key, count, filter, null, CancellationToken.None, inclusive);
            List<EventRow> expected = matches.Where(index => inclusive ? index >= anchor : index > anchor).Take(count).Select(index => all[index].Row).ToList();
            Assert.Empty(page.Errors);
            Assert.Equal(expected, page.Rows);
            Assert.Equal(matches.Count(index => inclusive ? index >= anchor : index > anchor) < count, page.ReachedBoundary);
        }
    }

    /// <summary>
    /// A random filter: an event filter with random predicates, or a search over one (or over no filter). A search binds its event filter to
    /// the snapshot before it wraps it, as the store's search does.
    /// </summary>
    private static IRowFilter RandomFilter(List<Reference> all, CatalogSnapshot snapshot, Random rng)
    {
        if (rng.Next(2) == 0)
        {
            return new EventFilter(RandomSpec(rng));
        }

        IRowFilter? inner = rng.Next(3) == 0 ? null : new EventFilter(RandomSpec(rng)).Bind(snapshot);
        return new EventSearchFilter(inner, RandomPattern(all, rng));
    }

    private static FilterSpec RandomSpec(Random rng)
    {
        LogLevel? minLevel = rng.Next(3) == 0 ? (LogLevel)rng.Next(1, 7) : null;
        IReadOnlySet<LogLevel>? levels = rng.Next(5) == 0 ? new HashSet<LogLevel> { LogLevel.Warn, LogLevel.Error } : null;
        string? file = rng.Next(5) == 0 ? "SheetKraft2" : null;
        DateTimeOffset? from = null;
        DateTimeOffset? to = null;
        if (rng.Next(4) == 0)
        {
            from = T0.AddSeconds(rng.Next(0, 900));
        }

        if (rng.Next(4) == 0)
        {
            to = (from ?? T0).AddSeconds(rng.Next(0, 900));
        }

        SearchQuery? text = rng.Next(2) == 0 ? RandomQuery(rng) : null;
        var fields = new List<FieldPredicate>();
        if (rng.Next(5) == 0)
        {
            fields.Add(new FieldPredicate(rng.Next(2) == 0 ? "thread" : "msg", FieldOperator.Equals, Vocabulary[rng.Next(Vocabulary.Length)]));
        }

        if (rng.Next(4) == 0)
        {
            // A logger predicate is a predicate on the logger field.
            fields.Add(new FieldPredicate("logger", (FieldOperator)rng.Next(4), rng.Next(4) == 0 ? "zz" : "Acme"));
        }

        return new FilterSpec(minLevel, levels, file, from, to, text, fields.Count == 0 ? null : fields);
    }

    private static SearchQuery RandomQuery(Random rng)
    {
        string[] regexes = ["Hello.*World", "^INFO", "timeout$", "\\bwarn\\b", "x\\\\y", "a\\.b", "[Ee]rror"];
        bool regex = rng.Next(3) == 0;
        string text = regex ? regexes[rng.Next(regexes.Length)] : Vocabulary[rng.Next(Vocabulary.Length)];
        return new SearchQuery(text, CaseSensitive: rng.Next(3) == 0, WholeWord: !regex && rng.Next(4) == 0, Regex: regex);
    }

    /// <summary>A search pattern: a literal from the vocabulary, or a regex, the way the search box makes them.</summary>
    private static SearchPattern RandomPattern(List<Reference> all, Random rng) =>
        SearchPattern.Create(RandomQuery(rng), out _) ?? throw new InvalidOperationException($"No pattern for {all.Count} rows.");

    private static string DescribeFilter(IRowFilter filter) => filter switch
    {
        EventFilter event_ => $"event[{event_.Describe()}]",
        EventSearchFilter => "search",
        _ => filter.GetType().Name,
    };

    /// <summary>Every event of every readable member, in timeline order, framed forward and built the full way.</summary>
    private static List<Reference> ReferenceRows(Catalog catalog, LogvuConfig config)
    {
        var context = new ParseContext(config.TimeZone);
        var rows = new List<Reference>();
        foreach (Member member in catalog.Ordered.Where(m => m.IsReadable))
        {
            using FileStream stream = Open(member.Path);
            var framer = new EventFramer(stream, member.Scheme!, live: false);
            foreach (EventExtent extent in framer.ReadForward(0))
            {
                if (extent.End > member.ReadCursor)
                {
                    break;
                }

                (EventRow row, LogEvent ev, string text) = RowBuilder.Build(stream, member.Scheme, member.Generation, extent, context);
                rows.Add(new Reference(row, ev, text));
            }
        }

        return rows;
    }

    // ---- Random sets ----------------------------------------------------------------------------------------

    private static RandomSet WriteSet(ConfigTestDirectory dir, string format, Random rng)
    {
        int files = rng.Next(1, 5);
        var names = new List<string>();
        for (int file = 0; file < files; file++)
        {
            string name = file == files - 1 ? "SheetKraft.log" : $"SheetKraft{files - 1 - file}.log";
            names.Add(name);
            DateTimeOffset start = T0.AddMinutes(file * 30);
            var text = new StringBuilder();
            if (rng.Next(3) == 0)
            {
                text.Append(RandomEol(rng) == "\r\n" ? "orphan line one\r\n" : "orphan line one\n");
            }

            int events = rng.Next(0, 25);
            for (int e = 0; e < events; e++)
            {
                DateTimeOffset time = start.AddSeconds(e * rng.Next(1, 9) + rng.Next(0, 3));
                string eol = RandomEol(rng);
                switch (format)
                {
                    case "log4net-core":
                        text.Append(TextEvent(rng, time, eol));
                        break;
                    case "slog-json":
                        text.Append(SlogLine(rng, time)).Append(eol);
                        break;
                    default:
                        text.Append(SerilogLine(rng, time)).Append(eol);
                        break;
                }
            }

            if (rng.Next(4) == 0 && format == "log4net-core")
            {
                text.Append("ERROR 2026-11-18 09:59:59,999 [x] Acme.Tail - unterminated tail");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text.ToString());
            if (rng.Next(3) == 0)
            {
                bytes = [0xEF, 0xBB, 0xBF, .. bytes];
            }

            File.WriteAllBytes(dir.Full(name), bytes);
        }

        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log", format: format);
        Catalog catalog = StoreFixtures.CatalogOf(dir, config, names.ToArray());

        // Some members are read only up to an event boundary, as a live file's read cursor is.
        foreach (Member member in catalog.Ordered)
        {
            if (rng.Next(3) == 0)
            {
                EventExtent[] extents = Extents(member);
                if (extents.Length > 0)
                {
                    member.ReadCursor = extents[rng.Next(extents.Length)].Start;
                }
            }
        }

        return new RandomSet(config, catalog);
    }

    private static string RandomEol(Random rng) => rng.Next(3) == 0 ? "\r\n" : "\n";

    /// <summary>A log4net-core event: a header, and sometimes continuation lines, some carrying vocabulary and some blank.</summary>
    private static string TextEvent(Random rng, DateTimeOffset time, string eol)
    {
        string level = Levels[rng.Next(Levels.Length)];
        string logger = Loggers[rng.Next(Loggers.Length)];
        string thread = Threads[rng.Next(Threads.Length)];
        string message = Words(rng);
        if (rng.Next(12) == 0)
        {
            // A junk line that is not a header, as an orphan or a continuation.
            return "  plain " + message + eol;
        }

        var text = new StringBuilder();
        text.Append($"{level,-5} {time:yyyy-MM-dd HH:mm:ss,fff} [{thread}] {logger} - {message}{eol}");
        int continuation = rng.Next(4) == 0 ? rng.Next(1, 4) : 0;
        for (int line = 0; line < continuation; line++)
        {
            string body = rng.Next(3) == 0 ? "   at Acme.Run(Int32 id) in C:\\src\\Run.cs:line 88" : Vocabulary[rng.Next(Vocabulary.Length)];
            text.Append(rng.Next(4) == 0 ? string.Empty : "   " + body).Append(eol);
        }

        return text.ToString();
    }

    private static string Words(Random rng)
    {
        int count = rng.Next(1, 4);
        var words = new string[count];
        for (int i = 0; i < count; i++)
        {
            words[i] = Vocabulary[rng.Next(Vocabulary.Length)];
        }

        return string.Join(' ', words);
    }

    /// <summary>A Serilog JsonFormatter line: a template with properties, sometimes a rendered message, an exception, and escapes.</summary>
    private static string SerilogLine(Random rng, DateTimeOffset time)
    {
        string[] templates = ["Order {Id} placed by {Name}", "Hello {Name}", "Plain text", "Literal {{braces}} {Name:l}", "{Count,5} items {@Obj}", "{$Name} done"];
        var properties = new Dictionary<string, object?>
        {
            ["Id"] = rng.Next(1, 999),
            ["Name"] = Vocabulary[rng.Next(Vocabulary.Length)],
            ["Count"] = rng.Next(0, 50),
            ["Obj"] = new Dictionary<string, object?> { ["k"] = Vocabulary[rng.Next(Vocabulary.Length)] },
            ["SourceContext"] = Loggers[rng.Next(Loggers.Length)],
        };
        var line = new Dictionary<string, object?>
        {
            ["Timestamp"] = time.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK", CultureInfo.InvariantCulture),
            ["Level"] = new[] { "Information", "Warning", "Error", "Debug" }[rng.Next(4)],
            ["MessageTemplate"] = templates[rng.Next(templates.Length)],
            ["Properties"] = properties,
        };
        if (rng.Next(3) == 0)
        {
            line["RenderedMessage"] = Words(rng);
        }

        if (rng.Next(5) == 0)
        {
            line["Exception"] = "System.InvalidOperationException: " + Vocabulary[rng.Next(Vocabulary.Length)] + "\n   at Acme.Run()";
        }

        return JsonSerializer.Serialize(line);
    }

    /// <summary>A slog JSONHandler line: a level with an offset, a message that may be an escaped string or a value of another kind.</summary>
    private static string SlogLine(Random rng, DateTimeOffset time)
    {
        string[] levels = ["INFO", "WARN+2", "DEBUG-4", "ERROR", "info"];
        object message = rng.Next(8) switch
        {
            0 => rng.Next(1000),
            1 => new Dictionary<string, object?> { ["text"] = Vocabulary[rng.Next(Vocabulary.Length)] },
            2 => Vocabulary[rng.Next(Vocabulary.Length)] + "\\n",
            _ => Words(rng),
        };
        var line = new Dictionary<string, object?>
        {
            ["time"] = time.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
            ["level"] = levels[rng.Next(levels.Length)],
            ["msg"] = message,
            ["logger"] = Loggers[rng.Next(Loggers.Length)],
            ["orderId"] = rng.Next(1, 999),
        };
        if (rng.Next(4) == 0)
        {
            line["err"] = new Dictionary<string, object?> { ["message"] = Vocabulary[rng.Next(Vocabulary.Length)] };
        }

        return JsonSerializer.Serialize(line);
    }
}
