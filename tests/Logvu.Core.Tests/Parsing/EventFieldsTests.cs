using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Parsing;

/// <summary>
/// The value of a path in an event: a JSON path, the message of a JSON event when the object has no such member, a text event's first field of
/// that name, and nothing for a raw event.
/// </summary>
public sealed class EventFieldsTests
{
    private static readonly ParseContext Context = new(TimeZoneInfo.Utc);

    private static LogEvent Parse(ILogScheme scheme, string text) => scheme.Parse(text, Context);

    [Fact]
    public void A_JSON_path_gives_the_value_at_the_path_as_text()
    {
        LogEvent e = Parse(BuiltInSchemes.SlogJson, """{"msg":"request failed","err":{"message":"db down"},"id":42}""");

        Assert.True(EventFields.TryGetValue(e, "err.message", out string error));
        Assert.Equal("db down", error);
        Assert.True(EventFields.TryGetValue(e, "id", out string id));
        Assert.Equal("42", id);
    }

    [Fact]
    public void A_missing_JSON_path_gives_no_value_and_is_not_the_message()
    {
        LogEvent e = Parse(BuiltInSchemes.SlogJson, """{"msg":"request failed"}""");

        Assert.False(EventFields.TryGetValue(e, "err.message", out _));
        Assert.False(EventFields.TryGetValue(e, "Message", out _), "The fallback is for the exact path message.");
    }

    [Fact]
    public void A_slog_event_gives_its_msg_for_the_message_path()
    {
        LogEvent e = Parse(BuiltInSchemes.SlogJson, """{"time":"2026-11-18T10:20:31.007+05:30","level":"ERROR","msg":"request failed"}""");

        Assert.True(EventFields.TryGetValue(e, "message", out string value));
        Assert.Equal("request failed", value);
    }

    [Fact]
    public void A_serilog_event_gives_its_rendered_message_for_the_message_path()
    {
        LogEvent e = Parse(
            BuiltInSchemes.SerilogJson,
            """{"Timestamp":"2026-11-18T10:20:30.1234567+05:30","Level":"Error","MessageTemplate":"Failed to process {OrderId}","Properties":{"OrderId":42}}""");

        Assert.True(e.MessageRendered);
        Assert.True(EventFields.TryGetValue(e, "message", out string value));
        Assert.Equal("Failed to process 42", value);
    }

    [Fact]
    public void A_serilog_event_with_a_rendered_message_gives_that_text()
    {
        LogEvent e = Parse(
            BuiltInSchemes.SerilogJson,
            """{"Timestamp":"2026-11-18T10:20:30.1234567+05:30","Level":"Error","MessageTemplate":"Failed {OrderId}","RenderedMessage":"Failed 42","Properties":{"OrderId":42}}""");

        Assert.True(EventFields.TryGetValue(e, "message", out string value));
        Assert.Equal("Failed 42", value);
    }

    [Fact]
    public void A_JSON_member_named_message_wins_over_the_event_message()
    {
        LogEvent slog = Parse(BuiltInSchemes.SlogJson, """{"msg":"the msg","message":"own text"}""");
        LogEvent serilog = Parse(
            BuiltInSchemes.SerilogJson,
            """{"Timestamp":"2026-11-18T10:20:30Z","Level":"Error","MessageTemplate":"Failed","message":"own text"}""");

        Assert.True(EventFields.TryGetValue(slog, "message", out string fromSlog));
        Assert.Equal("own text", fromSlog);
        Assert.True(EventFields.TryGetValue(serilog, "message", out string fromSerilog));
        Assert.Equal("own text", fromSerilog);
    }

    [Fact]
    public void A_JSON_member_named_message_that_is_not_a_string_gives_its_raw_text()
    {
        LogEvent e = Parse(BuiltInSchemes.SlogJson, """{"msg":"the msg","message":{"code":7}}""");

        Assert.True(EventFields.TryGetValue(e, "message", out string value));
        Assert.Equal("""{"code":7}""", value);
    }

    [Fact]
    public void A_text_event_gives_the_value_of_its_named_field()
    {
        LogEvent e = Parse(BuiltInSchemes.Log4NetCore, "ERROR 2026-11-18 09:00:00,000 [Worker 3] Orders.Engine - timeout talking to gateway");

        Assert.True(EventFields.TryGetValue(e, "thread", out string thread));
        Assert.Equal("Worker 3", thread);
        Assert.True(EventFields.TryGetValue(e, "logger", out string logger));
        Assert.Equal("Orders.Engine", logger);
        Assert.False(EventFields.TryGetValue(e, "Thread", out _), "Names are exact.");
        Assert.False(EventFields.TryGetValue(e, "Properties.thread", out _), "A text event has no dotted paths.");
    }

    [Fact]
    public void A_text_event_gives_its_whole_message_for_the_message_path_including_continuation_lines()
    {
        LogEvent e = Parse(BuiltInSchemes.Log4NetCore, "ERROR 2026-11-18 09:00:00,000 [1] Orders.Engine - failed\n   at Orders.Run() in Run.cs:line 9");

        Assert.True(EventFields.TryGetValue(e, "message", out string value));
        Assert.Equal(e.Message, value);
        Assert.Contains("\n   at Orders.Run()", value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_raw_event_has_no_values_even_for_the_message_path()
    {
        LogEvent raw = LogEvent.Raw("text before any header", null);

        Assert.False(EventFields.TryGetValue(raw, "message", out _));
        Assert.False(EventFields.TryGetValue(raw, "anything", out _));
    }

    [Fact]
    public void A_null_argument_is_an_error()
    {
        LogEvent e = LogEvent.Raw("x", null);

        Assert.Throws<ArgumentNullException>(() => EventFields.TryGetValue(null!, "message", out _));
        Assert.Throws<ArgumentNullException>(() => EventFields.TryGetValue(e, null!, out _));
    }
}
