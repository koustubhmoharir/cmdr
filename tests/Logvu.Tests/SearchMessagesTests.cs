using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Tui;

namespace Logvu.Tests;

/// <summary>The status texts of the search, and the throttle of its progress reports.</summary>
public sealed class SearchMessagesTests
{
    [Theory]
    [InlineData(0L, "Searching… 0.0 MB")]
    [InlineData(12_900_000L, "Searching… 12.3 MB")]
    [InlineData(5L * 1024 * 1024, "Searching… 5.0 MB")]
    public void Progress_shows_the_bytes_scanned_in_megabytes(long bytes, string expected)
    {
        Assert.Equal(expected, SearchMessages.Progress(bytes));
    }

    [Fact]
    public void A_match_shows_the_time_of_its_row_in_the_zone_to_the_second()
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "Plus two", "Plus two");
        EventRow row = Row(new DateTimeOffset(2026, 11, 18, 10, 20, 31, 7, TimeSpan.Zero), timeText: null);

        Assert.Equal("Match 2026-11-18 12:20:31", SearchMessages.Match(row, zone));
    }

    [Fact]
    public void A_match_without_a_time_shows_its_time_text_or_a_dash()
    {
        Assert.Equal("Match 18/11 10:20", SearchMessages.Match(Row(null, timeText: "18/11 10:20"), TimeZoneInfo.Utc));
        Assert.Equal("Match -", SearchMessages.Match(Row(null, timeText: null), TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData(true, "No more matches above")]
    [InlineData(false, "No more matches below")]
    public void The_boundary_names_the_side_the_search_could_not_go(bool older, string expected)
    {
        Assert.Equal(expected, SearchMessages.Boundary(older));
    }

    [Fact]
    public void A_read_error_shows_the_file_name_and_the_message()
    {
        var error = new MemberReadError(1, "/var/log/app.log", "access denied");

        Assert.Equal("app.log: access denied", SearchMessages.Problem(error));
    }

    [Fact]
    public void A_failed_search_says_so_with_the_exception_message()
    {
        Assert.Equal("Search failed: disk gone", SearchMessages.Failed("disk gone"));
    }

    [Fact]
    public void The_first_report_passes_at_once_and_reports_within_the_interval_are_dropped()
    {
        var clock = new FakeClock();
        var passed = new List<long>();
        var progress = new ScanProgress(passed.Add, intervalMs: 100, clock.Now);

        progress.Report(1);
        clock.Ms = 50;
        progress.Report(2);
        clock.Ms = 99;
        progress.Report(3);

        Assert.Equal([1L], passed);
    }

    [Fact]
    public void A_report_after_the_interval_passes_and_restarts_the_interval()
    {
        var clock = new FakeClock();
        var passed = new List<long>();
        var progress = new ScanProgress(passed.Add, intervalMs: 100, clock.Now);

        progress.Report(1);
        clock.Ms = 100;
        progress.Report(2);
        clock.Ms = 150;
        progress.Report(3);
        clock.Ms = 200;
        progress.Report(4);

        Assert.Equal([1L, 2L, 4L], passed);
    }

    private static EventRow Row(DateTimeOffset? time, string? timeText) => new(
        new RowKey(1, 0),
        new EventExtent(0, 0, 0, Provisional: false, Orphan: false),
        time,
        timeText,
        LogLevel.Unknown,
        null,
        "preview",
        IsRaw: false,
        Orphan: false,
        Provisional: false);

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class FakeClock
    {
        public long Ms { get; set; }

        public Func<long> Now => () => Ms;
    }
}
