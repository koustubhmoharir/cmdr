using System.Text;
using Logvu.Core.Config;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Shared.Core;

namespace Logvu.Core.Tests.Store;

/// <summary>
/// The pre-checks that let a walk skip parsing must agree with the full test. Each rule is checked on the cases it must get right: a match only
/// in a continuation line, CRLF, a byte order mark, a Serilog message rendered from its template, and slog messages with escapes. Random events
/// check the header and text tests against <see cref="EventFilter.Matches"/> and <see cref="EventSearch.Matches"/>.
/// </summary>
public sealed class PrecheckTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Writes one closed file and returns the first row a backward search from the newest event finds for the pattern.</summary>
    private static EventRow? FindNewest(string format, byte[] content, SearchQuery query)
    {
        using var dir = new ConfigTestDirectory();
        File.WriteAllBytes(dir.Full("SheetKraft.log"), content);
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log", format: format);
        Catalog catalog = StoreFixtures.CatalogOf(dir, config, "SheetKraft.log");
        var filter = new EventSearchFilter(null, SearchPattern.Create(query, out _)!);
        RowPage page = new HistoryReader(config).ReadNewest(catalog.Snapshot(), 1, filter, null, CancellationToken.None);
        Assert.Empty(page.Errors);
        return page.Rows.Count == 0 ? null : page.Rows[0];
    }

    private static SearchQuery Literal(string text) => new(text, CaseSensitive: false, WholeWord: false, Regex: false);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void A_match_that_lies_only_in_a_continuation_line_finds_its_event()
    {
        byte[] content = Utf8(
            "INFO  2026-11-18 09:00:00,000 [1] A.B - first\n"
            + "   at Acme.Zebra.Run()\n"
            + "WARN  2026-11-18 09:00:01,000 [1] A.B - second\n");

        EventRow? row = FindNewest("log4net-core", content, Literal("zebra"));

        Assert.NotNull(row);
        Assert.Equal(0, row.Key.Offset);
        Assert.Equal("first", row.Preview);
    }

    [Fact]
    public void A_match_across_a_crlf_line_end_finds_the_event_by_its_normalized_text()
    {
        byte[] content = Utf8("INFO  2026-11-18 09:00:00,000 [1] A.B - alpha\r\n   beta\r\nWARN  2026-11-18 09:00:01,000 [1] A.B - other\r\n");

        EventRow? row = FindNewest("log4net-core", content, Literal("alpha\n   beta"));

        Assert.NotNull(row);
        Assert.Equal(0, row.Key.Offset);
        Assert.Null(FindNewest("log4net-core", content, Literal("alpha\r\n   beta")));
    }

    [Fact]
    public void A_byte_order_mark_does_not_hide_the_first_event()
    {
        byte[] content = [0xEF, 0xBB, 0xBF, .. Utf8("INFO  2026-11-18 09:00:00,000 [1] A.B - first\n")];

        EventRow? row = FindNewest("log4net-core", content, Literal("first"));

        Assert.NotNull(row);
        Assert.Equal(3, row.Key.Offset);
    }

    [Fact]
    public void A_serilog_message_rendered_from_its_template_is_found_though_the_text_lacks_it()
    {
        byte[] content = Utf8(
            "{\"Timestamp\":\"2026-11-18T09:00:00.0000000+00:00\",\"Level\":\"Information\",\"MessageTemplate\":\"Hello {Name}!\","
            + "\"Properties\":{\"Name\":\"World\"}}\n");

        EventRow? row = FindNewest("serilog-json", content, Literal("Hello World!"));

        Assert.NotNull(row);
        Assert.Equal(0, row.Key.Offset);
    }

    [Fact]
    public void A_slog_message_with_an_escape_is_found_by_its_decoded_text()
    {
        byte[] content = Utf8("{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"INFO\",\"msg\":\"caf\\u00e9 ok\"}\n");

        Assert.NotNull(FindNewest("slog-json", content, Literal("café ok")));
        Assert.Null(FindNewest("slog-json", content, Literal("café ok!")));
    }

    [Fact]
    public void A_slog_message_with_a_backslash_is_never_rejected_on_its_raw_text()
    {
        byte[] content = Utf8("{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"INFO\",\"msg\":\"a\\u0062c\"}\n");

        EventRow? row = FindNewest("slog-json", content, Literal("abc"));

        Assert.NotNull(row);
    }

    [Fact]
    public void A_slog_message_without_escapes_is_found_when_the_text_has_the_pattern()
    {
        byte[] content = Utf8("{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"INFO\",\"msg\":\"plain text\"}\n");

        Assert.NotNull(FindNewest("slog-json", content, Literal("plain")));
        Assert.Null(FindNewest("slog-json", content, Literal("absent")));
    }

    [Fact]
    public void A_whole_word_pattern_on_a_slog_message_is_kept_for_the_full_test()
    {
        byte[] content = Utf8("{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"INFO\",\"msg\":\"plain text\"}\n");

        EventRow? row = FindNewest("slog-json", content, new SearchQuery("plain", CaseSensitive: false, WholeWord: true, Regex: false));

        Assert.NotNull(row);
    }

    [Fact]
    public void A_literal_spanning_the_join_of_a_slog_message_finds_the_event()
    {
        byte[] content = Utf8(
            "{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"WARN\",\"msg\":\"failed to register with CodeKraft\","
            + "\"error\":{\"message\":\"request cannot be made\"}}\n");

        Assert.NotNull(FindNewest("slog-json", content, Literal("CodeKraft: request")));
        Assert.NotNull(FindNewest("slog-json", content, Literal(" request")));
        Assert.Null(FindNewest("slog-json", content, Literal("CodeKraft:  request")));
    }

    [Fact]
    public void A_single_word_absent_from_a_slog_line_finds_nothing()
    {
        byte[] content = Utf8(
            "{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"WARN\",\"msg\":\"retrying\","
            + "\"cause\":[{\"message\":\"inner\"},\"connection refused\"]}\n");

        Assert.Null(FindNewest("slog-json", content, Literal("absent")));
    }

    [Fact]
    public void A_single_word_present_only_in_a_slog_cause_string_finds_the_event()
    {
        byte[] content = Utf8(
            "{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"WARN\",\"msg\":\"retrying\","
            + "\"err\":{\"message\":\"db down\",\"cause\":[\"connection-refused\"]}}\n");

        EventRow? row = FindNewest("slog-json", content, Literal("refused"));

        Assert.NotNull(row);
        Assert.Equal(0, row.Key.Offset);
    }

    [Fact]
    public void Slog_message_check_is_exact_for_random_escape_free_and_escaped_messages()
    {
        var rng = new Random(20261203);
        string[] pieces = ["ab", "Ab", "x", "é", "a b", "\\", "\"", "\\u0062", "end"];
        ILogScheme scheme = BuiltInSchemes.SlogJson;
        var context = new ParseContext(TimeZoneInfo.Utc);
        for (int i = 0; i < 3000; i++)
        {
            var message = new StringBuilder();
            int count = rng.Next(0, 5);
            for (int piece = 0; piece < count; piece++)
            {
                message.Append(pieces[rng.Next(pieces.Length)]);
            }

            string json = "{\"msg\":" + System.Text.Json.JsonSerializer.Serialize(message.ToString(), new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "}";
            if (rng.Next(2) == 0)
            {
                json = json.Replace("c", "\\u0063");
            }

            string pattern = rng.Next(3) switch
            {
                0 => pieces[rng.Next(pieces.Length)],
                1 => pieces[rng.Next(pieces.Length)] + pieces[rng.Next(pieces.Length)],
                _ => "zz",
            };
            SearchPattern search = SearchPattern.Create(Literal(pattern), out _)!;
            LogEvent ev = scheme.Parse(json, context);

            bool full = EventSearch.Matches(search, ev, json);
            bool may = EventSearch.MayMatch(search, json, scheme);
            Assert.True(!full || may, $"'{json}' pattern '{pattern}': the check rejects an event that matches");
        }
    }

    [Fact]
    public void Header_test_never_rejects_an_event_the_full_filter_accepts()
    {
        var rng = new Random(20261204);
        string[] levels = ["INFO", "WARN", "ERROR", "DEBUG", "FATAL", "info+2", "notice", "Trace"];
        string[] loggers = ["Acme.Orders", "Sync", "acme.billing"];
        string[] words = ["alpha", "Beta", "timeout", "x", "é"];
        var context = new ParseContext(TimeZoneInfo.Utc);
        for (int i = 0; i < 2000; i++)
        {
            DateTimeOffset time = T0.AddSeconds(rng.Next(-600, 600));
            string header = $"{levels[rng.Next(levels.Length)],-5} {time:yyyy-MM-dd HH:mm:ss,fff} [{rng.Next(3)}] {loggers[rng.Next(loggers.Length)]} - {words[rng.Next(words.Length)]}";
            string text = rng.Next(3) == 0 ? "junk " + header : header + (rng.Next(2) == 0 ? "\n   at " + words[rng.Next(words.Length)] : string.Empty);
            if (rng.Next(6) == 0)
            {
                text = "  " + text;
            }

            var extent = new EventExtent(0, Utf8(text).Length, 0, Provisional: false, Orphan: false);
            EventRow head = RowBuilder.BuildHead(text, BuiltInSchemes.Log4NetCore, 1, extent, context)!;
            (EventRow row, LogEvent ev) = RowBuilder.BuildFromText(text, BuiltInSchemes.Log4NetCore, 1, extent, context);
            Assert.Equal(row.Time, head.Time);
            Assert.Equal(row.TimeText, head.TimeText);
            Assert.Equal(row.Level, head.Level);
            Assert.Equal(row.LevelText, head.LevelText);
            Assert.Equal(row.IsRaw, head.IsRaw);
            Assert.Equal(row.Key, head.Key);
            Assert.Equal(string.Empty, head.Preview);

            var filter = new EventFilter(new FilterSpec(
                MinLevel: rng.Next(3) == 0 ? LogLevel.Warn : null,
                From: rng.Next(3) == 0 ? T0.AddSeconds(-100) : null,
                To: rng.Next(4) == 0 ? T0 : null));
            bool full = filter.Matches(row, ev, text);
            Assert.True(!full || filter.MayMatchHead(head), $"'{text}': the header test rejects an event that matches");

            // A filter with no time bound gets a header row without its time, which must decide the same way.
            EventRow headNoTime = RowBuilder.BuildHead(text, BuiltInSchemes.Log4NetCore, 1, extent, context, withTime: false)!;
            Assert.Null(headNoTime.Time);
            Assert.Null(headNoTime.TimeText);
            if (!filter.ChecksHeadTime)
            {
                Assert.Equal(filter.MayMatchHead(head), filter.MayMatchHead(headNoTime));
            }
        }
    }
}
