using Logvu.Core.Parsing;
using static Logvu.Core.Tests.Parsing.ParsingFixtures;

namespace Logvu.Core.Tests.Parsing;

public sealed class SchemeDetectorTests
{
    private static ILogScheme? Detect(string fileName, IReadOnlyList<ILogScheme>? candidates = null) =>
        SchemeDetector.Detect(Lines(fileName), candidates ?? BuiltInSchemes.All);

    [Theory]
    [InlineData("log4net-core.log", "log4net-core")]
    [InlineData("log4net-web.log", "log4net-web")]
    [InlineData("log4net-scheduler.log", "log4net-scheduler")]
    [InlineData("log4net-date-first.log", "log4net-date-first")]
    [InlineData("serilog.json", "serilog-json")]
    [InlineData("slog.json", "slog-json")]
    public void Each_fixture_detects_its_scheme(string fileName, string expectedName)
    {
        ILogScheme? scheme = Detect(fileName);

        Assert.NotNull(scheme);
        Assert.Equal(expectedName, scheme.Name);
    }

    [Fact]
    public void Serilog_content_with_only_slog_among_candidates_is_undetected()
    {
        Assert.Null(Detect("serilog.json", [BuiltInSchemes.SlogJson]));
    }

    [Fact]
    public void Slog_content_with_only_serilog_among_candidates_is_undetected()
    {
        Assert.Null(Detect("slog.json", [BuiltInSchemes.SerilogJson]));
    }

    [Fact]
    public void Json_first_line_with_no_text_scheme_considered_is_undetected()
    {
        Assert.Null(Detect("serilog.json", [BuiltInSchemes.Log4NetCore]));
    }

    [Fact]
    public void Empty_input_is_undetected()
    {
        Assert.Null(SchemeDetector.Detect([], BuiltInSchemes.All));
        Assert.Null(SchemeDetector.Detect(["", "   ", "\t"], BuiltInSchemes.All));
    }

    [Fact]
    public void Plain_prose_is_undetected()
    {
        string[] prose =
        [
            "The quick brown fox jumps over the lazy dog.",
            "",
            "Nothing in this file looks like a log record at all.",
            "   indented prose is still prose",
        ];

        Assert.Null(SchemeDetector.Detect(prose, BuiltInSchemes.All));
    }

    [Fact]
    public void Blank_lines_before_the_first_record_are_skipped()
    {
        string[] lines = ["", "   ", .. Lines("log4net-date-first.log")];

        Assert.Same(BuiltInSchemes.Log4NetDateFirst, SchemeDetector.Detect(lines, BuiltInSchemes.All));
    }

    [Fact]
    public void Leading_stack_trace_does_not_prevent_detection_of_the_text_scheme()
    {
        string[] lines = ["   at A.B.C() in x.cs:line 1", .. Lines("log4net-core.log")];

        Assert.Same(BuiltInSchemes.Log4NetCore, SchemeDetector.Detect(lines, BuiltInSchemes.All));
    }

    [Fact]
    public void Only_the_first_twenty_non_blank_lines_are_sampled()
    {
        var lines = new List<string>();
        for (int i = 0; i < SchemeDetector.SampleSize; i++)
        {
            lines.Add($"prose line {i}");
        }

        lines.AddRange(Lines("log4net-core.log"));

        Assert.Null(SchemeDetector.Detect(lines, BuiltInSchemes.All));
    }

    [Fact]
    public void Blank_lines_do_not_count_towards_the_sample()
    {
        var lines = new List<string>();
        for (int i = 0; i < SchemeDetector.SampleSize; i++)
        {
            lines.Add($"prose line {i}");
            lines.Add("");
        }

        lines.AddRange(Lines("log4net-core.log"));

        Assert.Null(SchemeDetector.Detect(lines, BuiltInSchemes.All));
    }

    [Fact]
    public void Most_header_matches_wins_regardless_of_candidate_order()
    {
        // The core fixture has seven headers. An ERROR-only scheme matches one of them, so it loses in either order.
        TextScheme few = new("few", @"ERROR (?<message>.*)", []);
        TextScheme most = BuiltInSchemes.Log4NetCore;
        string[] lines = Lines("log4net-core.log");

        Assert.Same(most, SchemeDetector.Detect(lines, [few, most]));
        Assert.Same(most, SchemeDetector.Detect(lines, [most, few]));
    }

    [Fact]
    public void Ties_go_to_the_earlier_candidate()
    {
        TextScheme first = new("first", @"(?<level>[A-Z]+) (?<message>.*)", []);
        TextScheme second = new("second", @"(?<level>[A-Z]+) (?<message>.*)", []);
        string[] lines = ["INFO one", "WARN two"];

        Assert.Same(first, SchemeDetector.Detect(lines, [first, second]));
        Assert.Same(second, SchemeDetector.Detect(lines, [second, first]));
    }

    [Fact]
    public void Serilog_json_first_line_selects_serilog_over_slog_when_both_are_candidates()
    {
        Assert.Same(BuiltInSchemes.SerilogJson, Detect("serilog.json", [BuiltInSchemes.SlogJson, BuiltInSchemes.SerilogJson]));
    }
}
