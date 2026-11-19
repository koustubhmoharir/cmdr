using System.Globalization;
using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Store;
using Shared.Tui;

namespace Logvu.Tui;

/// <summary>The widths of the timeline's columns, in terminal columns. A hidden column has width 0.</summary>
/// <param name="Time">The time column.</param>
/// <param name="Level">The level column.</param>
/// <param name="Message">The message column, which takes the remaining width.</param>
/// <param name="Extra">
/// The widths of the extra columns, one per configured column in configuration order, each 0 when hidden. Empty when no columns are
/// configured.
/// </param>
internal sealed record TimelineColumns(int Time, int Level, int Message, IReadOnlyList<int> Extra)
{
    /// <summary>Creates the columns of a timeline with no extra columns.</summary>
    public TimelineColumns(int time, int level, int message)
        : this(time, level, message, Array.Empty<int>())
    {
    }
}

/// <summary>
/// One line of the timeline: the text of each column, fitted to its width. The text of a hidden column is empty.
/// </summary>
/// <param name="Time">The time cell.</param>
/// <param name="LevelLabel">The level label, five columns wide.</param>
/// <param name="Message">The message cell, with the raw marker when the row is raw.</param>
/// <param name="Severity">The row's normalized level, which selects its colors. Unknown for the header.</param>
/// <param name="Extra">The cells of the extra columns, in configuration order. The header's cells are the column titles.</param>
internal sealed record TimelineLine(string Time, string LevelLabel, string Message, LogLevel Severity, IReadOnlyList<string> Extra);

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
/// <b>Extra columns.</b> Each configured column comes after Level and before Message, in configuration order. Its width is the widest
/// of its title and of its values on the page, at least 1 and at most its <see cref="ColumnSpec.MaxWidth"/>, and never less than its
/// floor. A value too long for the column is cut at its end, as <see cref="Cell"/> cuts it, or for a column with
/// <see cref="ColumnSpec.KeepEnd"/> set, keeps its end behind a leading <c>…</c>, as <see cref="KeepEnd"/> does. A value that is missing,
/// a raw row's and a row that has no value at a column's position show blank.
/// </para>
/// <para>
/// <b>Message.</b> The rest of the width. A raw row (one that could not be parsed) starts with the marker <c>⚠ </c>. The marker's
/// width is measured by <see cref="TextWidth"/> like any other text.
/// </para>
/// <para>
/// <b>Narrow widths.</b> The message keeps at least <see cref="MinMessageWidth"/> columns when it can. The optional columns are dropped
/// in this order until it does: the extra columns, the rightmost first, then Level, then Time. With no extra columns and the time-only
/// format, Time, Level and the message together need <see cref="TimeWidth"/> + 1 + <see cref="LevelWidth"/> + 1 +
/// <see cref="MinMessageWidth"/>, which is 39 columns: Level is dropped below 39. Time and the message need 12 + 1 + 20, which is 33
/// columns: Time is dropped below 33, and below 33 only the message remains. The date format is <see cref="DateTimeWidth"/> columns,
/// 11 more than the time-only format, so the thresholds are 50 and 44 for it. Each extra column costs its width and one space, so
/// with extra columns the thresholds are higher. Each column is separated from the next by one space.
/// </para>
/// </remarks>
internal static class TimelineLayout
{
    /// <summary>The width of the level label.</summary>
    public const int LevelWidth = 5;

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

    /// <summary>
    /// The column headings: Time, Level, the titles of the extra columns and Message, fitted to the columns.
    /// </summary>
    /// <param name="columns">The columns of the page.</param>
    /// <param name="specs">The configured extra columns, whose titles are the headings of the extra columns. Null or empty for none.</param>
    public static TimelineLine Header(TimelineColumns columns, IReadOnlyList<ColumnSpec>? specs = null)
    {
        IReadOnlyList<string> extra = columns.Extra.Count == 0
            ? Array.Empty<string>()
            : columns.Extra.Select((width, i) => Cell(specs is not null && i < specs.Count ? specs[i].Title : string.Empty, width)).ToArray();
        return new TimelineLine(
            Cell("Time", columns.Time),
            Cell("Level", columns.Level),
            Cell("Message", columns.Message),
            LogLevel.Unknown,
            extra);
    }

    /// <summary>
    /// Composes the page for the rows at a width. The rows are in the order given, which is the order of the viewport.
    /// </summary>
    /// <param name="rows">The visible rows, top first.</param>
    /// <param name="width">The width of the timeline, in columns. A width that is not positive gives no columns and no lines.</param>
    /// <param name="zone">The time zone that times are shown in.</param>
    /// <param name="specs">
    /// The configured extra columns, in configuration order. Null or empty for none. The page's <see cref="TimelineColumns.Extra"/> has
    /// one width per spec.
    /// </param>
    /// <param name="extraFloors">
    /// The narrowest each extra column may be (still within its cap), by position in <paramref name="specs"/>. A missing entry is 0. The view
    /// passes the widest each column has shown at this width, so that the columns do not jump as rows with shorter values scroll into view.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> or <paramref name="zone"/> is <c>null</c>.</exception>
    public static TimelinePage Compose(
        IReadOnlyList<EventRow> rows,
        int width,
        TimeZoneInfo zone,
        IReadOnlyList<ColumnSpec>? specs = null,
        IReadOnlyList<int>? extraFloors = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(zone);

        IReadOnlyList<ColumnSpec> columnSpecs = specs ?? Array.Empty<ColumnSpec>();
        if (width <= 0)
        {
            return new TimelinePage(new TimelineColumns(0, 0, 0, Hidden(columnSpecs.Count)), [], null);
        }

        (bool spansDates, DateOnly? day) = Dates(rows, zone);
        int timeWidth = spansDates ? DateTimeWidth : TimeWidth;
        string timeFormat = spansDates ? DateTimeFormat : TimeFormat;
        IReadOnlyList<int> extraWidths = ExtraWidths(rows, columnSpecs, extraFloors);

        TimelineColumns columns = ChooseColumns(width, timeWidth, extraWidths);
        IReadOnlyList<TimelineLine> lines = rows.Select(row => Line(row, columns, columnSpecs, timeFormat, zone)).ToList();
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

    /// <summary>
    /// The widths of the extra columns: the widest of each title and its values on the page, at least <see cref="ColumnSpec.MinWidth"/>,
    /// no less than its floor, and at most its <see cref="ColumnSpec.MaxWidth"/>.
    /// </summary>
    private static IReadOnlyList<int> ExtraWidths(IReadOnlyList<EventRow> rows, IReadOnlyList<ColumnSpec> specs, IReadOnlyList<int>? floors)
    {
        if (specs.Count == 0)
        {
            return Array.Empty<int>();
        }

        var widths = new int[specs.Count];
        for (int i = 0; i < widths.Length; i++)
        {
            int widest = TextWidth.Columns(specs[i].Title);
            foreach (EventRow row in rows)
            {
                if (ColumnValue(row, i) is { } value)
                {
                    widest = Math.Max(widest, TextWidth.Columns(value));
                }
            }

            int floor = floors is not null && i < floors.Count ? floors[i] : 0;
            widths[i] = Math.Clamp(Math.Max(widest, floor), ColumnSpec.MinWidth, specs[i].MaxWidth);
        }

        return widths;
    }

    /// <summary>
    /// Chooses the columns for a width, dropping optional columns until the message has its minimum width. The extra columns go first,
    /// the rightmost first, then Level and Time.
    /// </summary>
    private static TimelineColumns ChooseColumns(int width, int timeWidth, IReadOnlyList<int> extraWidths)
    {
        bool showTime = true;
        bool showLevel = true;
        int extraCount = extraWidths.Count;
        while (true)
        {
            int used = (showTime ? timeWidth + Separator : 0)
                + (showLevel ? LevelWidth + Separator : 0)
                + extraWidths.Take(extraCount).Sum(extra => extra + Separator);
            int messageWidth = width - used;
            if (messageWidth >= MinMessageWidth || (extraCount == 0 && !(showTime || showLevel)))
            {
                return new TimelineColumns(
                    showTime ? timeWidth : 0,
                    showLevel ? LevelWidth : 0,
                    messageWidth,
                    ShownExtraWidths(extraWidths, extraCount));
            }

            if (extraCount > 0)
            {
                extraCount--;
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

    /// <summary>The widths of the extra columns, with the ones past <paramref name="shown"/> hidden at 0.</summary>
    private static IReadOnlyList<int> ShownExtraWidths(IReadOnlyList<int> widths, int shown)
    {
        if (widths.Count == 0)
        {
            return Array.Empty<int>();
        }

        return widths.Select((width, index) => index < shown ? width : 0).ToArray();
    }

    /// <summary>The list of <paramref name="count"/> hidden extra columns: empty for none.</summary>
    private static IReadOnlyList<int> Hidden(int count) => count == 0 ? Array.Empty<int>() : new int[count];

    /// <summary>
    /// The value of an extra column for a row: null when the row is raw, or when it has no value at that position, which includes a
    /// row whose list is shorter than the configured columns.
    /// </summary>
    private static string? ColumnValue(EventRow row, int index) =>
        !row.IsRaw && index < row.Columns.Count ? row.Columns[index] : null;

    private static TimelineLine Line(EventRow row, TimelineColumns columns, IReadOnlyList<ColumnSpec> specs, string timeFormat, TimeZoneInfo zone)
    {
        string time = columns.Time > 0 ? TimeCell(row, columns.Time, timeFormat, zone) : string.Empty;
        string level = columns.Level > 0 ? LevelLabel(row) : string.Empty;
        string message = columns.Message > 0 ? Cell((row.IsRaw ? RawMarker : string.Empty) + row.Preview, columns.Message) : string.Empty;
        IReadOnlyList<string> extra = columns.Extra.Count == 0
            ? Array.Empty<string>()
            : columns.Extra.Select((width, i) => width > 0 ? ExtraCell(ColumnValue(row, i) ?? string.Empty, width, specs[i].KeepEnd) : string.Empty)
                .ToArray();
        return new TimelineLine(time, level, message, row.Level, extra);
    }

    /// <summary>The cell of an extra column: its value fitted to the width, with its end cut, or with its start cut for a column that keeps its end.</summary>
    private static string ExtraCell(string value, int width, bool keepEnd) => keepEnd ? KeepEnd(value, width) : Cell(value, width);

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
