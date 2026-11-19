using System.Text.Json;
using Logvu.Core.Config;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Logvu.Core.Tests.Store;
using Shared.Core;

namespace Logvu.Core.Tests.Filter;

public sealed class EventFilterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly ParseContext Context = new(TimeZoneInfo.Utc);

    private const string Log4NetLine = "ERROR 2026-11-18 09:00:00,000 [Worker 3] Orders.Engine - timeout talking to gateway\n";
    private const string SerilogLine =
        """{"Timestamp":"2026-11-18T10:20:30.1234567+05:30","Level":"Error","MessageTemplate":"Failed to process {OrderId} for {Customer}","Properties":{"OrderId":42,"Customer":{"Name":"Asha"},"Scope":["Request 7"],"SourceContext":"Orders.Engine"}}""";

    /// <summary>Parses a text with its scheme, as the store does, and returns its row, its event and the text.</summary>
    private static (EventRow Row, LogEvent Event, string Text) Parse(ILogScheme scheme, string text, int generation = 1)
    {
        var extent = new EventExtent(0, text.Length, 0, false, false);
        (EventRow row, LogEvent ev) = RowBuilder.BuildFromText(text, scheme, generation, extent, Context);
        return (row, ev, text);
    }

    /// <summary>An event of a JSON value, with a row that has no level, time or source.</summary>
    private static (EventRow Row, LogEvent Event, string Text) JsonEvent(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var ev = new LogEvent { Message = "message", Json = document.RootElement.Clone() };
        return (Row(), ev, json);
    }

    /// <summary>A row of generation 1 with the given level and time.</summary>
    private static EventRow Row(int generation = 1, LogLevel level = LogLevel.Info, DateTimeOffset? time = null) =>
        new(
            new RowKey(generation, 0),
            new EventExtent(0, 0, 0, false, false),
            time,
            time?.ToString("O"),
            level,
            null,
            "preview",
            false,
            false,
            false);

    /// <summary>An event with the given message and no fields, for predicates that read only the row or the text.</summary>
    private static LogEvent Plain(string message = "message") => new() { Message = message };

    /// <summary>A JSON event of the given text, with the given message: the message of a slog event is its <c>msg</c> member.</summary>
    private static (EventRow Row, LogEvent Event, string Text) JsonEvent(string json, string message)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var ev = new LogEvent { Message = message, Json = document.RootElement.Clone() };
        return (Row(), ev, json);
    }

    private static bool Matches(IRowFilter filter, (EventRow Row, LogEvent Event, string Text) parsed) =>
        filter.Matches(parsed.Row, parsed.Event, parsed.Text);

    private static EventFilter Filter(FilterSpec spec) => new(spec);

    private static SearchQuery Literal(string text, bool caseSensitive = false, bool wholeWord = false) =>
        new(text, caseSensitive, wholeWord, Regex: false);

    private static SearchQuery Regex(string text, bool caseSensitive = false, bool wholeWord = false) =>
        new(text, caseSensitive, wholeWord, Regex: true);

    [Fact]
    public void An_empty_spec_is_empty_describes_nothing_and_matches_every_event()
    {
        var filter = Filter(new FilterSpec());

        Assert.True(filter.IsEmpty);
        Assert.Equal(string.Empty, filter.Describe());
        Assert.True(filter.Matches(Row(), Plain(), string.Empty));
        Assert.Equal((null, null), filter.TimeWindow);
    }

    [Fact]
    public void Empty_strings_and_an_empty_set_are_not_predicates()
    {
        var filter = Filter(new FilterSpec(FileContains: string.Empty, Levels: new HashSet<LogLevel>(), Text: Literal(string.Empty)));

        Assert.True(filter.IsEmpty);
        Assert.Null(filter.Text);
        Assert.Null(filter.Levels);
    }

    [Fact]
    public void Min_level_keeps_the_level_and_above_and_excludes_unknown()
    {
        var filter = Filter(new FilterSpec(MinLevel: LogLevel.Warn));

        Assert.False(filter.Matches(Row(level: LogLevel.Unknown), Plain(), string.Empty));
        Assert.False(filter.Matches(Row(level: LogLevel.Info), Plain(), string.Empty));
        Assert.True(filter.Matches(Row(level: LogLevel.Warn), Plain(), string.Empty));
        Assert.True(filter.Matches(Row(level: LogLevel.Fatal), Plain(), string.Empty));
    }

    [Fact]
    public void Unknown_level_is_matched_by_a_level_set_that_names_it()
    {
        var filter = Filter(new FilterSpec(Levels: new HashSet<LogLevel> { LogLevel.Unknown }));

        Assert.True(filter.Matches(Row(level: LogLevel.Unknown), Plain(), string.Empty));
        Assert.False(filter.Matches(Row(level: LogLevel.Error), Plain(), string.Empty));
    }

    [Fact]
    public void Level_set_and_minimum_both_apply()
    {
        var filter = Filter(new FilterSpec(MinLevel: LogLevel.Warn, Levels: new HashSet<LogLevel> { LogLevel.Info, LogLevel.Error }));

        Assert.True(filter.Matches(Row(level: LogLevel.Error), Plain(), string.Empty));
        Assert.False(filter.Matches(Row(level: LogLevel.Info), Plain(), string.Empty), "Info is in the set but below the minimum.");
        Assert.False(filter.Matches(Row(level: LogLevel.Warn), Plain(), string.Empty), "Warn is above the minimum but not in the set.");
    }

    [Fact]
    public void Logger_contains_is_case_insensitive_and_excludes_an_event_without_a_logger_field()
    {
        var filter = Filter(new FilterSpec(Fields: [new FieldPredicate("logger", FieldOperator.Contains, "orders")]));

        Assert.True(Matches(filter, Parse(BuiltInSchemes.Log4NetCore, Log4NetLine)), "The logger is Orders.Engine.");
        Assert.False(Matches(filter, Parse(BuiltInSchemes.Log4NetCore, Log4NetLine.Replace("Orders.Engine", "Payments.Engine", StringComparison.Ordinal))));
        Assert.False(filter.Matches(Row(), Plain(), string.Empty), "A row alone has no logger field to read.");
    }

    [Fact]
    public void File_contains_matches_the_member_name_of_the_bound_snapshot_ignoring_case()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft1.log", StoreFixtures.Events(T0, "one", 2));
        dir.Write("SheetKraft.log", StoreFixtures.Events(T0.AddHours(1), "two", 2));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Catalog catalog = StoreFixtures.CatalogOf(dir, config, "SheetKraft1.log", "SheetKraft.log");
        Member rotated = catalog.Ordered[0];
        Member active = catalog.Ordered[1];
        CatalogSnapshot snapshot = catalog.Snapshot();

        IRowFilter filter = Filter(new FilterSpec(FileContains: "KRAFT1.LOG")).Bind(snapshot);

        Assert.True(filter.Matches(Row(generation: rotated.Generation), Plain(), string.Empty));
        Assert.False(filter.Matches(Row(generation: active.Generation), Plain(), string.Empty));
        Assert.False(filter.Matches(Row(generation: 999), Plain(), string.Empty), "A generation outside the snapshot has no file name.");
    }

    [Fact]
    public void A_file_predicate_on_an_unbound_filter_throws_instead_of_guessing()
    {
        var filter = Filter(new FilterSpec(FileContains: "SheetKraft"));

        Assert.False(filter.IsBound);
        Assert.Throws<InvalidOperationException>(() => filter.Matches(Row(), Plain(), string.Empty));
    }

    [Fact]
    public void Time_bounds_are_inclusive_and_exclude_rows_without_a_time()
    {
        DateTimeOffset from = T0;
        DateTimeOffset to = T0.AddMinutes(10);
        var filter = Filter(new FilterSpec(From: from, To: to));

        Assert.True(filter.Matches(Row(time: from), Plain(), string.Empty), "A time equal to From matches.");
        Assert.True(filter.Matches(Row(time: to), Plain(), string.Empty), "A time equal to To matches.");
        Assert.False(filter.Matches(Row(time: from.AddTicks(-1)), Plain(), string.Empty));
        Assert.False(filter.Matches(Row(time: to.AddTicks(1)), Plain(), string.Empty));
        Assert.False(filter.Matches(Row(time: null), Plain(), string.Empty), "A row without a time never matches a bound.");
        Assert.Equal((from, to), filter.TimeWindow);
    }

    [Fact]
    public void A_row_without_a_time_matches_when_no_time_bound_is_set()
    {
        Assert.True(Filter(new FilterSpec(MinLevel: LogLevel.Info)).Matches(Row(time: null, level: LogLevel.Error), Plain(), string.Empty));
        Assert.False(Filter(new FilterSpec(From: T0)).Matches(Row(time: null), Plain(), string.Empty));
        Assert.False(Filter(new FilterSpec(To: T0)).Matches(Row(time: null), Plain(), string.Empty));
    }

    [Fact]
    public void A_bound_in_another_time_zone_compares_instants()
    {
        var filter = Filter(new FilterSpec(To: new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(1))));

        Assert.True(filter.Matches(Row(time: new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero)), Plain(), string.Empty));
        Assert.False(filter.Matches(Row(time: new DateTimeOffset(2026, 11, 18, 9, 0, 1, TimeSpan.Zero)), Plain(), string.Empty));
    }

    [Fact]
    public void From_after_To_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new EventFilter(new FilterSpec(From: T0.AddMinutes(1), To: T0)));
    }

    [Fact]
    public void Literal_text_is_case_insensitive_and_its_characters_are_not_special()
    {
        var filter = Filter(new FilterSpec(Text: Literal("a.b")));

        Assert.True(filter.Matches(Row(), Plain("TIMEOUT a.b"), "TIMEOUT a.b"));
        Assert.False(filter.Matches(Row(), Plain("axb"), "axb"));
    }

    [Fact]
    public void Regex_text_is_matched_as_a_regex()
    {
        var filter = Filter(new FilterSpec(Text: Regex("time.*gateway")));

        Assert.True(filter.Matches(Row(), Plain(), Log4NetLine));
        Assert.False(filter.Matches(Row(), Plain(), "gateway timeout"));
    }

    [Fact]
    public void Case_sensitive_text_does_not_match_another_case()
    {
        var filter = Filter(new FilterSpec(Text: Literal("Timeout", caseSensitive: true)));

        Assert.True(filter.Matches(Row(), Plain(), "a Timeout here"));
        Assert.False(filter.Matches(Row(), Plain(), "a timeout here"));
    }

    [Fact]
    public void Whole_word_text_does_not_match_inside_a_longer_word()
    {
        var filter = Filter(new FilterSpec(Text: Literal("time", wholeWord: true)));

        Assert.False(filter.Matches(Row(), Plain(), "timeout talking"));
        Assert.True(filter.Matches(Row(), Plain(), "a time out"));
    }

    [Fact]
    public void Text_matches_the_full_text_of_a_text_event_including_continuation_lines()
    {
        string text = "ERROR 2026-11-18 09:00:00,000 [1] Orders.Engine - failed\n   at Orders.Run() in Run.cs:line 9\n";
        var filter = Filter(new FilterSpec(Text: Literal("run.cs")));

        Assert.True(Matches(filter, Parse(BuiltInSchemes.Log4NetCore, text)));
    }

    [Fact]
    public void Text_matches_the_rendered_message_of_a_serilog_event_and_not_only_its_template()
    {
        var parsed = Parse(BuiltInSchemes.SerilogJson, SerilogLine);
        Assert.Contains("Failed to process 42", parsed.Event.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed to process 42", parsed.Text, StringComparison.Ordinal);

        Assert.True(Matches(Filter(new FilterSpec(Text: Literal("process 42"))), parsed));
        Assert.True(Matches(Filter(new FilterSpec(Text: Literal("{OrderId}"))), parsed), "The raw text is matched too.");
    }

    [Fact]
    public void Text_predicate_uses_the_text_of_the_event_and_not_only_its_row_preview()
    {
        var filter = Filter(new FilterSpec(Text: Literal("gateway")));
        (EventRow row, LogEvent ev, string text) = Parse(BuiltInSchemes.Log4NetCore, Log4NetLine);

        Assert.True(filter.Matches(row, ev, text));
        Assert.False(filter.Matches(row, ev, "nothing relevant"));
    }

    [Fact]
    public void Text_field_predicate_matches_the_named_field_of_a_text_event()
    {
        var parsed = Parse(BuiltInSchemes.Log4NetCore, Log4NetLine);
        Assert.Contains(parsed.Event.Fields, field => field.Name == "thread" && field.Value == "Worker 3");

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("thread", FieldOperator.Equals, "Worker 3")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("thread", FieldOperator.Equals, "Worker")])), parsed), "The value must be exact.");
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Thread", FieldOperator.Equals, "Worker 3")])), parsed), "Names are ordinal.");
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("missing", FieldOperator.Equals, "Worker 3")])), parsed));
    }

    [Fact]
    public void Json_field_predicate_matches_a_top_level_and_a_nested_value()
    {
        var parsed = Parse(BuiltInSchemes.SerilogJson, SerilogLine);

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.OrderId", FieldOperator.Equals, "42")])), parsed));
        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.Customer.Name", FieldOperator.Equals, "Asha")])), parsed), "A string is compared unquoted.");
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.OrderId", FieldOperator.Equals, "43")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.OrderId", FieldOperator.Equals, "\"42\"")])), parsed), "A number is not a string.");
    }

    [Fact]
    public void Json_field_predicate_matches_a_nested_string_under_an_object()
    {
        var parsed = JsonEvent("""{"req":{"headers":{"accept":"application/json"}}}""");

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("req.headers.accept", FieldOperator.Equals, "application/json")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("req.headers.accept", FieldOperator.Equals, "text/html")])), parsed));
    }

    [Fact]
    public void Json_field_predicate_matches_an_array_element()
    {
        var parsed = JsonEvent("""{"err":{"stack":["main.handler (/src/app/main.go:42)","main.main (/src/app/main.go:10)"]}}""");

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("err.stack[0]", FieldOperator.Equals, "main.handler (/src/app/main.go:42)")])), parsed));
        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("err.stack[1]", FieldOperator.Equals, "main.main (/src/app/main.go:10)")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("err.stack[0]", FieldOperator.Equals, "main.main (/src/app/main.go:10)")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("err.stack[2]", FieldOperator.Equals, "anything")])), parsed), "An index past the end matches nothing.");
    }

    [Fact]
    public void Json_field_predicate_reads_an_element_of_an_array_of_arrays()
    {
        var parsed = JsonEvent("""{"grid":[[1,2],[3,4]]}""");

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("grid[1][0]", FieldOperator.Equals, "3")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("grid[1][2]", FieldOperator.Equals, "3")])), parsed));
    }

    [Fact]
    public void Json_field_predicate_on_a_missing_path_matches_nothing()
    {
        var parsed = Parse(BuiltInSchemes.SerilogJson, SerilogLine);

        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.Nope", FieldOperator.Equals, "x")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.Scope[1]", FieldOperator.Equals, "Request 7")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.OrderId.Deeper", FieldOperator.Equals, "42")])), parsed), "A scalar has no members.");
    }

    [Theory]
    [InlineData("a..b")]
    [InlineData("a.")]
    [InlineData("stack[x]")]
    [InlineData("stack[-1]")]
    [InlineData("stack[0")]
    [InlineData("stack[0]x")]
    [InlineData("stack[+0]")]
    [InlineData("[0]")]
    public void Malformed_json_paths_match_nothing(string path)
    {
        var parsed = JsonEvent("""{"stack":["a"],"a":{"b":"c"}}""");

        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate(path, FieldOperator.Equals, "a")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate(path, FieldOperator.Equals, "c")])), parsed));
    }

    [Fact]
    public void Json_field_predicate_compares_an_object_by_its_raw_json_text()
    {
        var parsed = Parse(BuiltInSchemes.SerilogJson, SerilogLine);

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.Customer", FieldOperator.Equals, """{"Name":"Asha"}""")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.Customer", FieldOperator.Equals, "Asha")])), parsed));
    }

    [Fact]
    public void Json_field_predicate_compares_a_null_by_its_raw_text()
    {
        var parsed = JsonEvent("""{"opt":null,"flag":true}""");

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("opt", FieldOperator.Equals, "null")])), parsed));
        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("flag", FieldOperator.Equals, "true")])), parsed));
    }

    [Fact]
    public void Message_contains_matches_the_whole_message_of_a_text_event_including_its_continuation_lines()
    {
        string text = "ERROR 2026-11-18 09:00:00,000 [1] Orders.Engine - failed\n   at Orders.Run() in Run.cs:line 9\n";
        var parsed = Parse(BuiltInSchemes.Log4NetCore, text);

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("message", FieldOperator.Contains, "run.cs")])), parsed));
        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("message", FieldOperator.Prefix, "FAILED")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("message", FieldOperator.Equals, "failed")])), parsed), "The message has its continuation.");
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("message", FieldOperator.Contains, "gateway")])), parsed));
    }

    [Fact]
    public void Message_contains_matches_the_message_of_a_JSON_event_when_the_object_has_no_message_member()
    {
        var slog = JsonEvent("""{"msg":"request failed","err":{"message":"db down"}}""", message: "request failed");
        var serilog = Parse(BuiltInSchemes.SerilogJson, SerilogLine);

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("message", FieldOperator.Suffix, "failed")])), slog));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("message", FieldOperator.Contains, "db down")])), slog)," The nested err.message is not the message.");
        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("message", FieldOperator.Contains, "process 42")])), serilog));
    }

    [Fact]
    public void Message_is_the_JSON_member_named_message_when_the_object_has_one()
    {
        var parsed = JsonEvent("""{"msg":"the msg","message":"own"}""", message: "the msg");

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("message", FieldOperator.Equals, "own")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("message", FieldOperator.Equals, "the msg")])), parsed));
    }

    [Fact]
    public void Each_operator_compares_the_value_of_the_event_ignoring_case()
    {
        var parsed = Parse(BuiltInSchemes.SerilogJson, SerilogLine);

        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.OrderId", FieldOperator.Prefix, "4")])), parsed));
        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.Customer.Name", FieldOperator.Suffix, "HA")])), parsed));
        Assert.True(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.Customer.Name", FieldOperator.Contains, "sha")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.OrderId", FieldOperator.Contains, "43")])), parsed));
        Assert.False(Matches(Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.OrderId", FieldOperator.Prefix, "2")])), parsed));
    }

    [Fact]
    public void Field_predicates_are_anded_with_each_other()
    {
        var parsed = Parse(BuiltInSchemes.SerilogJson, SerilogLine);
        var both = Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.OrderId", FieldOperator.Equals, "42"), new FieldPredicate("Properties.Customer.Name", FieldOperator.Equals, "Asha")]));
        var oneWrong = Filter(new FilterSpec(Fields: [new FieldPredicate("Properties.OrderId", FieldOperator.Equals, "42"), new FieldPredicate("Properties.Customer.Name", FieldOperator.Equals, "Bo")]));

        Assert.True(Matches(both, parsed));
        Assert.False(Matches(oneWrong, parsed));
    }

    [Fact]
    public void All_predicates_must_hold()
    {
        var parsed = Parse(BuiltInSchemes.Log4NetCore, Log4NetLine);
        (EventRow row, LogEvent ev, string text) = parsed;
        var thread = new FieldPredicate("thread", FieldOperator.Equals, "Worker 3");
        var logger = new FieldPredicate("logger", FieldOperator.Contains, "orders");
        var spec = new FilterSpec(
            MinLevel: LogLevel.Warn,
            From: T0.AddMinutes(-1),
            To: T0.AddMinutes(1),
            Text: Literal("timeout"),
            Fields: [thread, logger]);

        Assert.True(Filter(spec).Matches(row, ev, text));
        Assert.False(Filter(spec with { MinLevel = LogLevel.Fatal }).Matches(row, ev, text));
        Assert.False(Filter(spec with { Fields = [thread, new FieldPredicate("logger", FieldOperator.Contains, "payments")] }).Matches(row, ev, text));
        Assert.False(Filter(spec with { To = T0.AddMinutes(-1) }).Matches(row, ev, text));
        Assert.False(Filter(spec with { Text = Literal("gateway timeout") }).Matches(row, ev, text));
        Assert.False(Filter(spec with { Fields = [new FieldPredicate("thread", FieldOperator.Equals, "Worker 4")] }).Matches(row, ev, text));
    }

    [Fact]
    public void A_bound_filter_matches_the_same_events_as_the_unbound_one()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", StoreFixtures.Events(T0, "one", 2));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        Member member = StoreFixtures.CatalogOf(dir, config, "SheetKraft.log").Ordered.Single();
        var filter = Filter(new FilterSpec(MinLevel: LogLevel.Warn, FileContains: "kraft"));
        IRowFilter bound = filter.Bind(StoreFixtures.CatalogOf(dir, config, "SheetKraft.log").Snapshot());

        Assert.True(bound.Matches(Row(generation: member.Generation, level: LogLevel.Error), Plain(), string.Empty));
        Assert.False(bound.Matches(Row(generation: member.Generation, level: LogLevel.Info), Plain(), string.Empty));
        Assert.True(((EventFilter)bound).IsBound);
        Assert.False(filter.IsBound);
    }

    [Fact]
    public void Describe_lists_the_set_predicates_in_editor_order()
    {
        var filter = Filter(new FilterSpec(
            MinLevel: LogLevel.Warn,
            Text: Literal("timeout"),
            Fields: [new FieldPredicate("logger", FieldOperator.Contains, "Orders"), new FieldPredicate("Properties.OrderId", FieldOperator.Equals, "42")]));

        Assert.Equal("level ≥ WARN · text ~ timeout · logger contains Orders · Properties.OrderId equals 42", filter.Describe());
    }

    [Fact]
    public void Describe_renders_sets_files_times_and_text_options()
    {
        DateTimeOffset from = new(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(5));
        var filter = Filter(new FilterSpec(
            Levels: new HashSet<LogLevel> { LogLevel.Error, LogLevel.Info },
            FileContains: "SheetKraft1",
            From: from,
            To: from.AddHours(2),
            Text: Regex("time.*out", caseSensitive: true, wholeWord: true)));

        Assert.Equal(
            "level ∈ {INFO, ERROR} · file ~ SheetKraft1 · time ≥ 2026-11-18 09:00:00 · time ≤ 2026-11-18 11:00:00 · text ~ /time.*out/ (case-sensitive) (whole word)",
            filter.Describe());
    }

    [Fact]
    public void Describe_of_an_unbound_filter_does_not_depend_on_binding()
    {
        var filter = Filter(new FilterSpec(MinLevel: LogLevel.Error));

        Assert.Equal("level ≥ ERROR", filter.Describe());
        Assert.Equal(filter.Describe(), ((EventFilter)filter.Bind(new CatalogSnapshot(0, []))).Describe());
    }

    [Fact]
    public void An_invalid_regex_is_rejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new EventFilter(new FilterSpec(Text: Regex("a)(b"))));

        Assert.Contains("not a valid pattern", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_predicate_with_an_empty_path_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new EventFilter(new FilterSpec(Fields: [new FieldPredicate(string.Empty, FieldOperator.Equals, "x")])));
    }

    [Fact]
    public void A_timed_out_text_pattern_makes_the_filter_match_nothing_more()
    {
        // The backreference forces the backtracking engine, where the nested quantifier takes exponential time.
        var filter = Filter(new FilterSpec(Text: Regex("(a+)+\\1b")));
        Assert.False(filter.TimedOut);

        Assert.False(filter.Matches(Row(), Plain(new string('a', 30)), new string('a', 30)));
        Assert.True(filter.TimedOut);
        Assert.False(filter.Matches(Row(), Plain("ab ab"), "ab ab"), "A timed-out pattern matches nothing, even a text it would match.");
        Assert.False(filter.IsEmpty);
    }

    [Fact]
    public void A_timeout_is_shared_with_the_bound_copy_of_the_filter()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", StoreFixtures.Events(T0, "one", 2));
        LogvuConfig config = StoreTestConfig.Create(dir, activeFile: "SheetKraft.log");
        CatalogSnapshot snapshot = StoreFixtures.CatalogOf(dir, config, "SheetKraft.log").Snapshot();
        var filter = Filter(new FilterSpec(Text: Regex("(a+)+\\1b"), FileContains: "kraft"));
        var bound = (EventFilter)filter.Bind(snapshot);

        bound.Matches(Row(generation: snapshot.Ordered[0].Generation), Plain(new string('a', 30)), new string('a', 30));

        Assert.True(bound.TimedOut);
        Assert.True(filter.TimedOut);
    }

    [Fact]
    public void Describe_writes_each_field_predicate_with_its_keyword_in_order()
    {
        var filter = Filter(new FilterSpec(Fields:
        [
            new FieldPredicate("user", FieldOperator.Equals, "alice"),
            new FieldPredicate("logger", FieldOperator.Prefix, "Orders"),
            new FieldPredicate("code", FieldOperator.Suffix, "9"),
            new FieldPredicate("message", FieldOperator.Contains, "timeout"),
        ]));

        Assert.Equal("user equals alice · logger prefix Orders · code suffix 9 · message contains timeout", filter.Describe());
    }

    [Fact]
    public void Time_window_is_the_time_bounds_and_is_null_without_them()
    {
        Assert.Equal((T0, null), Filter(new FilterSpec(From: T0)).TimeWindow);
        Assert.Equal((null, T0), Filter(new FilterSpec(To: T0)).TimeWindow);
        Assert.Equal((null, null), Filter(new FilterSpec(MinLevel: LogLevel.Warn)).TimeWindow);
    }
}
