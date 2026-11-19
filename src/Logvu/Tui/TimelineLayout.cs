using System.Globalization;
using Logvu.Core.Parsing;
using Logvu.Core.Store;
using Shared.Tui;

namespace Logvu.Tui;

/// <summary>The widths of the timeline's columns, in terminal columns. A hidden column has width 0.</summary>
/// <param name="Time">The time column.</param>
/// <param name="Level">The level column.</param>
/// <param name="Source">The source column.</param>
/// <param name="Message">The message column, which takes the remaining width.</param>
internal sealed record TimelineColumns(int Time, int Level, int Source, int Message);

/// <summary>One line of the timeline: the text of each column, fitted to its width. The text of a hidden column is empty.</summary>
/// <param name="Time">The time cell.</param>
/// <param name="LevelLabel">The level label, five columns wide.</param>
/// <param name="Source">The source cell, its end kept when it is cut.</param>
/// <param name="Message">The message cell, with the raw marker when the row is raw.</param>
/// <param name="Severity">The row's normalized level, which selects its colors. Unknown for the header.</param>
internal sealed record TimelineLine(string Time, string LevelLabel, string Source, string Message, LogLevel Severity);

/// <summary>
/// A composed page: its columns, one line per row in the order given, and the page date. The page date is set when every row
/// shows only its time, so the status line can show the day; it is <c>null</c> when the rows cross a day or have no time.
/// </summary>
/// <param name="Columns">The columns the lines were fitted to.</param>
/// <param name="Lines">One line per row, top first.</param>
/// <param name="PageDate">The day of a time-only page, as <c>yyyy-MM-dd</c>, or <c>null</c>.</param>
internal sealed record TimelinePage(TimelineColumns Columns, IReadOnlyList<TimelineLine> Lines, string? PageDate);

/// <summary>
/// Computes the timeline's columns and cell texts for a width. It has no Terminal.Gui dependency.
/// </summary>
/// <remarks>
/// <para>
/// <b>Time.</b> An event's time is converted to the configured time zone. When the rows span more than one calendar date in that
/// zone, every row shows <c>yyyy-MM-dd HH:mm:ss.fff</c> (23 columns). Otherwise the rows show <c>HH:mm:ss.fff</c> (12 columns) and the
/// day is <see cref="TimelinePage.PageDate"/>. A row without a time shows its time text, or <c>-</c>.
/// </para>
/// <para>
/// <b>Level.</b> A five-column label: the normalized level's name, or for an unknown level the first five characters of the level
/// text in upper case, or <c>-</c>.
/// </para>
/// <para>
/// <b>Source.</b> Its width is the widest source of the rows, clamped to 6 columns and to the smaller of 40 columns and 30% of the
/// width. A longer source keeps its end behind a leading <c>…</c>, because logger names are most specific at the end.
/// </para>
/// <para>
/// <b>Message.</b> The rest of the width. A raw row (one that could not be parsed) starts with the marker <c>⚠ </c>. The marker's
/// width is measured by <see cref="TextWidth"/> like any other text.
/// </para>
/// <para>
/// <b>Narrow widths.</b> The message keeps at least 20 columns when it can. The optional columns are dropped in this order until it
/// does: Source, then Level, then Time. The thresholds assume the narrowest source (6 columns) and the time-only format: all four
/// columns need 46 columns, Source is dropped below 46, Level below 39 and Time below 33, and below 33 only the message remains. The
/// date format needs 11 columns more for its time. Each column is separated from the next by one space.
/// </para>
/// </remarks>
internal static class TimelineLayout
{
    /// <summary>The width of the level label.</summary>
    public const int LevelWidth = 5;

    /// <summary>The narrowest source column.</summary>
    public const int MinSourceWidth = 6;

    /// <summary>The widest source column.</summary>
    public const int MaxSourceWidth = 40;

    /// <summary>The message width that the optional columns give way for, when the width allows it.</summary>
    public const int MinMessageWidth = 20;

    /// <summary>The time format of a time-only page.</summary>
    public const string TimeFormat = "HH:mm:ss.fff";

    /// <summary>The time format of a page that crosses days.</summary>
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

    /// <summary>The width of <see cref="TimeFormat"/>.</summary>
    public const int TimeWidth = 12;

    /// <summary>The width of <see cref="DateTimeFormat"/>.</summary>
    public const int DateTimeWidth = 23;

    /// <summary>The text of a missing value.</summary>
    public const string Missing = "-";

    /// <summary>The marker at the start of a raw row's message.</summary>
    public const string RawMarker = "⚠ ";

    private const string DateFormat = "yyyy-MM-dd";
    private const string Ellipsis = "…";
    private const int Separator = 1;

    /// <summary>The column headings: Time, Level, Source and Message, fitted to the columns.</summary>
    /// <param name="columns">The columns of the page.</param>
    public static TimelineLine Header(TimelineColumns columns) => new(
        Cell("Time", columns.Time),
        Cell("Level", columns.Level),
        Cell("Source", columns.Source),
        Cell("Message", columns.Message),
        LogLevel.Unknown);

    /// <summary>
    /// Composes the page for the rows at a width. The rows are in the order given, which is the order of the viewport.
    /// </summary>
    /// <param name="rows">The visible rows, top first.</param>
    /// <param name="width">The width of the timeline, in columns. A width that is not positive gives no columns and no lines.</param>
    /// <param name="zone">The time zone that times are shown in.</param>
    /// <param name="sourceFloor">
    /// The narrowest the source column may be (still within its cap): the view passes the widest source column it has shown at this
    /// width, so that the columns do not jump as rows with shorter sources scroll into view.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> or <paramref name="zone"/> is <c>null</c>.</exception>
    public static TimelinePage Compose(IReadOnlyList<EventRow> rows, int width, TimeZoneInfo zone, int sourceFloor = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(zone);

        if (width <= 0)
        {
            return new TimelinePage(new TimelineColumns(0, 0, 0, 0), [], null);
        }

        (bool spansDates, DateOnly? day) = Dates(rows, zone);
        int timeWidth = spansDates ? DateTimeWidth : TimeWidth;
        string timeFormat = spansDates ? DateTimeFormat : TimeFormat;
        int widestSource = Math.Max(sourceFloor, rows.Select(row => row.Source is null ? 0 : TextWidth.Columns(row.Source)).DefaultIfEmpty(0).Max());

        TimelineColumns columns = ChooseColumns(width, timeWidth, widestSource);
        IReadOnlyList<TimelineLine> lines = rows.Select(row => Line(row, columns, timeFormat, zone)).ToList();
        string? pageDate = spansDates || day is null ? null : day.Value.ToString(DateFormat, CultureInfo.InvariantCulture);
        return new TimelinePage(columns, lines, pageDate);
    }

    /// <summary>
    /// The day that a time-only page shows, as <c>yyyy-MM-dd</c>. It is <c>null</c> when the rows span more than one day in the zone,
    /// or when none of them has a time.
    /// </summary>
    /// <param name="rows">The visible rows.</param>
    /// <param name="zone">The time zone that times are shown in.</param>
    public static string? PageDate(IReadOnlyList<EventRow> rows, TimeZoneInfo zone)
    {
        (bool spansDates, DateOnly? day) = Dates(rows, zone);
        return spansDates || day is null ? null : day.Value.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Shortens text to <paramref name="width"/> columns by dropping its start: a leading <c>…</c> stands for the dropped text. Text
    /// that fits is only padded. Graphemes are never split.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The width to fit in. A width that is not positive gives an empty string.</param>
    public static string KeepEnd(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (width <= 0)
        {
            return string.Empty;
        }

        string clean = TextWidth.Clean(text);
        if (TextWidth.Columns(clean) <= width)
        {
            return TextWidth.Fit(clean, width);
        }

        int used = 0;
        int start = clean.Length;
        var graphemes = TextWidth.Graphemes(clean).ToList();
        for (int i = graphemes.Count - 1; i >= 0; i--)
        {
            (int offset, _, int columns) = graphemes[i];
            if (used + columns > width - 1)
            {
                break;
            }

            used += columns;
            start = offset;
        }

        return TextWidth.Fit(Ellipsis + clean[start..], width);
    }

    /// <summary>Chooses the columns for a width, dropping optional columns until the message has its minimum width.</summary>
    private static TimelineColumns ChooseColumns(int width, int timeWidth, int widestSource)
    {
        bool showTime = true;
        bool showLevel = true;
        bool showSource = true;
        while (true)
        {
            int sourceWidth = showSource ? SourceWidth(widestSource, width) : 0;
            int used = (showTime ? timeWidth + Separator : 0)
                + (showLevel ? LevelWidth + Separator : 0)
                + (showSource ? sourceWidth + Separator : 0);
            int messageWidth = width - used;
            if (messageWidth >= MinMessageWidth || !(showTime || showLevel || showSource))
            {
                return new TimelineColumns(
                    showTime ? timeWidth : 0,
                    showLevel ? LevelWidth : 0,
                    showSource ? sourceWidth : 0,
                    messageWidth);
            }

            if (showSource)
            {
                showSource = false;
            }
            else if (showLevel)
            {
                showLevel = false;
            }
            else
            {
                showTime = false;
            }
        }
    }

    /// <summary>The source column's width: the widest source, clamped to 6 columns and to the smaller of 40 and 30% of the width.</summary>
    private static int SourceWidth(int widest, int width)
    {
        int cap = Math.Max(MinSourceWidth, Math.Min(MaxSourceWidth, width * 3 / 10));
        return Math.Clamp(widest, MinSourceWidth, cap);
    }

    private static TimelineLine Line(EventRow row, TimelineColumns columns, string timeFormat, TimeZoneInfo zone)
    {
        string time = columns.Time > 0 ? TimeCell(row, columns.Time, timeFormat, zone) : string.Empty;
        string level = columns.Level > 0 ? LevelLabel(row) : string.Empty;
        string source = columns.Source > 0 ? KeepEnd(row.Source ?? string.Empty, columns.Source) : string.Empty;
        string message = columns.Message > 0 ? Cell((row.IsRaw ? RawMarker : string.Empty) + row.Preview, columns.Message) : string.Empty;
        return new TimelineLine(time, level, source, message, row.Level);
    }

    private static string TimeCell(EventRow row, int width, string timeFormat, TimeZoneInfo zone)
    {
        if (row.Time is { } time)
        {
            string text = TimeZoneInfo.ConvertTime(time, zone).DateTime.ToString(timeFormat, CultureInfo.InvariantCulture);
            return Cell(text, width);
        }

        return Cell(string.IsNullOrWhiteSpace(row.TimeText) ? Missing : row.TimeText, width);
    }

    private static string LevelLabel(EventRow row)
    {
        if (row.Level != LogLevel.Unknown)
        {
            return Cell(Label(row.Level), LevelWidth);
        }

        string? text = row.LevelText?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return Cell(Missing, LevelWidth);
        }

        string first = text.Length > LevelWidth ? text[..LevelWidth] : text;
        return Cell(first.ToUpperInvariant(), LevelWidth);
    }

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Fatal => "FATAL",
        _ => Missing,
    };

    /// <summary>The first and last of the dates the rows show in the zone, and whether they differ.</summary>
    private static (bool SpansDates, DateOnly? Day) Dates(IReadOnlyList<EventRow> rows, TimeZoneInfo zone)
    {
        DateOnly? first = null;
        foreach (EventRow row in rows)
        {
            if (row.Time is not { } time)
            {
                continue;
            }

            DateOnly day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).DateTime);
            if (first is null)
            {
                first = day;
            }
            else if (first.Value != day)
            {
                return (true, null);
            }
        }

        return (false, first);
    }

    private static string Cell(string text, int width) => TextWidth.Fit(text, width);
}
