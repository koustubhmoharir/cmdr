using System.Text;
using System.Text.Json.Nodes;
using Logvu.Core.Config;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using static Logvu.Core.Tests.Parsing.ParsingFixtures;

namespace Logvu.Core.Tests.Store;

public sealed class MemberProbeTests
{
    private const string Header = "INFO  2026-11-18 10:20:30,123 [12] SheetKraft.Engine.Runner - Started run 42";

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static ProbeResult ProbeFile(string path, LogvuConfig config, bool live)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return MemberProbe.Probe(stream, config, live);
    }

    private static ProbeResult ProbeText(ConfigTestDirectory dir, string content, bool live, LogvuConfig? config = null)
    {
        string path = dir.Write("SheetKraft.log", content);
        return ProbeFile(path, config ?? StoreTestConfig.Create(dir), live);
    }

    /// <summary>A configured scheme with the same header as log4net-core, under another name.</summary>
    private static JsonArray CustomCoreScheme() => new(new JsonObject
    {
        ["name"] = "my-core",
        ["header"] = @"(?<level>[A-Za-z]+) +(?<time>[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2},[0-9]{3}) \[(?<thread>[^\]]*)\] (?<logger>\S+) - (?<message>.*)",
        ["timeFormats"] = new JsonArray("yyyy-MM-dd HH:mm:ss,fff"),
    });

    private static string Lines(int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"prose line {i}\n"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_file_is_pending(bool live)
    {
        using var dir = new ConfigTestDirectory();

        ProbeResult result = ProbeText(dir, string.Empty, live);

        Assert.Equal(MemberState.Pending, result.State);
        Assert.Null(result.Scheme);
        Assert.Equal(0, result.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Byte_order_mark_only_is_pending(bool live)
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.WriteBytes("SheetKraft.log", [0xEF, 0xBB, 0xBF]);

        ProbeResult result = ProbeFile(path, StoreTestConfig.Create(dir), live);

        Assert.Equal(MemberState.Pending, result.State);
        Assert.Equal(3, result.Length);
    }

    [Fact]
    public void Live_file_with_only_an_unterminated_header_line_is_pending()
    {
        using var dir = new ConfigTestDirectory();

        ProbeResult result = ProbeText(dir, Header, live: true);

        Assert.Equal(MemberState.Pending, result.State);
        Assert.Null(result.FirstTime);
    }

    [Fact]
    public void Closed_file_with_only_an_unterminated_header_line_is_dated()
    {
        using var dir = new ConfigTestDirectory();

        ProbeResult result = ProbeText(dir, Header, live: false);

        Assert.Equal(MemberState.Dated, result.State);
        Assert.Equal("log4net-core", result.Scheme!.Name);
        Assert.Equal(0, result.FirstEventOffset);
        AssertTime(result.FirstTime, new DateTime(2026, 11, 18, 10, 20, 30, 123), TimeSpan.Zero);
    }

    [Fact]
    public void Live_header_with_continuation_lines_gives_its_time_from_the_provisional_event()
    {
        using var dir = new ConfigTestDirectory();

        ProbeResult result = ProbeText(dir, Header + "\n   at SomeFrame()\n", live: true);

        Assert.Equal(MemberState.Dated, result.State);
        AssertTime(result.FirstTime, new DateTime(2026, 11, 18, 10, 20, 30, 123), TimeSpan.Zero);
    }

    [Fact]
    public void Orphan_lines_then_a_header_are_dated_at_the_header()
    {
        using var dir = new ConfigTestDirectory();
        const string orphans = "orphan one\n   at SomeFrame()\n";

        ProbeResult result = ProbeText(dir, orphans + Header + "\nmore\n", live: false);

        Assert.Equal(MemberState.Dated, result.State);
        Assert.Equal(Encoding.UTF8.GetByteCount(orphans), result.FirstEventOffset);
        AssertTime(result.FirstTime, new DateTime(2026, 11, 18, 10, 20, 30, 123), TimeSpan.Zero);
    }

    [Theory]
    [InlineData("log4net-core.log", "log4net-core", 2026, 11, 18, 10, 20, 30, 123, 0L)]
    [InlineData("log4net-web.log", "log4net-web", 2026, 11, 18, 10, 20, 30, 123, 0L)]
    [InlineData("log4net-scheduler.log", "log4net-scheduler", 2026, 11, 18, 10, 20, 30, 0, 0L)]
    [InlineData("log4net-date-first.log", "log4net-date-first", 2026, 11, 18, 10, 20, 30, 123, 0L)]
    public void Text_fixtures_are_dated_with_their_scheme_and_first_time(
        string fileName, string schemeName, int year, int month, int day, int hour, int minute, int second, int millisecond, long firstOffset)
    {
        using var dir = new ConfigTestDirectory();
        var local = new DateTime(year, month, day, hour, minute, second, millisecond);

        ProbeResult result = ProbeFile(FixturePath(fileName), StoreTestConfig.Create(dir), live: false);

        Assert.Equal(MemberState.Dated, result.State);
        Assert.Equal(schemeName, result.Scheme!.Name);
        Assert.Equal(firstOffset, result.FirstEventOffset);
        AssertTime(result.FirstTime, local, TimeSpan.Zero);
    }

    [Theory]
    [InlineData("serilog.json", "serilog-json")]
    [InlineData("slog.json", "slog-json")]
    public void Json_fixtures_are_dated_with_their_scheme_and_keep_their_offset(string fileName, string schemeName)
    {
        using var dir = new ConfigTestDirectory();

        ProbeResult result = ProbeFile(FixturePath(fileName), StoreTestConfig.Create(dir), live: false);

        // Both first lines read 10:20:30.1234567 at +05:30 (slog keeps nine digits, truncated to seven).
        Assert.Equal(MemberState.Dated, result.State);
        Assert.Equal(schemeName, result.Scheme!.Name);
        AssertTime(result.FirstTime, new DateTime(2026, 11, 18, 10, 20, 30).AddTicks(1234567), TimeSpan.FromHours(5.5));
    }

    [Fact]
    public void Forced_format_overrides_sniffing()
    {
        using var dir = new ConfigTestDirectory();
        LogvuConfig forced = StoreTestConfig.Create(dir, format: "my-core", schemes: CustomCoreScheme());
        LogvuConfig sniffed = StoreTestConfig.Create(dir, schemes: CustomCoreScheme());

        ProbeResult overridden = ProbeFile(FixturePath("log4net-core.log"), forced, live: false);
        ProbeResult detected = ProbeFile(FixturePath("log4net-core.log"), sniffed, live: false);

        Assert.Equal("my-core", overridden.Scheme!.Name);
        Assert.Equal(MemberState.Dated, overridden.State);
        AssertTime(overridden.FirstTime, new DateTime(2026, 11, 18, 10, 20, 30, 123), TimeSpan.Zero);
        Assert.Equal("log4net-core", detected.Scheme!.Name);
    }

    [Fact]
    public void Prose_file_closed_is_unknown_time_with_the_raw_scheme()
    {
        using var dir = new ConfigTestDirectory();

        ProbeResult result = ProbeText(dir, "This is not a log.\nNothing here either.\n", live: false);

        Assert.Equal(MemberState.UnknownTime, result.State);
        Assert.Equal("raw", result.Scheme!.Name);
        Assert.Equal(MemberProbe.FormatNotRecognized, result.Diagnostic);
        Assert.Equal(0, result.FirstEventOffset);
        Assert.Null(result.FirstTime);
    }

    [Theory]
    [InlineData(3, MemberState.Pending)]
    [InlineData(19, MemberState.Pending)]
    [InlineData(20, MemberState.UnknownTime)]
    [InlineData(25, MemberState.UnknownTime)]
    public void Live_prose_file_waits_for_a_sample_before_it_is_raw(int lines, MemberState expected)
    {
        using var dir = new ConfigTestDirectory();

        ProbeResult result = ProbeText(dir, Lines(lines), live: true);

        Assert.Equal(expected, result.State);
        if (expected == MemberState.Pending)
        {
            Assert.Null(result.Scheme);
        }
        else
        {
            Assert.Equal("raw", result.Scheme!.Name);
        }
    }

    [Fact]
    public void Header_whose_date_does_not_parse_is_unknown_time_with_a_diagnostic()
    {
        using var dir = new ConfigTestDirectory();

        // The header pattern admits any digits, so the line is a header; only the time fails to parse.
        ProbeResult result = ProbeText(dir, "INFO  2026-13-45 10:20:30,123 [12] SheetKraft.Engine.Runner - bad date\n", live: false);

        Assert.Equal(MemberState.UnknownTime, result.State);
        Assert.Equal("log4net-core", result.Scheme!.Name);
        Assert.Equal(MemberProbe.NoReadableTime, result.Diagnostic);
        Assert.Null(result.FirstTime);
        Assert.Equal(0, result.FirstEventOffset);
    }

    [Fact]
    public void Closed_file_with_orphan_lines_and_no_header_is_unknown_time_when_scheme_is_forced()
    {
        using var dir = new ConfigTestDirectory();
        LogvuConfig config = StoreTestConfig.Create(dir, format: "log4net-core");

        ProbeResult result = ProbeText(dir, "no header here\nor here\n", live: false, config);

        Assert.Equal(MemberState.UnknownTime, result.State);
        Assert.Equal("log4net-core", result.Scheme!.Name);
        Assert.Equal(MemberProbe.NoEventHeader, result.Diagnostic);
    }

    [Fact]
    public void Time_zone_from_the_configuration_is_applied_to_a_time_without_offset()
    {
        using var dir = new ConfigTestDirectory();
        LogvuConfig config = StoreTestConfig.Create(dir, timeZone: "Asia/Kolkata");

        ProbeResult result = ProbeText(dir, Header + "\n", live: false, config);

        Assert.Equal(MemberState.Dated, result.State);
        AssertTime(result.FirstTime, new DateTime(2026, 11, 18, 10, 20, 30, 123), TimeSpan.FromHours(5.5));
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 4, 50, 30, 123, TimeSpan.Zero), result.FirstTime!.Value.ToUniversalTime());
    }
}
