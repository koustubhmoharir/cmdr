using System.Text.Json;
using Logvu.Core.Parsing;
using static Logvu.Core.Tests.Parsing.ParsingFixtures;

namespace Logvu.Core.Tests.Parsing;

public sealed class SlogSchemeTests
{
    private static readonly SlogJsonScheme Scheme = BuiltInSchemes.SlogJson;

    private const string ErrorLine =
        """{"time":"2026-11-18T10:20:31.007000000+05:30","level":"ERROR","msg":"request failed","logger":"api","err":{"message":"db down","attrs":{"table":"orders"},"cause":["dial tcp 10.0.0.9:1433: connect: connection refused"],"stack":["main.handler (/src/app/main.go:42)","net/http.HandlerFunc.ServeHTTP (net/http/server.go:2220)"]}}""";

    private const string WarnLine =
        """{"time":"2026-11-18T10:20:31.5+05:30","level":"WARN+2","msg":"retrying","cause":[{"message":"inner","attrs":{"attempt":2}},"io: EOF"],"stack":["pkg.F (pkg/f.go:10)"]}""";

    [Fact]
    public void Error_example_parses_time_level_message_and_fields()
    {
        LogEvent e = Scheme.Parse(ErrorLine, Utc);

        Assert.False(e.IsRaw);
        AssertTime(e.Time, new DateTime(2026, 11, 18, 10, 20, 31, 7), TimeSpan.FromHours(5.5));
        Assert.Equal("2026-11-18T10:20:31.007000000+05:30", e.TimeText);
        Assert.Equal(LogLevel.Error, e.Level);
        Assert.Equal("ERROR", e.LevelText);
        Assert.Equal("request failed: db down: dial tcp 10.0.0.9:1433: connect: connection refused", e.Message);
        AssertFields(e,
            ("time", "2026-11-18T10:20:31.007000000+05:30"),
            ("level", "ERROR"),
            ("msg", "request failed"),
            ("logger", "api"),
            ("err", """{"message":"db down","attrs":{"table":"orders"},"cause":["dial tcp 10.0.0.9:1433: connect: connection refused"],"stack":["main.handler (/src/app/main.go:42)","net/http.HandlerFunc.ServeHTTP (net/http/server.go:2220)"]}"""));
    }

    [Fact]
    public void Message_joins_msg_the_error_message_and_its_causes()
    {
        const string line =
            """{"time":"2026-11-18T10:20:31.007000000+05:30","level":"WARN","msg":"failed to register with CodeKraft","error":{"message":"request cannot be made because auth token could not be obtained","attrs":{"method":"POST"},"cause":[{"attrs":{"url":"u"},"message":"request failed"},"Post \"http://localhost:57664/api/extapp/token\": dial tcp 127.0.0.1:57664: connect: connection refused"],"stack":["a.F (x.go:1)"]}}""";

        LogEvent e = Scheme.Parse(line, Utc);

        Assert.False(e.IsRaw);
        Assert.False(e.MessageRendered);
        Assert.Equal(
            "failed to register with CodeKraft: request cannot be made because auth token could not be obtained: request failed: Post \"http://localhost:57664/api/extapp/token\": dial tcp 127.0.0.1:57664: connect: connection refused",
            e.Message);
    }

    [Fact]
    public void Msg_alone_is_the_message_when_there_is_no_error_or_cause()
    {
        LogEvent e = Scheme.Parse("""{"time":"2026-11-18T10:20:34Z","level":"INFO","msg":"plain","attrs":{"message":"not it"}}""", Utc);

        Assert.Equal("plain", e.Message);
    }

    [Fact]
    public void Err_key_gives_the_error_message()
    {
        LogEvent e = Scheme.Parse("""{"msg":"m","err":{"message":"e1"}}""", Utc);

        Assert.Equal("m: e1", e.Message);
    }

    [Fact]
    public void Error_key_is_matched_ignoring_case()
    {
        LogEvent e = Scheme.Parse("""{"msg":"m","ERROR":{"message":"e1"}}""", Utc);

        Assert.Equal("m: e1", e.Message);
    }

    [Fact]
    public void Top_level_cause_array_without_an_error_object_gives_the_causes()
    {
        LogEvent e = Scheme.Parse("""{"msg":"m","cause":["c1",{"message":"c2"}]}""", Utc);

        Assert.Equal("m: c1: c2", e.Message);
    }

    [Fact]
    public void Nested_causes_are_collected_depth_first()
    {
        LogEvent e = Scheme.Parse(
            """{"msg":"top","cause":[{"message":"a","cause":[{"message":"b","cause":["c"]},"d"]},"e"]}""", Utc);

        Assert.Equal("top: a: b: c: d: e", e.Message);
    }

    [Fact]
    public void Error_object_without_message_still_gives_its_causes()
    {
        LogEvent e = Scheme.Parse("""{"msg":"m","err":{"attrs":{"x":1},"cause":["x1"]}}""", Utc);

        Assert.Equal("m: x1", e.Message);
    }

    [Fact]
    public void Empty_and_missing_parts_are_skipped()
    {
        LogEvent e = Scheme.Parse(
            """{"msg":"","err":{"message":"","cause":["","y",{"cause":["z"]},{"message":null},{"message":""}]}}""", Utc);

        Assert.Equal("y: z", e.Message);
    }

    [Fact]
    public void Error_member_that_is_a_string_is_ignored_for_the_message()
    {
        Assert.Equal("m", Scheme.Parse("""{"msg":"m","err":"boom"}""", Utc).Message);
        Assert.Equal("m: c", Scheme.Parse("""{"msg":"m","error":"boom","cause":["c"]}""", Utc).Message);
    }

    [Fact]
    public void A_string_error_does_not_hide_a_later_error_object()
    {
        LogEvent e = Scheme.Parse("""{"msg":"m","error":"boom","err":{"message":"e1"}}""", Utc);

        Assert.Equal("m: e1", e.Message);
    }

    [Fact]
    public void The_error_objects_cause_array_takes_precedence_over_the_top_level_one()
    {
        Assert.Equal("m: inner", Scheme.Parse("""{"msg":"m","err":{"cause":["inner"]},"cause":["outer"]}""", Utc).Message);
    }

    [Fact]
    public void The_top_level_cause_is_used_when_the_error_objects_cause_is_not_an_array()
    {
        Assert.Equal("m: outer", Scheme.Parse("""{"msg":"m","err":{"cause":"text"},"cause":["outer"]}""", Utc).Message);
    }

    [Fact]
    public void Deep_cause_chains_are_collected_in_full_within_the_parser_depth()
    {
        // JsonDocument allows 64 levels of nesting and each cause level costs two of them, so 31 levels parse. The depth guard in
        // AddCauses (32 cause arrays) is therefore not reached through Parse.
        const int levels = 31;
        string inner = "\"end\"";
        for (int level = levels; level >= 1; level--)
        {
            inner = $"{{\"message\":\"l{level}\",\"cause\":[{inner}]}}";
        }

        LogEvent e = Scheme.Parse($$"""{"msg":"m","cause":[{{inner}}]}""", Utc);

        string expected = "m: " + string.Join(": ", Enumerable.Range(1, levels).Select(level => $"l{level}")) + ": end";
        Assert.False(e.IsRaw);
        Assert.Equal(expected, e.Message);
    }

    [Fact]
    public void Nine_digit_fraction_is_truncated_to_seven_digits()
    {
        LogEvent e = Scheme.Parse(
            """{"time":"2026-11-18T10:20:30.123456789+05:30","level":"INFO","msg":"x"}""", Utc);

        Assert.NotNull(e.Time);
        Assert.Equal(1234567L, e.Time.Value.Ticks % TimeSpan.TicksPerSecond);
        Assert.Equal(TimeSpan.FromHours(5.5), e.Time.Value.Offset);
        Assert.Equal("2026-11-18T10:20:30.123456789+05:30", e.TimeText);
    }

    [Fact]
    public void Warn_plus_two_is_warn_with_its_text_kept()
    {
        LogEvent e = Scheme.Parse(WarnLine, Utc);

        Assert.False(e.IsRaw);
        Assert.Equal(LogLevel.Warn, e.Level);
        Assert.Equal("WARN+2", e.LevelText);
        Assert.Equal("retrying: inner: io: EOF", e.Message);
        AssertFields(e,
            ("time", "2026-11-18T10:20:31.5+05:30"),
            ("level", "WARN+2"),
            ("msg", "retrying"),
            ("cause", """[{"message":"inner","attrs":{"attempt":2}},"io: EOF"]"""),
            ("stack", """["pkg.F (pkg/f.go:10)"]"""));
    }

    [Fact]
    public void Json_stays_usable_after_parse_returns()
    {
        LogEvent e = Scheme.Parse(ErrorLine, Utc);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.NotNull(e.Json);
        Assert.Equal("db down", e.Json.Value.GetProperty("err").GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("DEBUG-4", LogLevel.Debug)]
    [InlineData("INFO", LogLevel.Info)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("CUSTOM", LogLevel.Unknown)]
    public void Levels_normalize(string level, LogLevel expected)
    {
        LogEvent e = Scheme.Parse($$"""{"time":"2026-11-18T10:20:30Z","level":"{{level}}","msg":"x"}""", Utc);

        Assert.Equal(expected, e.Level);
        Assert.Equal(level, e.LevelText);
    }

    [Fact]
    public void Offset_free_time_is_read_in_the_context_zone()
    {
        const string line = """{"time":"2026-11-18T10:20:30.5","level":"INFO","msg":"x"}""";

        AssertTime(Scheme.Parse(line, Custom).Time, new DateTime(2026, 11, 18, 10, 20, 30, 500), TimeSpan.FromHours(5.5));
        AssertTime(Scheme.Parse(line, Utc).Time, new DateTime(2026, 11, 18, 10, 20, 30, 500), TimeSpan.Zero);
    }

    [Fact]
    public void Zulu_time_is_utc()
    {
        LogEvent e = Scheme.Parse("""{"time":"2026-11-18T10:20:34Z","level":"INFO","msg":"x"}""", Custom);

        AssertTime(e.Time, new DateTime(2026, 11, 18, 10, 20, 34), TimeSpan.Zero);
    }

    [Fact]
    public void Missing_msg_is_an_empty_message_and_missing_level_is_unknown()
    {
        LogEvent e = Scheme.Parse("""{"time":"2026-11-18T10:20:34Z"}""", Utc);

        Assert.False(e.IsRaw);
        Assert.Equal(string.Empty, e.Message);
        Assert.Equal(LogLevel.Unknown, e.Level);
        Assert.Null(e.LevelText);
    }

    [Fact]
    public void Non_string_logger_is_an_ordinary_member()
    {
        LogEvent e = Scheme.Parse("""{"time":"2026-11-18T10:20:34Z","level":"INFO","msg":"x","logger":5}""", Utc);

        Assert.Contains(e.Fields, field => field.Name == "logger" && field.Value == "5");
    }

    [Fact]
    public void Malformed_line_is_raw_with_the_parse_error()
    {
        LogEvent e = Scheme.Parse("""{"time":"2026-11-18T10:20:33.000000000+05:30","level":"INFO","msg":"truncated""", Utc);

        Assert.True(e.IsRaw);
        Assert.NotNull(e.ParseError);
        Assert.Null(e.Json);
        Assert.Equal(LogLevel.Unknown, e.Level);
    }

    [Fact]
    public void Json_array_is_raw_with_a_parse_error()
    {
        LogEvent e = Scheme.Parse("""["2026-11-18T10:20:34Z","INFO","array"]""", Utc);

        Assert.True(e.IsRaw);
        Assert.Equal("line is not a JSON object", e.ParseError);
    }

    [Fact]
    public void Fixture_has_two_raw_lines_and_six_events()
    {
        List<LogEvent> events = JsonEvents("slog.json").Select(line => Scheme.Parse(line, Utc)).ToList();

        Assert.Equal(8, events.Count);
        Assert.Equal(2, events.Count(e => e.IsRaw));
        Assert.Equal(6, events.Count(e => !e.IsRaw));
        Assert.All(events.Where(e => !e.IsRaw), e => Assert.NotNull(e.Time));
    }

    [Fact]
    public void Json_element_of_fixture_event_is_usable_after_the_document_is_gone()
    {
        LogEvent e = Scheme.Parse(JsonEvents("slog.json")[6], Utc);

        Assert.Equal(JsonValueKind.Number, e.Json!.Value.GetProperty("duration_ms").ValueKind);
        Assert.Equal("api", e.Json.Value.GetProperty("logger").GetString());
    }
}
