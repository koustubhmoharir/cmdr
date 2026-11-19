using Logvu.Core.Detail;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Detail;

/// <summary>The spans of text-event field values and message lines, JSON string values, inline exception type names and links.</summary>
public sealed class ValueStylingTests
{
    private const string SlogEvent = "{\"time\":\"2026-11-18T10:20:31Z\",\"level\":\"INFO\",\"msg\":\"m\"";

    private static DetailDocument TextDocument(string message) =>
        DetailRig.Build(DetailRig.Loaded(TextEventDocumentTests.ErrorEvent().Replace("Run failed", message, StringComparison.Ordinal), BuiltInSchemes.Log4NetCore));

    private static DetailDocument JsonDocument(string json) => DetailRig.Build(DetailRig.Loaded(json, BuiltInSchemes.SlogJson));

    private static List<(SpanStyle Style, string Text)> Pairs(string text, IEnumerable<StyledSpan> spans) =>
        spans.Select(span => (span.Style, text.Substring(span.Start, span.Length))).ToList();

    private static List<string> LinkTexts(DetailLine line) =>
        DetailRig.SpansOf(line).Where(span => span.Style == SpanStyle.Link).Select(span => span.Text).ToList();

    [Fact]
    public void Text_field_values_are_coloured_by_what_they_look_like()
    {
        IReadOnlyList<DetailLine> lines = TextDocument("Run failed").Lines;

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.JsonKey, "time"),
            (SpanStyle.Punctuation, ": "),
            (SpanStyle.Date, "2026-11-18"),
            (SpanStyle.Muted, " "),
            (SpanStyle.Time, "10:20:31"),
            (SpanStyle.Muted, ",007"),
        ], DetailRig.SpansOf(DetailRig.Line(lines, "time: ")));
        Assert.Equal<(SpanStyle, string)>(
            [(SpanStyle.JsonKey, "level"), (SpanStyle.Punctuation, ": "), (SpanStyle.JsonString, "ERROR")],
            DetailRig.SpansOf(DetailRig.Line(lines, "level: ")));
        Assert.Equal<(SpanStyle, string)>(
            [(SpanStyle.JsonKey, "thread"), (SpanStyle.Punctuation, ": "), (SpanStyle.JsonString, "Worker 3")],
            DetailRig.SpansOf(DetailRig.Line(lines, "thread: ")));
    }

    [Theory]
    [InlineData("42", SpanStyle.JsonNumber)]
    [InlineData("-1.5e+3", SpanStyle.JsonNumber)]
    [InlineData("0.25", SpanStyle.JsonNumber)]
    [InlineData("\u0663", SpanStyle.JsonString)]
    [InlineData("1.", SpanStyle.JsonString)]
    [InlineData("2026-10-20", SpanStyle.JsonString)]
    public void Field_values_that_are_numbers_or_not_are_one_span(string value, SpanStyle style)
    {
        Assert.Equal<(SpanStyle, string)>([(style, value)], Pairs(value, ValueStyling.Field(value)));
    }

    [Fact]
    public void A_complete_timestamp_field_value_is_drawn_in_parts_without_quotes()
    {
        const string value = "2026-10-20T15:50:23.5+05:30";

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Date, "2026-10-20"),
            (SpanStyle.Muted, "T"),
            (SpanStyle.Time, "15:50:23"),
            (SpanStyle.Muted, ".5"),
            (SpanStyle.Muted, "+05:30"),
        ], Pairs(value, ValueStyling.Field(value)));
    }

    [Fact]
    public void An_empty_field_value_has_no_span()
    {
        Assert.Empty(ValueStyling.Field(string.Empty));
    }

    [Fact]
    public void A_message_line_is_a_json_string_and_its_label_is_a_json_key()
    {
        DetailLine line = DetailRig.Line(TextDocument("Run failed").Lines, "message: ");

        Assert.Equal<(SpanStyle, string)>(
            [(SpanStyle.JsonKey, "message"), (SpanStyle.Punctuation, ": "), (SpanStyle.JsonString, "Run failed")],
            DetailRig.SpansOf(line));
    }

    [Fact]
    public void A_raw_event_message_line_is_a_json_string()
    {
        DetailDocument document = DetailRig.Build(DetailRig.Loaded("not a header at all\n  see https://x.y/z", BuiltInSchemes.Log4NetCore));

        DetailLine line = DetailRig.Line(document.Lines, "Message: ");
        Assert.Equal<(SpanStyle, string)>(
            [(SpanStyle.JsonKey, "Message"), (SpanStyle.Punctuation, ": "), (SpanStyle.JsonString, "not a header at all")],
            DetailRig.SpansOf(line));
    }

    [Fact]
    public void An_inline_exception_type_name_in_a_message_line_is_red_inside_the_json_string()
    {
        DetailLine line = DetailRig.Line(TextDocument("Failed: System.IO.IOException, see log").Lines, "message: ");

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.JsonKey, "message"),
            (SpanStyle.Punctuation, ": "),
            (SpanStyle.JsonString, "Failed: "),
            (SpanStyle.ExceptionType, "System.IO.IOException"),
            (SpanStyle.JsonString, ", see log"),
        ], DetailRig.SpansOf(line));
    }

    [Theory]
    [InlineData("Failed: MyException, see log")]
    [InlineData("Failed: System.ExceptionHandler, see log")]
    [InlineData("Failed: Exception, see log")]
    public void Names_that_are_not_dotted_exception_types_are_not_red(string message)
    {
        DetailLine line = DetailRig.Line(TextDocument(message).Lines, "message: ");

        Assert.DoesNotContain(DetailRig.SpansOf(line), span => span.Style == SpanStyle.ExceptionType);
    }

    [Theory]
    [InlineData("See https://example.com/a/b for details", "https://example.com/a/b")]
    [InlineData("Docs at https://example.com/x.", "https://example.com/x")]
    [InlineData("(see https://en.wikipedia.org/wiki/Foo_(bar))", "https://en.wikipedia.org/wiki/Foo_(bar)")]
    [InlineData("Go to http://a/b now", "http://a/b")]
    [InlineData("Log at C:\\logs\\app.log written", "C:\\logs\\app.log")]
    [InlineData("Share \\\\server\\share\\dir\\file.txt ok", "\\\\server\\share\\dir\\file.txt")]
    [InlineData("Saved to /var/log/app.log.", "/var/log/app.log")]
    [InlineData("see (/var/log/app.log)", "/var/log/app.log")]
    [InlineData("a, /var/log/x, b", "/var/log/x")]
    [InlineData("Config ~/logs/app.log loaded", "~/logs/app.log")]
    [InlineData("ratio 1/2/3 done", "")]
    public void Links_in_a_message_line_cover_the_path_or_url_without_trailing_punctuation(string message, string expected)
    {
        DetailLine line = DetailRig.Line(TextDocument(message).Lines, "message: ");

        List<string> links = LinkTexts(line);
        if (expected.Length == 0)
        {
            Assert.Empty(links);
        }
        else
        {
            Assert.Equal([expected], links);
        }
    }

    [Fact]
    public void A_url_in_a_message_line_is_a_link_between_plain_text_spans()
    {
        DetailLine line = DetailRig.Line(TextDocument("See https://example.com/a/b for details").Lines, "message: ");

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.JsonKey, "message"),
            (SpanStyle.Punctuation, ": "),
            (SpanStyle.JsonString, "See "),
            (SpanStyle.Link, "https://example.com/a/b"),
            (SpanStyle.JsonString, " for details"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void A_windows_path_in_a_text_field_value_is_a_link()
    {
        string text = TextEventDocumentTests.ErrorEvent().Replace("[Worker 3]", "[C:\\logs\\app.log]", StringComparison.Ordinal);
        DetailDocument document = DetailRig.Build(DetailRig.Loaded(text, BuiltInSchemes.Log4NetCore));

        DetailLine thread = DetailRig.Line(document.Lines, "thread: ");
        Assert.Equal<(SpanStyle, string)>(
            [(SpanStyle.JsonKey, "thread"), (SpanStyle.Punctuation, ": "), (SpanStyle.Link, "C:\\logs\\app.log")],
            DetailRig.SpansOf(thread));
    }

    [Fact]
    public void A_windows_path_in_a_json_string_is_a_link_that_covers_the_path_but_not_the_quotes()
    {
        DetailDocument document = JsonDocument(SlogEvent + ",\"path\":\"C:\\\\logs\\\\app.log\"}");

        DetailLine path = DetailRig.Line(document.Lines, "  path: ");
        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.JsonKey, "path"),
            (SpanStyle.Punctuation, ": "),
            (SpanStyle.JsonString, "\""),
            (SpanStyle.Link, "C:\\\\logs\\\\app.log"),
            (SpanStyle.JsonString, "\""),
        ], DetailRig.SpansOf(path));
    }

    [Fact]
    public void A_unix_path_in_a_json_string_is_a_link()
    {
        DetailDocument document = JsonDocument(SlogEvent + ",\"file\":\"/var/log/app.log\"}");

        DetailLine file = DetailRig.Line(document.Lines, "  file: ");
        Assert.Equal(["/var/log/app.log"], LinkTexts(file));
    }

    [Fact]
    public void A_json_key_holding_a_path_is_not_a_link()
    {
        DetailDocument document = JsonDocument(SlogEvent + ",\"C:\\\\logs\\\\app.log\":1}");

        DetailLine key = DetailRig.Line(document.Lines, "  C:");
        Assert.Empty(LinkTexts(key));
        Assert.Equal(SpanStyle.JsonKey, DetailRig.SpansOf(key)[0].Style);
    }

    [Fact]
    public void A_multi_line_json_string_links_its_lines_without_a_string_style()
    {
        DetailDocument document = JsonDocument(SlogEvent + ",\"detail\":\"first\\nsee /var/log/app.log\"}");

        DetailLine line = document.Lines.Single(l => l.Text.Contains("see /var/log/app.log", StringComparison.Ordinal));
        Assert.Equal<(SpanStyle, string)>([(SpanStyle.Link, "/var/log/app.log")], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Stack_frame_lines_have_no_links()
    {
        DetailLine frame = TextDocument("Run failed").Lines.Single(l => l.Text.Contains("SheetKraft.Engine.Runner.Execute", StringComparison.Ordinal));

        Assert.Empty(LinkTexts(frame));
        Assert.Contains((SpanStyle.Directory, @"C:\src\Engine\"), DetailRig.SpansOf(frame));
    }

    [Fact]
    public void Each_link_in_a_message_is_one_span_in_order()
    {
        DetailLine line = DetailRig.Line(TextDocument("http://a/b and /var/log/x").Lines, "message: ");

        Assert.Equal(["http://a/b", "/var/log/x"], LinkTexts(line));
    }
}
