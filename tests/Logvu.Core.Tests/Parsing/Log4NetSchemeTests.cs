using Logvu.Core.Parsing;
using static Logvu.Core.Tests.Parsing.ParsingFixtures;

namespace Logvu.Core.Tests.Parsing;

public sealed class Log4NetSchemeTests
{
    private const string CoreHeader = "INFO  2026-11-18 10:20:30,123 [12] SheetKraft.Engine.Runner - Started run 42";
    private const string WebHeader = "WARN  2026-11-18 10:20:30,123 v.5.2.1 u.17 10.0.0.5 10.0.0.1 [23] SheetKraftWeb.Controllers.ApiController - Slow request";
    private const string SchedulerHeader = "[2026-11-18 10:20:30] INFO  Task Daily-Import started";
    private const string DateFirstHeader = "2026-11-18 10:20:30,123 [1] INFO AxisBankBalance.Program - Fetched 12 rows";

    [Fact]
    public void Core_parses_padded_level_and_thread_with_a_space()
    {
        LogEvent e = BuiltInSchemes.Log4NetCore.Parse(
            "ERROR 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - Run failed", Custom);

        Assert.False(e.IsRaw);
        AssertTime(e.Time, new DateTime(2026, 11, 18, 10, 20, 31, 7), TimeSpan.FromHours(5.5));
        Assert.Equal("2026-11-18 10:20:31,007", e.TimeText);
        Assert.Equal(LogLevel.Error, e.Level);
        Assert.Equal("ERROR", e.LevelText);
        Assert.Equal("SheetKraft.Engine.Runner", e.Source);
        Assert.Equal("Run failed", e.Message);
        Assert.Null(e.Json);
        AssertFields(e,
            ("level", "ERROR"),
            ("time", "2026-11-18 10:20:31,007"),
            ("thread", "Worker 3"),
            ("logger", "SheetKraft.Engine.Runner"));
    }

    [Fact]
    public void Core_keeps_the_stack_trace_and_blank_line_in_the_message()
    {
        var scheme = BuiltInSchemes.Log4NetCore;
        string[] events = TextEvents("log4net-core.log", scheme).ToArray();

        // Event 4 of the fixture is the ERROR with the .NET stack trace and a blank continuation line.
        LogEvent e = scheme.Parse(events[3], Utc);

        Assert.Equal(
            "Run failed\n" +
            "System.InvalidOperationException: Sheet not found\n" +
            "   at SheetKraft.Engine.Runner.Execute(Int32 id) in C:\\src\\Engine\\Runner.cs:line 88\n" +
            " ---> System.IO.IOException: locked\n" +
            "   --- End of inner exception stack trace ---\n" +
            "\n" +
            "Retrying is not possible; see the log for run 42.",
            e.Message);
        Assert.Equal(LogLevel.Error, e.Level);
    }

    [Fact]
    public void Core_offset_free_time_is_read_in_the_context_zone()
    {
        LogEvent custom = BuiltInSchemes.Log4NetCore.Parse(CoreHeader, Custom);
        LogEvent utc = BuiltInSchemes.Log4NetCore.Parse(CoreHeader, Utc);

        AssertTime(custom.Time, new DateTime(2026, 11, 18, 10, 20, 30, 123), TimeSpan.FromHours(5.5));
        AssertTime(utc.Time, new DateTime(2026, 11, 18, 10, 20, 30, 123), TimeSpan.Zero);
    }

    [Fact]
    public void Core_time_that_does_not_parse_is_null_with_its_text_kept()
    {
        LogEvent e = BuiltInSchemes.Log4NetCore.Parse(
            "INFO  2026-13-40 10:20:30,123 [12] SheetKraft.Engine.Runner - Bad date", Utc);

        Assert.False(e.IsRaw);
        Assert.Null(e.Time);
        Assert.Equal("2026-13-40 10:20:30,123", e.TimeText);
        Assert.Equal("Bad date", e.Message);
    }

    [Fact]
    public void Web_fields_are_in_pattern_order_including_the_null_placeholders()
    {
        LogEvent e = BuiltInSchemes.Log4NetWeb.Parse(
            "ERROR 2026-11-18 10:20:31,007 v.(null) u.(null) (null) (null) [7] SheetKraftWeb.Startup - Anonymous",
            Custom);

        Assert.False(e.IsRaw);
        Assert.Equal(LogLevel.Error, e.Level);
        Assert.Equal("SheetKraftWeb.Startup", e.Source);
        Assert.Equal("Anonymous", e.Message);
        AssertFields(e,
            ("level", "ERROR"),
            ("time", "2026-11-18 10:20:31,007"),
            ("version", "(null)"),
            ("user", "(null)"),
            ("sourceAddress", "(null)"),
            ("localAddress", "(null)"),
            ("thread", "7"),
            ("logger", "SheetKraftWeb.Startup"));
    }

    [Fact]
    public void Web_parses_the_populated_example()
    {
        LogEvent e = BuiltInSchemes.Log4NetWeb.Parse(WebHeader, Custom);

        Assert.Equal(LogLevel.Warn, e.Level);
        Assert.Equal("WARN", e.LevelText);
        AssertTime(e.Time, new DateTime(2026, 11, 18, 10, 20, 30, 123), TimeSpan.FromHours(5.5));
        Assert.Equal("SheetKraftWeb.Controllers.ApiController", e.Source);
        Assert.Equal("Slow request", e.Message);
        AssertFields(e,
            ("level", "WARN"),
            ("time", "2026-11-18 10:20:30,123"),
            ("version", "5.2.1"),
            ("user", "17"),
            ("sourceAddress", "10.0.0.5"),
            ("localAddress", "10.0.0.1"),
            ("thread", "23"),
            ("logger", "SheetKraftWeb.Controllers.ApiController"));
    }

    [Fact]
    public void Scheduler_parses_bracketed_time_without_fraction_and_has_no_source()
    {
        LogEvent e = BuiltInSchemes.Log4NetScheduler.Parse(SchedulerHeader, Utc);

        Assert.False(e.IsRaw);
        AssertTime(e.Time, new DateTime(2026, 11, 18, 10, 20, 30), TimeSpan.Zero);
        Assert.Equal("2026-11-18 10:20:30", e.TimeText);
        Assert.Equal(LogLevel.Info, e.Level);
        Assert.Equal("INFO", e.LevelText);
        Assert.Null(e.Source);
        Assert.Equal("Task Daily-Import started", e.Message);
        AssertFields(e, ("time", "2026-11-18 10:20:30"), ("level", "INFO"));
    }

    [Fact]
    public void Date_first_parses_unpadded_level()
    {
        LogEvent e = BuiltInSchemes.Log4NetDateFirst.Parse(DateFirstHeader, Custom);

        Assert.False(e.IsRaw);
        AssertTime(e.Time, new DateTime(2026, 11, 18, 10, 20, 30, 123), TimeSpan.FromHours(5.5));
        Assert.Equal(LogLevel.Info, e.Level);
        Assert.Equal("AxisBankBalance.Program", e.Source);
        Assert.Equal("Fetched 12 rows", e.Message);
        AssertFields(e,
            ("time", "2026-11-18 10:20:30,123"),
            ("thread", "1"),
            ("level", "INFO"),
            ("logger", "AxisBankBalance.Program"));
    }

    [Theory]
    [InlineData("log4net-core.log", 7)]
    [InlineData("log4net-web.log", 6)]
    [InlineData("log4net-scheduler.log", 7)]
    [InlineData("log4net-date-first.log", 6)]
    public void Fixtures_frame_into_events_that_all_parse(string fileName, int expectedEvents)
    {
        ILogScheme scheme = fileName switch
        {
            "log4net-core.log" => BuiltInSchemes.Log4NetCore,
            "log4net-web.log" => BuiltInSchemes.Log4NetWeb,
            "log4net-scheduler.log" => BuiltInSchemes.Log4NetScheduler,
            _ => BuiltInSchemes.Log4NetDateFirst,
        };

        List<string> events = TextEvents(fileName, scheme);

        Assert.Equal(expectedEvents, events.Count);
        foreach (string text in events)
        {
            LogEvent e = scheme.Parse(text, Custom);
            Assert.False(e.IsRaw, $"event did not parse: {text}");
            Assert.NotNull(e.Time);
            Assert.NotEqual(LogLevel.Unknown, e.Level);
        }
    }

    [Theory]
    [InlineData("   at SheetKraft.Engine.Runner.Execute(Int32 id) in C:\\src\\Engine\\Runner.cs:line 88")]
    [InlineData(" ---> System.IO.IOException: locked")]
    [InlineData("   --- End of inner exception stack trace ---")]
    [InlineData("System.InvalidOperationException: Sheet not found")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-11-18 10:20:31 retry scheduled")]
    [InlineData("Retrying is not possible; see the log for run 42.")]
    public void Continuation_lines_are_not_headers(string line)
    {
        Assert.False(BuiltInSchemes.Log4NetCore.IsHeader(line));
        Assert.False(BuiltInSchemes.Log4NetWeb.IsHeader(line));
        Assert.False(BuiltInSchemes.Log4NetScheduler.IsHeader(line));
        Assert.False(BuiltInSchemes.Log4NetDateFirst.IsHeader(line));
    }

    [Theory]
    [InlineData("INFO  2026-11-18 10:20:30 [12] SheetKraft.Engine.Runner - no milliseconds")]
    [InlineData("INFO  2026-11-18 10:20:30,123 SheetKraft.Engine.Runner - no thread brackets")]
    [InlineData("INFO  2026-11-18 10:20:30,123 [12] Runner without separator")]
    [InlineData("2026-11-18 10:20:30,123 [1] INFO AxisBankBalance.Program - date first")]
    [InlineData("[2026-11-18 10:20:30] INFO  scheduler line")]
    public void Core_rejects_headers_of_other_layouts(string line)
    {
        Assert.False(BuiltInSchemes.Log4NetCore.IsHeader(line));
    }

    [Theory]
    [InlineData("2026-11-18 10:20:31,007 retry without brackets")]
    [InlineData("2026-11-18 10:20:31 [1] INFO Logger - seconds only")]
    [InlineData("2026-11-18 10:20:31,007 [1] INFO Logger")]
    public void Date_first_rejects_a_date_line_with_the_wrong_layout(string line)
    {
        Assert.False(BuiltInSchemes.Log4NetDateFirst.IsHeader(line));
    }

    [Theory]
    [InlineData("2026-11-18 10:20:31 retry scheduled")]
    [InlineData("[2026-11-18 10:20:31]")]
    [InlineData("[2026-11-18 10:20:31] 42 not a level")]
    public void Scheduler_rejects_a_bracketed_date_without_a_level_word(string line)
    {
        Assert.False(BuiltInSchemes.Log4NetScheduler.IsHeader(line));
    }

    [Fact]
    public void Each_text_header_matches_only_its_own_scheme()
    {
        var schemes = new[] { BuiltInSchemes.Log4NetCore, BuiltInSchemes.Log4NetWeb, BuiltInSchemes.Log4NetScheduler, BuiltInSchemes.Log4NetDateFirst };
        var headers = new[] { CoreHeader, WebHeader, SchedulerHeader, DateFirstHeader };

        for (int h = 0; h < headers.Length; h++)
        {
            for (int s = 0; s < schemes.Length; s++)
            {
                Assert.Equal(h == s, schemes[s].IsHeader(headers[h]));
            }
        }
    }
}
