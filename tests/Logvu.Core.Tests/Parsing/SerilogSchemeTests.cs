using System.Text.Json;
using Logvu.Core.Parsing;
using static Logvu.Core.Tests.Parsing.ParsingFixtures;

namespace Logvu.Core.Tests.Parsing;

public sealed class SerilogSchemeTests
{
    private const string SpecExample =
        """{"Timestamp":"2026-11-18T10:20:30.1234567+05:30","Level":"Error","MessageTemplate":"Failed to process {OrderId} for {@Customer} {{literal}}","Exception":"System.InvalidOperationException: boom\r\n   at A.B.C() in C:\\src\\x.cs:line 10","Properties":{"OrderId":42,"Customer":{"Name":"Asha"},"SourceContext":"SampleDotNetApp.Orders","Scope":["Request 7"],"ExceptionDetail":{"Type":"System.InvalidOperationException","Message":"boom","HResult":-2146233079}}}""";

    private static readonly SerilogJsonScheme Scheme = BuiltInSchemes.SerilogJson;

    [Fact]
    public void Spec_example_parses_time_level_message_and_fields()
    {
        LogEvent e = Scheme.Parse(SpecExample, Utc);

        Assert.False(e.IsRaw);
        AssertTime(e.Time, new DateTime(2026, 11, 18, 10, 20, 30, 123, DateTimeKind.Unspecified).AddTicks(4567), TimeSpan.FromHours(5.5));
        Assert.Equal("2026-11-18T10:20:30.1234567+05:30", e.TimeText);
        Assert.Equal(LogLevel.Error, e.Level);
        Assert.Equal("Error", e.LevelText);
        Assert.Equal("SampleDotNetApp.Orders", e.Json!.Value.GetProperty("Properties").GetProperty("SourceContext").GetString());
        Assert.Equal("Failed to process 42 for {\"Name\":\"Asha\"} {literal}", e.Message);
        AssertFields(e,
            ("Timestamp", "2026-11-18T10:20:30.1234567+05:30"),
            ("Level", "Error"),
            ("MessageTemplate", "Failed to process {OrderId} for {@Customer} {{literal}}"),
            ("Exception", "System.InvalidOperationException: boom\r\n   at A.B.C() in C:\\src\\x.cs:line 10"),
            ("Properties", """{"OrderId":42,"Customer":{"Name":"Asha"},"SourceContext":"SampleDotNetApp.Orders","Scope":["Request 7"],"ExceptionDetail":{"Type":"System.InvalidOperationException","Message":"boom","HResult":-2146233079}}"""));
    }

    [Fact]
    public void Json_stays_usable_after_the_document_is_disposed()
    {
        LogEvent e = Scheme.Parse(SpecExample, Utc);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.NotNull(e.Json);
        JsonElement properties = e.Json.Value.GetProperty("Properties");
        Assert.Equal("SampleDotNetApp.Orders", properties.GetProperty("SourceContext").GetString());
        Assert.Equal("Request 7", properties.GetProperty("Scope")[0].GetString());
        Assert.Equal(-2146233079, properties.GetProperty("ExceptionDetail").GetProperty("HResult").GetInt32());
    }

    [Fact]
    public void Rendered_message_is_preferred_over_the_template()
    {
        LogEvent e = Scheme.Parse(
            """{"Timestamp":"2026-11-18T10:20:31.5000000+05:30","Level":"Warning","MessageTemplate":"Retry {Attempt} of {Max}","RenderedMessage":"Retry 2 of 5 (rendered)","Properties":{"Attempt":2,"Max":5}}""",
            Utc);

        Assert.Equal("Retry 2 of 5 (rendered)", e.Message);
        Assert.Equal(LogLevel.Warn, e.Level);
    }

    [Fact]
    public void Rendered_message_present_is_never_marked_as_rendered_by_logvu()
    {
        LogEvent e = Scheme.Parse(
            """{"Timestamp":"2026-11-18T10:20:31.5000000+05:30","Level":"Warning","MessageTemplate":"Retry {Attempt} of {Max}","RenderedMessage":"Retry 2 of 5 (rendered)","Properties":{"Attempt":2,"Max":5}}""",
            Utc);

        Assert.Equal("Retry 2 of 5 (rendered)", e.Message);
        Assert.False(e.MessageRendered);
    }

    [Theory]
    [InlineData("Started {RunId}", """{"RunId":42}""", true)]
    [InlineData("{{literal}}", "{}", true)]
    [InlineData("Server started", "{}", false)]
    [InlineData("Missing {Nope} here", """{"A":1}""", false)]
    public void Message_is_marked_rendered_only_when_the_rendered_text_differs_from_the_template(string template, string properties, bool rendered)
    {
        LogEvent e = Scheme.Parse(
            $$"""{"Timestamp":"2026-11-18T10:20:30+05:30","Level":"Information","MessageTemplate":"{{template}}","Properties":{{properties}}}""",
            Utc);

        Assert.Equal(rendered, e.MessageRendered);
    }

    [Fact]
    public void Non_string_template_is_not_marked_as_rendered()
    {
        LogEvent e = Scheme.Parse("""{"Timestamp":"2026-11-18T10:20:30+05:30","Level":"Information","MessageTemplate":5}""", Utc);

        Assert.Equal("5", e.Message);
        Assert.False(e.MessageRendered);
    }

    [Fact]
    public void Missing_template_and_rendered_message_is_not_marked_as_rendered()
    {
        LogEvent e = Scheme.Parse("""{"Timestamp":"2026-11-18T10:20:30+05:30","Level":"Debug"}""", Utc);

        Assert.False(e.MessageRendered);
    }

    [Fact]
    public void Missing_level_is_info_spelled_information()
    {
        LogEvent e = Scheme.Parse("""{"Timestamp":"2026-11-18T10:20:30+05:30","MessageTemplate":"hi"}""", Utc);

        Assert.Equal(LogLevel.Info, e.Level);
        Assert.Equal("Information", e.LevelText);
    }

    [Fact]
    public void Missing_template_and_rendered_message_give_an_empty_message()
    {
        LogEvent e = Scheme.Parse("""{"Timestamp":"2026-11-18T10:20:30+05:30","Level":"Debug"}""", Utc);

        Assert.False(e.IsRaw);
        Assert.Equal(string.Empty, e.Message);
        Assert.Equal(LogLevel.Debug, e.Level);
    }

    [Fact]
    public void Properties_stay_one_field_without_a_source_context()
    {
        LogEvent e = Scheme.Parse("""{"Timestamp":"2026-11-18T10:20:30+05:30","Level":"Debug","MessageTemplate":"x","Properties":{"A":1}}""", Utc);

        Assert.Contains(e.Fields, field => field.Name == "Properties");
    }

    [Fact]
    public void Verbose_is_trace_and_warning_is_warn()
    {
        Assert.Equal(LogLevel.Trace, Scheme.Parse("""{"Timestamp":"2026-11-18T10:20:30+05:30","Level":"Verbose"}""", Utc).Level);
        Assert.Equal(LogLevel.Warn, Scheme.Parse("""{"Timestamp":"2026-11-18T10:20:30+05:30","Level":"Warning"}""", Utc).Level);
    }

    [Fact]
    public void Offset_free_timestamp_is_read_in_the_context_zone()
    {
        const string line = """{"Timestamp":"2026-11-18T10:20:30.5","Level":"Information","MessageTemplate":"x"}""";

        AssertTime(Scheme.Parse(line, Custom).Time, new DateTime(2026, 11, 18, 10, 20, 30, 500), TimeSpan.FromHours(5.5));
        AssertTime(Scheme.Parse(line, Utc).Time, new DateTime(2026, 11, 18, 10, 20, 30, 500), TimeSpan.Zero);
    }

    [Fact]
    public void Timestamp_that_does_not_parse_is_null_with_its_text_kept()
    {
        LogEvent e = Scheme.Parse("""{"Timestamp":"yesterday","Level":"Information","MessageTemplate":"x"}""", Utc);

        Assert.False(e.IsRaw);
        Assert.Null(e.Time);
        Assert.Equal("yesterday", e.TimeText);
    }

    [Fact]
    public void Missing_timestamp_is_null_time_and_null_text()
    {
        LogEvent e = Scheme.Parse("""{"Level":"Information","MessageTemplate":"x"}""", Utc);

        Assert.Null(e.Time);
        Assert.Null(e.TimeText);
    }

    [Theory]
    [InlineData("Started {RunId}", """{"RunId":42}""", "Started 42")]
    [InlineData("Missing {Nope} here", """{"A":1}""", "Missing {Nope} here")]
    [InlineData("{0} and {1}", """{"0":"zero","1":7}""", "zero and 7")]
    [InlineData("{Count:000} items", """{"Count":42}""", "42 items")]
    [InlineData("[{Name,-10}]", """{"Name":"Asha"}""", "[Asha]")]
    [InlineData("[{Name,-10:x}]", """{"Name":"Asha"}""", "[Asha]")]
    [InlineData("{{literal}}", """{}""", "{literal}")]
    [InlineData("{{{Value}}}", """{"Value":7}""", "{7}")]
    [InlineData("a }} b", """{}""", "a } b")]
    [InlineData("open {Name", """{"Name":"x"}""", "open {Name")]
    [InlineData("empty {} hole", """{}""", "empty {} hole")]
    [InlineData("spaced { Name }", """{"Name":"x"}""", "spaced { Name }")]
    [InlineData("{S} {N} {Z} {B} {O} {A}", """{"S":"text","N":3.5,"Z":null,"B":true,"O":{"k":[1,2]},"A":[1,"x"]}""", """text 3.5 null true {"k":[1,2]} [1,"x"]""")]
    [InlineData("{@O} {$S}", """{"O":{"k":"v"},"S":"plain"}""", """{"k":"v"} plain""")]
    [InlineData("{Missing}", """{"A":1}""", "{Missing}")]
    public void Template_rendering(string template, string properties, string expected)
    {
        using JsonDocument document = JsonDocument.Parse(properties);

        Assert.Equal(expected, SerilogTemplate.Render(template, document.RootElement));
    }

    [Fact]
    public void Template_with_no_properties_keeps_every_hole()
    {
        Assert.Equal("a {X} b", SerilogTemplate.Render("a {X} b", null));
    }

    [Fact]
    public void Malformed_line_is_raw_with_the_parse_error()
    {
        LogEvent e = Scheme.Parse("""{"Timestamp":"2026-11-18T10:20:32.0000000+05:30","Level":"Error","MessageTemplate":"Truncated {Thing""", Utc);

        Assert.True(e.IsRaw);
        Assert.NotNull(e.ParseError);
        Assert.Equal(LogLevel.Unknown, e.Level);
        Assert.Null(e.Json);
    }

    [Fact]
    public void Json_array_is_raw_with_a_parse_error()
    {
        LogEvent e = Scheme.Parse("""["2026-11-18T10:20:34+05:30","Information","not an object"]""", Utc);

        Assert.True(e.IsRaw);
        Assert.Equal("line is not a JSON object", e.ParseError);
        Assert.Equal("""["2026-11-18T10:20:34+05:30","Information","not an object"]""", e.Message);
    }

    [Fact]
    public void Raw_factory_builds_an_unknown_level_event_with_the_text_as_message()
    {
        LogEvent e = LogEvent.Raw("stray", null);

        Assert.True(e.IsRaw);
        Assert.Null(e.ParseError);
        Assert.Equal("stray", e.Message);
        Assert.Equal(LogLevel.Unknown, e.Level);
        Assert.Empty(e.Fields);
    }

    [Fact]
    public void Fixture_has_one_raw_malformed_line_and_one_array_line_and_the_rest_parse()
    {
        List<string> lines = JsonEvents("serilog.json");

        List<LogEvent> events = lines.Select(line => Scheme.Parse(line, Utc)).ToList();

        Assert.Equal(9, events.Count);
        Assert.Equal(7, events.Count(e => !e.IsRaw));
        Assert.Equal(2, events.Count(e => e.IsRaw));
        Assert.All(events.Where(e => !e.IsRaw), e => Assert.NotNull(e.Json));
    }

    [Fact]
    public void Fixture_error_event_renders_the_template_and_keeps_the_exception()
    {
        LogEvent e = Scheme.Parse(JsonEvents("serilog.json")[2], Utc);

        Assert.Equal("Failed to process 42 for {\"Name\":\"Asha\"} {literal}", e.Message);
        Assert.Contains("\r\n", e.Fields.Single(f => f.Name == "Exception").Value);
    }

    [Fact]
    public void Fixture_missing_property_stays_verbatim()
    {
        LogEvent e = Scheme.Parse(JsonEvents("serilog.json")[8], Utc);

        Assert.Equal("Cache holds 3 entries, {Missing} missing", e.Message);
        Assert.Equal(LogLevel.Trace, e.Level);
    }

    [Fact]
    public void Fixture_fields_are_top_level_members_in_order()
    {
        LogEvent e = Scheme.Parse(JsonEvents("serilog.json")[3], Utc);

        AssertFields(e,
            ("Timestamp", "2026-11-18T10:20:31.5000000+05:30"),
            ("Level", "Warning"),
            ("MessageTemplate", "Retry {Attempt} of {Max}"),
            ("RenderedMessage", "Retry 2 of 5"),
            ("Properties", """{"Attempt":2,"Max":5,"SourceContext":"SampleDotNetApp.Retry"}"""));
    }
}
