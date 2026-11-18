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
    public void A_source_that_is_too_long_keeps_its_end_behind_an_ellipsis()
    {
        const string source = "SheetKraft.Core.Services.Ingest.Pipeline.Worker";

        TimelinePage page = TimelineLayout.Compose([Row(source: source)], 60, Utc);

        string text = page.Lines[0].Source;
        Assert.Equal(page.Columns.Source, TextWidth.Columns(text));
        Assert.StartsWith("…", text);
        Assert.EndsWith("Pipeline.Worker", text.TrimEnd());
    }

    [Fact]
    public void A_source_that_fits_is_padded_and_a_missing_source_is_empty()
    {
        TimelinePage page = TimelineLayout.Compose([Row(source: "Log"), Row(source: null)], 80, Utc);

        Assert.Equal(page.Columns.Source, page.Lines[0].Source.Length);
        Assert.Equal("Log", page.Lines[0].Source.TrimEnd());
        Assert.Equal(new string(' ', page.Columns.Source), page.Lines[1].Source);
    }

    [Theory]
    [InlineData(2, 100, 6)]
    [InlineData(30, 100, 30)]
    [InlineData(60, 100, 30)]
    [InlineData(60, 200, 40)]
    [InlineData(60, 60, 18)]
    public void The_source_width_is_clamped_to_six_and_the_smaller_of_forty_and_thirty_percent(int sourceLength, int width, int expected)
    {
        string source = new('x', sourceLength);

        TimelinePage page = TimelineLayout.Compose([Row(source: source), Row(source: "y")], width, Utc);

        Assert.Equal(expected, page.Columns.Source);
    }

    [Fact]
    public void Wide_characters_fit_the_message_column_exactly()
    {
        const string wide = "日本語のログメッセージは長いので切り詰められます";

        TimelinePage page = TimelineLayout.Compose([Row(preview: wide), Row(preview: "日本語")], 50, Utc);

        int width = page.Columns.Message;
        Assert.Equal(24, width);
        Assert.Equal(width, TextWidth.Columns(page.Lines[0].Message));
        Assert.Equal(width, TextWidth.Columns(page.Lines[1].Message));
        Assert.EndsWith("…", page.Lines[0].Message.TrimEnd());
        Assert.Equal("日本語", page.Lines[1].Message.TrimEnd());
    }

    [Fact]
    public void Every_visible_line_is_exactly_the_width_of_the_page()
    {
        TimelinePage page = TimelineLayout.Compose(
            [Row(time: Morning, level: LogLevel.Warn, source: "A.B.C", preview: "long message 日本語 text")],
            70,
            Utc);

        TimelineColumns c = page.Columns;
        int total = c.Time + c.Level + c.Source + c.Message + ((c.Time > 0 ? 1 : 0) + (c.Level > 0 ? 1 : 0) + (c.Source > 0 ? 1 : 0));
        Assert.Equal(70, total);
        TimelineLine line = page.Lines[0];
        Assert.Equal(c.Message, TextWidth.Columns(line.Message));
    }

    [Fact]
    public void A_page_of_full_width_uses_every_column()
    {
        TimelinePage page = TimelineLayout.Compose([Row(time: Morning, source: "Log")], 50, Utc);

        Assert.Equal(TimelineLayout.TimeWidth, page.Columns.Time);
        Assert.Equal(TimelineLayout.LevelWidth, page.Columns.Level);
        Assert.Equal(6, page.Columns.Source);
        Assert.Equal(24, page.Columns.Message);
    }

    [Fact]
    public void A_narrow_page_drops_the_source_first()
    {
        TimelinePage page = TimelineLayout.Compose([Row(time: Morning, source: "Log", preview: "message")], 40, Utc);

        Assert.Equal(TimelineLayout.TimeWidth, page.Columns.Time);
        Assert.Equal(TimelineLayout.LevelWidth, page.Columns.Level);
        Assert.Equal(0, page.Columns.Source);
        Assert.Equal(21, page.Columns.Message);
    }

    [Fact]
    public void A_narrower_page_then_drops_the_level()
    {
        TimelinePage page = TimelineLayout.Compose([Row(time: Morning, level: LogLevel.Info, source: "Log")], 36, Utc);

        Assert.Equal(TimelineLayout.TimeWidth, page.Columns.Time);
        Assert.Equal(0, page.Columns.Level);
        Assert.Equal(0, page.Columns.Source);
        Assert.Equal(23, page.Columns.Message);
    }

    [Fact]
    public void Below_thirty_three_columns_only_the_message_remains()
    {
        TimelinePage page = TimelineLayout.Compose([Row(time: Morning, level: LogLevel.Info, source: "Log", preview: "message")], 30, Utc);

        Assert.Equal(new TimelineColumns(0, 0, 0, 30), page.Columns);
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
        Assert.Equal("Source", header.Source.TrimEnd());
        Assert.Equal("Message", header.Message.TrimEnd());
        Assert.Equal(LogLevel.Unknown, header.Severity);
    }

    [Fact]
    public void An_empty_page_has_columns_and_no_lines()
    {
        TimelinePage page = TimelineLayout.Compose([], 80, Utc);

        Assert.Empty(page.Lines);
        Assert.Null(page.PageDate);
        Assert.Equal(TimelineLayout.MinSourceWidth, page.Columns.Source);
    }

    [Fact]
    public void A_width_that_is_not_positive_gives_no_columns_and_no_lines()
    {
        TimelinePage page = TimelineLayout.Compose([Row(time: Morning)], 0, Utc);

        Assert.Equal(new TimelineColumns(0, 0, 0, 0), page.Columns);
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

    private static EventRow Row(
        DateTimeOffset? time = null,
        string? timeText = null,
        LogLevel level = LogLevel.Unknown,
        string? levelText = null,
        string? source = null,
        string preview = "message",
        bool raw = false) => new(
            new RowKey(1, 0),
            new EventExtent(0, 0, 0, false, false),
            time,
            timeText,
            level,
            levelText,
            source,
            preview,
            raw,
            false,
            false);
}
