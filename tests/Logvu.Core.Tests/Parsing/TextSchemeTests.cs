using System.Diagnostics;
using Logvu.Core.Parsing;
using static Logvu.Core.Tests.Parsing.ParsingFixtures;

namespace Logvu.Core.Tests.Parsing;

public sealed class TextSchemeTests
{
    private static TextScheme Custom(string pattern, params string[] formats) =>
        new("custom", pattern, formats);

    [Fact]
    public void Constructor_rejects_an_invalid_regex()
    {
        var ex = Assert.Throws<ArgumentException>(() => Custom("(unclosed (?<message>.*)"));

        Assert.Contains("not a valid regular expression", ex.Message);
        Assert.Equal("headerPattern", ex.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_pattern_without_a_message_group()
    {
        var ex = Assert.Throws<ArgumentException>(() => Custom(@"(?<level>[A-Z]+) (?<rest>.*)"));

        Assert.Contains("'message'", ex.Message);
        Assert.Equal("headerPattern", ex.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_blank_name_and_a_blank_time_format()
    {
        Assert.Throws<ArgumentException>(() => new TextScheme(" ", "(?<message>.*)", []));
        Assert.Throws<ArgumentException>(() => Custom("(?<time>.+) (?<message>.*)", " "));
    }

    [Fact]
    public void Header_match_is_against_the_whole_line()
    {
        TextScheme scheme = Custom(@"(?<level>[A-Z]+) (?<message>.*)");

        Assert.True(scheme.IsHeader("INFO hello"));
        Assert.True(scheme.IsHeader("INFO hello and more text"));
        Assert.False(scheme.IsHeader("xINFO hello"));
        Assert.False(scheme.IsHeader("   INFO hello"));
    }

    [Fact]
    public void Continuation_lines_are_joined_with_newlines_and_blank_lines_are_kept()
    {
        LogEvent e = BuiltInSchemes.Log4NetCore.Parse(
            "ERROR 2026-11-18 10:20:31,007 [1] A.B - first\n\n   at A.B.C()\n", Utc);

        Assert.Equal("first\n\n   at A.B.C()\n", e.Message);
    }

    [Fact]
    public void Text_that_does_not_start_with_a_header_is_orphan_raw_without_an_error()
    {
        LogEvent e = BuiltInSchemes.Log4NetCore.Parse("   at A.B.C() in x.cs:line 1\nmore", Utc);

        Assert.True(e.IsRaw);
        Assert.Null(e.ParseError);
        Assert.Equal(LogLevel.Unknown, e.Level);
        Assert.Equal("   at A.B.C() in x.cs:line 1\nmore", e.Message);
        Assert.Null(e.Time);
    }

    [Fact]
    public void Level_group_keeps_its_spelling_and_unknown_levels_are_unknown()
    {
        TextScheme scheme = Custom(@"(?<level>[A-Z]+) (?<message>.*)");

        LogEvent e = scheme.Parse("CUSTOM hello", Utc);

        Assert.False(e.IsRaw);
        Assert.Equal(LogLevel.Unknown, e.Level);
        Assert.Equal("CUSTOM", e.LevelText);
        Assert.Equal("hello", e.Message);
    }

    [Fact]
    public void Time_that_fails_the_format_is_null_and_its_text_is_kept()
    {
        TextScheme scheme = Custom(@"(?<time>\d{4}-\d{2}-\d{2}) (?<message>.*)", "yyyy-MM-dd");

        LogEvent e = scheme.Parse("2026-02-31 text", Utc);

        Assert.Null(e.Time);
        Assert.Equal("2026-02-31", e.TimeText);
        Assert.Equal("text", e.Message);
    }

    [Fact]
    public void Unmatched_optional_groups_are_empty_fields()
    {
        TextScheme scheme = Custom(@"(?<level>[A-Z]+)(?: (?<tag>#\w+))? (?<message>.*)");

        LogEvent e = scheme.Parse("INFO hi", Utc);

        ParsingFixtures.AssertFields(e, ("level", "INFO"), ("tag", ""), ("message", "hi"));
    }

    [Fact]
    public void An_event_has_no_source_property_of_its_own()
    {
        // The logger is an ordinary field of every scheme that names one: nothing else carries it.
        Assert.Null(typeof(LogEvent).GetProperty("Source"));
    }

    [Fact]
    public void Logger_group_is_an_ordinary_field_in_pattern_order()
    {
        TextScheme scheme = Custom(@"(?<time>\d{4}) (?<logger>\S+) (?<level>[A-Z]+) (?<id>\d+) (?<message>.*)", "yyyy");

        LogEvent e = scheme.Parse("2026 Svc.A WARN 9 text", Utc);

        Assert.Equal(LogLevel.Warn, e.Level);
        ParsingFixtures.AssertFields(e,
            ("time", "2026"),
            ("logger", "Svc.A"),
            ("level", "WARN"),
            ("id", "9"),
            ("message", "text"));
    }

    [Fact]
    public void Message_is_a_field_in_its_pattern_position_holding_the_whole_message_with_its_continuation_lines()
    {
        TextScheme scheme = Custom(@"(?<level>[A-Z]+) (?<message>.+?) \| (?<tag>\w+)");

        LogEvent e = scheme.Parse("WARN first part | ops\n  second line\n\nthird line", Utc);

        AssertFields(e,
            ("level", "WARN"),
            ("message", "first part\n  second line\n\nthird line"),
            ("tag", "ops"));
        Assert.Equal("first part\n  second line\n\nthird line", e.Message);
    }

    [Fact]
    public void A_header_only_read_has_no_message_field_and_the_message_of_its_first_line()
    {
        TextScheme scheme = Custom(@"(?<level>[A-Z]+) (?<message>.*)");

        LogEvent e = scheme.ParseHeader("WARN first line\n  more", Utc, withTime: false);

        Assert.Empty(e.Fields);
        Assert.Equal(string.Empty, e.Message);
    }

    [Fact]
    public void Catastrophic_header_pattern_times_out_as_not_a_header_in_bounded_time()
    {
        TextScheme scheme = Custom("^(a+)+$(?<message>)");
        string line = new string('a', 40) + "b";

        var clock = Stopwatch.StartNew();
        bool isHeader = scheme.IsHeader(line);
        LogEvent e = scheme.Parse(line, Utc);
        clock.Stop();

        Assert.False(isHeader);
        Assert.True(e.IsRaw);
        Assert.Equal("header pattern timed out", e.ParseError);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }
}
