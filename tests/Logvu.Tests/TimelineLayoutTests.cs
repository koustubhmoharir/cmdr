using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Tui;
using Shared.Tui;

namespace Logvu.Tests;

public sealed class TimelineLayoutTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Morning = new(2026, 11, 18, 10, 0, 0, 123, TimeSpan.Zero);

    /// <summary>A logger column that keeps its end. Its title "Logger" is 6 columns, so it is 6 columns wide for a short value.</summary>
    private static readonly ColumnSpec LoggerColumn = new("logger", "Logger", 20, keepEnd: true);

    [Fact]
    public void A_single_day_page_shows_times_only_and_the_page_date()
    {
        var rows = new[]
        {
            Row(time: Morning),
            Row(time: Morning.AddMilliseconds(1333)),
        };

        TimelinePage page = TimelineLayout.Compose(rows, 80, Utc);

        Assert.Equal(TimelineLayout.TimeWidth, page.Columns.Time);
        Assert.Equal("10:00:00.123", page.Lines[0].Time.TrimEnd());
        Assert.Equal("10:00:01.456", page.Lines[1].Time.TrimEnd());
        Assert.Equal("2026-11-18", page.PageDate);
        Assert.Equal("2026-11-18", TimelineLayout.PageDate(rows, Utc));
    }

    [Fact]
    public void A_page_that_crosses_midnight_shows_the_date_on_every_row()
    {
        var rows = new[]
        {
            Row(time: new DateTimeOffset(2026, 11, 18, 23, 59, 59, 500, TimeSpan.Zero)),
            Row(time: new DateTimeOffset(2026, 11, 19, 0, 0, 0, 250, TimeSpan.Zero)),
        };

        TimelinePage page = TimelineLayout.Compose(rows, 80, Utc);

        Assert.Equal(TimelineLayout.DateTimeWidth, page.Columns.Time);
        Assert.Equal("2026-11-18 23:59:59.500", page.Lines[0].Time);
        Assert.Equal("2026-11-19 00:00:00.250", page.Lines[1].Time);
        Assert.Null(page.PageDate);
        Assert.Null(TimelineLayout.PageDate(rows, Utc));
    }

    [Fact]
    public void Times_are_shown_in_the_configured_zone()
    {
        // 10:00 at +05:30 is 04:30 in UTC, on the same day.
        var offset = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.FromHours(5.5));

        TimelinePage page = TimelineLayout.Compose([Row(time: offset)], 80, Utc);

        Assert.Equal("04:30:00.000", page.Lines[0].Time.TrimEnd());
        Assert.Equal("2026-01-02", page.PageDate);
    }

    [Fact]
    public void Calendar_days_are_taken_in_the_configured_zone_not_in_each_offset()
    {
        // 23:30 on the 2nd at -05:00 is 04:30 on the 3rd in UTC. Judged by its own offset the first row is on the 2nd, so the
        // page would cross days; in UTC both rows are on the 3rd and the page is time-only.
        var late = new DateTimeOffset(2026, 1, 2, 23, 30, 0, TimeSpan.FromHours(-5));
        var early = new DateTimeOffset(2026, 1, 3, 4, 0, 0, TimeSpan.Zero);

        TimelinePage page = TimelineLayout.Compose([Row(time: late), Row(time: early)], 80, Utc);

        Assert.Equal(TimelineLayout.TimeWidth, page.Columns.Time);
        Assert.Equal("04:30:00.000", page.Lines[0].Time.TrimEnd());
        Assert.Equal("2026-01-03", page.PageDate);
    }

    [Fact]
    public void A_row_without_a_time_shows_its_time_text_or_a_dash()
    {
        TimelinePage page = TimelineLayout.Compose(
            [Row(timeText: "2026-01-02 garbage"), Row()],
            80,
            Utc);

        Assert.StartsWith("2026-01-02", page.Lines[0].Time);
        Assert.EndsWith("…", page.Lines[0].Time);
        Assert.Equal(TimelineLayout.TimeWidth, TextWidth.Columns(page.Lines[0].Time));
        Assert.Equal("-", page.Lines[1].Time.TrimEnd());
        Assert.Null(page.PageDate);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRACE")]
    [InlineData(LogLevel.Debug, "DEBUG")]
    [InlineData(LogLevel.Info, "INFO")]
    [InlineData(LogLevel.Warn, "WARN")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Fatal, "FATAL")]
    public void Each_level_has_its_five_column_label(LogLevel level, string label)
    {
        TimelinePage page = TimelineLayout.Compose([Row(level: level, levelText: label.ToLowerInvariant())], 80, Utc);

        Assert.Equal(TimelineLayout.LevelWidth, page.Columns.Level);
        Assert.Equal(TimelineLayout.LevelWidth, page.Lines[0].LevelLabel.Length);
        Assert.Equal(label, page.Lines[0].LevelLabel.TrimEnd());
        Assert.Equal(level, page.Lines[0].Severity);
    }

    [Theory]
    [InlineData("custom", "CUSTO")]
    [InlineData("ab", "AB   ")]
    [InlineData("   ", "-    ")]
    [InlineData(null, "-    ")]
    public void An_unknown_level_shows_the_first_five_characters_of_its_text(string? levelText, string expected)
    {
        TimelinePage page = TimelineLayout.Compose([Row(level: LogLevel.Unknown, levelText: levelText)], 80, Utc);

        Assert.Equal(expected, page.Lines[0].LevelLabel);
        Assert.Equal(LogLevel.Unknown, page.Lines[0].Severity);
    }

    [Fact]
    public void A_keep_end_column_that_is_too_long_keeps_its_end_behind_an_ellipsis()
    {
        const string logger = "SheetKraft.Core.Services.Ingest.Pipeline.Worker";

        TimelinePage page = TimelineLayout.Compose([WithColumns(Row(time: Morning), logger)], 100, Utc, specs: [LoggerColumn]);

        string text = page.Lines[0].Extra[0];
        Assert.Equal(20, page.Columns.Extra[0]);
        Assert.Equal(page.Columns.Extra[0], TextWidth.Columns(text));
        Assert.StartsWith("…", text);
        Assert.EndsWith("Pipeline.Worker", text.TrimEnd());
    }

    [Fact]
    public void A_keep_end_column_that_fits_is_padded_and_a_missing_value_is_blank()
    {
        TimelinePage page = TimelineLayout.Compose(
            [WithColumns(Row(time: Morning), "Log"), WithColumns(Row(time: Morning), (string?)null)],
            80,
            Utc,
            specs: [LoggerColumn]);

        Assert.Equal(6, page.Columns.Extra[0]);
        Assert.Equal(page.Columns.Extra[0], page.Lines[0].Extra[0].Length);
        Assert.Equal("Log", page.Lines[0].Extra[0].TrimEnd());
        Assert.Equal(new string(' ', page.Columns.Extra[0]), page.Lines[1].Extra[0]);
    }

    [Theory]
    [InlineData(2, 30, 6)]
    [InlineData(30, 30, 30)]
    [InlineData(60, 40, 40)]
    [InlineData(60, 25, 25)]
    public void A_keep_end_column_is_as_wide_as_its_widest_value_within_its_maximum(int valueLength, int maxWidth, int expected)
    {
        ColumnSpec[] specs = [new ColumnSpec("logger", "Logger", maxWidth, keepEnd: true)];

        TimelinePage page = TimelineLayout.Compose(
            [WithColumns(Row(time: Morning), new string('x', valueLength)), WithColumns(Row(time: Morning), "y")],
            200,
            Utc,
            specs: specs);

        Assert.Equal(expected, page.Columns.Extra[0]);
    }

    [Fact]
    public void A_keep_end_column_is_cut_from_the_front_and_a_normal_column_at_the_end()
    {
        ColumnSpec[] specs = [new ColumnSpec("logger", "Logger", 8, keepEnd: true), new ColumnSpec("method", "Method", 5)];
        EventRow row = WithColumns(Row(time: Morning), "Pipeline.Worker.Final", "abcdefgh");

        TimelinePage page = TimelineLayout.Compose([row], 100, Utc, specs: specs);

        Assert.Equal([8, 5], page.Columns.Extra);
        Assert.Equal("…r.Final", page.Lines[0].Extra[0]);
        Assert.Equal("abcd…", page.Lines[0].Extra[1]);
    }

    [Fact]
    public void Wide_characters_fit_the_message_column_exactly()
    {
        const string wide = "日本語のログメッセージは長いので切り詰められます";

        TimelinePage page = TimelineLayout.Compose([Row(preview: wide), Row(preview: "日本語")], 50, Utc);

        int width = page.Columns.Message;
        Assert.Equal(31, width);
        Assert.Equal(width, TextWidth.Columns(page.Lines[0].Message));
        Assert.Equal(width, TextWidth.Columns(page.Lines[1].Message));
        Assert.EndsWith("…", page.Lines[0].Message.TrimEnd());
        Assert.Equal("日本語", page.Lines[1].Message.TrimEnd());
    }

    [Fact]
    public void Every_visible_line_is_exactly_the_width_of_the_page()
    {
        TimelinePage page = TimelineLayout.Compose(
            [WithColumns(Row(time: Morning, level: LogLevel.Warn, preview: "long message 日本語 text"), "A.B.C")],
            70,
            Utc,
            specs: [LoggerColumn]);

        TimelineColumns c = page.Columns;
        int separators = (c.Time > 0 ? 1 : 0) + (c.Level > 0 ? 1 : 0) + c.Extra.Count(width => width > 0);
        int total = c.Time + c.Level + c.Extra.Sum() + c.Message + separators;
        Assert.Equal(70, total);
        TimelineLine line = page.Lines[0];
        Assert.Equal(c.Extra[0], TextWidth.Columns(line.Extra[0]));
        Assert.Equal(c.Message, TextWidth.Columns(line.Message));
    }

    [Fact]
    public void A_page_of_full_width_uses_every_column()
    {
        TimelinePage page = TimelineLayout.Compose([WithColumns(Row(time: Morning), "Log")], 50, Utc, specs: [LoggerColumn]);

        Assert.Equal(TimelineLayout.TimeWidth, page.Columns.Time);
        Assert.Equal(TimelineLayout.LevelWidth, page.Columns.Level);
        Assert.Equal(6, page.Columns.Extra[0]);
        Assert.Equal(24, page.Columns.Message);
    }

    [Fact]
    public void A_narrow_page_drops_the_extra_column_first()
    {
        TimelinePage page = TimelineLayout.Compose([WithColumns(Row(time: Morning, preview: "message"), "Log")], 40, Utc, specs: [LoggerColumn]);

        Assert.Equal(TimelineLayout.TimeWidth, page.Columns.Time);
        Assert.Equal(TimelineLayout.LevelWidth, page.Columns.Level);
        Assert.Equal([0], page.Columns.Extra);
        Assert.Equal(21, page.Columns.Message);
    }

    [Fact]
    public void A_narrower_page_then_drops_the_level()
    {
        TimelinePage page = TimelineLayout.Compose([WithColumns(Row(time: Morning, level: LogLevel.Info), "Log")], 36, Utc, specs: [LoggerColumn]);

        Assert.Equal(TimelineLayout.TimeWidth, page.Columns.Time);
        Assert.Equal(0, page.Columns.Level);
        Assert.Equal([0], page.Columns.Extra);
        Assert.Equal(23, page.Columns.Message);
    }

    [Fact]
    public void Below_thirty_three_columns_only_the_message_remains()
    {
        TimelinePage page = TimelineLayout.Compose([Row(time: Morning, level: LogLevel.Info, preview: "message")], 30, Utc);

        Assert.Equal(new TimelineColumns(0, 0, 30), page.Columns);
        Assert.Equal("message", page.Lines[0].Message.TrimEnd());
        Assert.Equal(string.Empty, page.Lines[0].Time);
        Assert.Equal(string.Empty, page.Lines[0].LevelLabel);
    }

    [Fact]
    public void A_raw_row_starts_its_message_with_the_warning_marker()
    {
        // The marker is measured like any text. If the terminal measure ever disagreed, the columns would drift.
        Assert.Equal(1, TextWidth.Columns("⚠"));

        TimelinePage page = TimelineLayout.Compose([Row(preview: "bad line", raw: true), Row(preview: "good line")], 80, Utc);

        Assert.Equal("⚠ bad line", page.Lines[0].Message.TrimEnd());
        Assert.Equal("good line", page.Lines[1].Message.TrimEnd());
    }

    [Fact]
    public void The_header_names_the_visible_columns()
    {
        TimelinePage page = TimelineLayout.Compose([Row(time: Morning)], 80, Utc);

        TimelineLine header = TimelineLayout.Header(page.Columns);

        Assert.Equal("Time", header.Time.TrimEnd());
        Assert.Equal("Level", header.LevelLabel);
        Assert.Equal("Message", header.Message.TrimEnd());
        Assert.Equal(LogLevel.Unknown, header.Severity);
        Assert.Empty(header.Extra);
    }

    [Fact]
    public void A_floor_keeps_a_keep_end_column_at_least_that_wide_within_its_maximum()
    {
        ColumnSpec[] specs = [new ColumnSpec("logger", "Logger", 30, keepEnd: true)];

        TimelinePage narrow = TimelineLayout.Compose([WithColumns(Row(time: Morning), "api")], 100, Utc, specs: specs, extraFloors: [20]);
        Assert.Equal(20, narrow.Columns.Extra[0]);

        // The maximum width still applies to the floor.
        TimelinePage capped = TimelineLayout.Compose([WithColumns(Row(time: Morning), "api")], 100, Utc, specs: specs, extraFloors: [60]);
        Assert.Equal(30, capped.Columns.Extra[0]);

        // A wider value than the floor still widens the column.
        TimelinePage wider = TimelineLayout.Compose([WithColumns(Row(time: Morning), new string('s', 25))], 100, Utc, specs: specs, extraFloors: [10]);
        Assert.Equal(25, wider.Columns.Extra[0]);
    }

    [Fact]
    public void An_empty_page_has_columns_and_no_lines()
    {
        TimelinePage page = TimelineLayout.Compose([], 80, Utc);

        Assert.Empty(page.Lines);
        Assert.Null(page.PageDate);
        Assert.Equal(80 - (TimelineLayout.TimeWidth + 1 + TimelineLayout.LevelWidth + 1), page.Columns.Message);
    }

    [Fact]
    public void A_width_that_is_not_positive_gives_no_columns_and_no_lines()
    {
        TimelinePage page = TimelineLayout.Compose([Row(time: Morning)], 0, Utc);

        Assert.Equal(new TimelineColumns(0, 0, 0), page.Columns);
        Assert.Empty(page.Lines);
    }

    [Theory]
    [InlineData("ab", 5, "ab   ")]
    [InlineData("abcdef", 3, "…ef")]
    [InlineData("abcdef", 0, "")]
    public void KeepEnd_fits_text_by_dropping_its_start(string text, int width, string expected)
    {
        Assert.Equal(expected, TimelineLayout.KeepEnd(text, width));
    }

    [Fact]
    public void The_extra_columns_go_between_level_and_message_in_configuration_order()
    {
        ColumnSpec[] specs = [new ColumnSpec("method", "Method", 10), new ColumnSpec("status", "Status", 10)];
        EventRow row = WithColumns(Row(time: Morning, level: LogLevel.Info, preview: "message"), "GET", "200");

        TimelinePage page = TimelineLayout.Compose([row], 80, Utc, specs: specs);

        TimelineColumns c = page.Columns;
        Assert.Equal([6, 6], c.Extra);
        Assert.Equal(80 - (13 + 6 + 7 + 7), c.Message);
        Assert.Equal("GET", page.Lines[0].Extra[0].TrimEnd());
        Assert.Equal("200", page.Lines[0].Extra[1].TrimEnd());
        Assert.StartsWith("message", page.Lines[0].Message);
    }

    [Fact]
    public void The_header_shows_the_titles_of_the_extra_columns()
    {
        ColumnSpec[] specs = [new ColumnSpec("req.method", "Verb", 10), new ColumnSpec("status")];
        TimelinePage page = TimelineLayout.Compose([WithColumns(Row(time: Morning), "GET", "200")], 80, Utc, specs: specs);

        TimelineLine header = TimelineLayout.Header(page.Columns, specs);

        Assert.Equal("Verb", header.Extra[0].TrimEnd());
        Assert.Equal("status", header.Extra[1].TrimEnd());
        Assert.Equal(page.Columns.Extra[0], header.Extra[0].Length);
    }

    [Theory]
    [InlineData("Id", "1234567", 30, 7)]
    [InlineData("Duration", "1", 30, 8)]
    [InlineData("Id", "1234567", 4, 4)]
    [InlineData("VeryLongTitle", "x", 6, 6)]
    public void An_extra_column_is_as_wide_as_its_widest_text_within_its_maximum(string title, string value, int maxWidth, int expected)
    {
        ColumnSpec[] specs = [new ColumnSpec("a", title, maxWidth)];

        TimelinePage page = TimelineLayout.Compose([WithColumns(Row(time: Morning), value), WithColumns(Row(time: Morning), "y")], 100, Utc, specs: specs);

        Assert.Equal(expected, page.Columns.Extra[0]);
        Assert.Equal(expected, page.Lines[0].Extra[0].Length);
        Assert.Equal(expected, TimelineLayout.Header(page.Columns, specs).Extra[0].Length);
    }

    [Fact]
    public void A_value_longer_than_the_column_is_cut_at_its_end()
    {
        ColumnSpec[] specs = [new ColumnSpec("a", "A", 5)];

        TimelinePage page = TimelineLayout.Compose([WithColumns(Row(time: Morning), "abcdefghijkl")], 100, Utc, specs: specs);

        Assert.Equal(5, page.Columns.Extra[0]);
        Assert.Equal("abcd…", page.Lines[0].Extra[0]);
    }

    [Fact]
    public void Missing_raw_and_short_values_are_blank()
    {
        ColumnSpec[] specs = [new ColumnSpec("a", "A", 8), new ColumnSpec("b", "B", 8)];
        EventRow valued = WithColumns(Row(time: Morning), "one", "two");
        EventRow nulls = WithColumns(Row(time: Morning), null, null);
        EventRow raw = WithColumns(Row(time: Morning, preview: "bad", raw: true), "one", "two");
        EventRow shortRow = WithColumns(Row(time: Morning), "only");
        EventRow unset = Row(time: Morning);

        TimelinePage page = TimelineLayout.Compose([valued, nulls, raw, shortRow, unset], 100, Utc, specs: specs);

        Assert.Equal("one", page.Lines[0].Extra[0].TrimEnd());
        Assert.Equal("two", page.Lines[0].Extra[1].TrimEnd());
        // The columns are as wide as their widest text on the page (the title or "one" or "two"), so a blank is that wide.
        string blank0 = new(' ', page.Columns.Extra[0]);
        string blank1 = new(' ', page.Columns.Extra[1]);
        Assert.Equal(blank0, page.Lines[1].Extra[0]);
        Assert.Equal(blank1, page.Lines[1].Extra[1]);
        Assert.Equal(blank0, page.Lines[2].Extra[0]);
        Assert.Equal(blank1, page.Lines[2].Extra[1]);
        Assert.Equal("only", page.Lines[3].Extra[0].TrimEnd());
        Assert.Equal(blank1, page.Lines[3].Extra[1]);
        Assert.Equal(blank0, page.Lines[4].Extra[0]);
        Assert.Equal(blank1, page.Lines[4].Extra[1]);
    }

    [Fact]
    public void A_floor_keeps_an_extra_column_at_its_width_on_a_page_of_narrower_values()
    {
        ColumnSpec[] specs = [new ColumnSpec("a", "A", 10)];

        TimelinePage kept = TimelineLayout.Compose([WithColumns(Row(time: Morning), "x")], 100, Utc, specs: specs, extraFloors: [7]);
        Assert.Equal(7, kept.Columns.Extra[0]);
        Assert.Equal(7, kept.Lines[0].Extra[0].Length);

        // The cap still applies to the floor.
        TimelinePage capped = TimelineLayout.Compose([WithColumns(Row(time: Morning), "x")], 100, Utc, specs: specs, extraFloors: [20]);
        Assert.Equal(10, capped.Columns.Extra[0]);

        // A wider value than the floor still widens the column.
        TimelinePage wider = TimelineLayout.Compose([WithColumns(Row(time: Morning), "abcdef")], 100, Utc, specs: specs, extraFloors: [3]);
        Assert.Equal(6, wider.Columns.Extra[0]);
    }

    [Fact]
    public void A_narrow_page_drops_the_extra_columns_rightmost_first_then_the_level()
    {
        ColumnSpec[] specs = [new ColumnSpec("a", "A", 10), new ColumnSpec("b", "B", 10)];
        EventRow row = WithColumns(Row(time: Morning, level: LogLevel.Info, preview: "message"), new string('a', 10), new string('b', 10));

        // Both extra columns (11 each) with Time and Level (19) leave the message 19 columns, below its minimum: the rightmost goes first.
        TimelinePage sixty = TimelineLayout.Compose([row], 60, Utc, specs: specs);
        Assert.Equal([10, 0], sixty.Columns.Extra);
        Assert.Equal(TimelineLayout.LevelWidth, sixty.Columns.Level);

        // With neither extra column the message has 27 columns: the level stays, as its minimum is kept.
        TimelinePage fortySix = TimelineLayout.Compose([row], 46, Utc, specs: specs);
        Assert.Equal([0, 0], fortySix.Columns.Extra);
        Assert.Equal(TimelineLayout.LevelWidth, fortySix.Columns.Level);
        Assert.Equal(27, fortySix.Columns.Message);

        // Only once the extra columns are gone does the level go.
        TimelinePage thirtyEight = TimelineLayout.Compose([row], 38, Utc, specs: specs);
        Assert.Equal([0, 0], thirtyEight.Columns.Extra);
        Assert.Equal(0, thirtyEight.Columns.Level);
        Assert.Equal(TimelineLayout.TimeWidth, thirtyEight.Columns.Time);
    }

    [Theory]
    [InlineData(60, 12, 5, 41)]
    [InlineData(39, 12, 5, 20)]
    [InlineData(38, 12, 0, 25)]
    [InlineData(33, 12, 0, 20)]
    [InlineData(32, 0, 0, 32)]
    public void Without_extra_columns_the_level_goes_below_39_columns_and_the_time_below_33(int width, int time, int level, int message)
    {
        // Time, level and a 20 column message need 12 + 1 + 5 + 1 + 20 = 39 columns; time and the message need 33.
        TimelinePage page = TimelineLayout.Compose([Row(time: Morning, level: LogLevel.Info)], width, Utc);

        Assert.Equal(time, page.Columns.Time);
        Assert.Equal(level, page.Columns.Level);
        Assert.Equal(message, page.Columns.Message);
    }

    [Theory]
    [InlineData(50, 23, 5, 20)]
    [InlineData(49, 23, 0, 25)]
    [InlineData(44, 23, 0, 20)]
    [InlineData(43, 0, 0, 43)]
    public void With_dates_the_thresholds_are_11_columns_higher(int width, int time, int level, int message)
    {
        // The date format is 23 columns: 23 + 1 + 5 + 1 + 20 = 50 for all three, and 23 + 1 + 20 = 44 for time and the message.
        TimelinePage page = TimelineLayout.Compose([Row(time: Morning), Row(time: Morning.AddDays(1))], width, Utc);

        Assert.Equal(time, page.Columns.Time);
        Assert.Equal(level, page.Columns.Level);
        Assert.Equal(message, page.Columns.Message);
    }

    [Theory]
    [InlineData(46, 6, 5, 12, 20)]
    [InlineData(45, 0, 5, 12, 26)]
    [InlineData(38, 0, 0, 12, 25)]
    [InlineData(32, 0, 0, 0, 32)]
    public void With_a_keep_end_column_of_six_columns_the_extra_column_goes_first_then_the_level_then_the_time(
        int width, int extra, int level, int time, int message)
    {
        // The extra column costs 7 with its separator, so the thresholds with it are 46, 39 and 33 with the level and the time as before.
        TimelinePage page = TimelineLayout.Compose([WithColumns(Row(time: Morning, level: LogLevel.Info), "a")], width, Utc, specs: [LoggerColumn]);

        Assert.Equal(time, page.Columns.Time);
        Assert.Equal(level, page.Columns.Level);
        Assert.Equal([extra], page.Columns.Extra);
        Assert.Equal(message, page.Columns.Message);
    }

    [Fact]
    public void With_no_columns_the_layout_is_the_same_as_without_the_parameter()
    {
        EventRow row = Row(time: Morning, level: LogLevel.Warn, preview: "long message 日本語 text");

        TimelinePage plain = TimelineLayout.Compose([row], 70, Utc);
        TimelinePage empty = TimelineLayout.Compose([row], 70, Utc, specs: []);

        Assert.Equal(plain.Columns, empty.Columns);
        Assert.Empty(plain.Columns.Extra);
        Assert.Equal(plain.Lines, empty.Lines);
        Assert.Empty(TimelineLayout.Header(plain.Columns).Extra);
    }

    private static EventRow WithColumns(EventRow row, params string?[] values) => row with { Columns = values };

    private static EventRow Row(
        DateTimeOffset? time = null,
        string? timeText = null,
        LogLevel level = LogLevel.Unknown,
        string? levelText = null,
        string preview = "message",
        bool raw = false) => new(
            new RowKey(1, 0),
            new EventExtent(0, 0, 0, false, false),
            time,
            timeText,
            level,
            levelText,
            preview,
            raw,
            false,
            false);
}
