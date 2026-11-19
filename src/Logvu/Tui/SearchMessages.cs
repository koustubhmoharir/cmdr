using System.Globalization;
using Logvu.Core.Store;
using Shared.Tui;

namespace Logvu.Tui;

/// <summary>
/// The status texts of the search: the progress of a scan, the outcome of a search, and the problems it reports. Pure.
/// </summary>
internal static class SearchMessages
{
    /// <summary>The text of an invalid pattern, as the bar's status shows it.</summary>
    public const string InvalidPattern = "invalid pattern";

    /// <summary>The text of a search whose pattern timed out.</summary>
    public const string TimedOut = "Search pattern timed out";

    /// <summary>The text of a scan cancelled by Esc.</summary>
    public const string Cancelled = "Search cancelled";

    /// <summary>The text of a detail search that found no further match in the event.</summary>
    public const string NoMatchesInEvent = "No more matches in this event";

    /// <summary>The key hint of the matches of the timeline, while a query is active: <c>n older  N newer</c>.</summary>
    public static Hint TimelineKeys { get; } = KeyHints.Join(KeyHints.Bound('n', "n older"), KeyHints.Bound('N', "N newer"));

    /// <summary>The key hint of the matches of a detail, while a query is active: <c>n next  N prev</c>.</summary>
    public static Hint DetailKeys { get; } = KeyHints.Join(KeyHints.Bound('n', "n next"), KeyHints.Bound('N', "N prev"));

    private const double BytesPerMegabyte = 1024.0 * 1024.0;
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The progress of a scan: <c>Searching… 12.3 MB</c>, the bytes read so far in megabytes.</summary>
    /// <param name="bytes">The bytes scanned.</param>
    public static string Progress(long bytes) =>
        "Searching… " + (bytes / BytesPerMegabyte).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    /// <summary>The outcome of a search that found a match: <c>Match</c> and the match's time in the zone, to the second.</summary>
    /// <param name="row">The row of the match.</param>
    /// <param name="zone">The time zone that times are shown in.</param>
    public static string Match(EventRow row, TimeZoneInfo zone)
    {
        string time = row.Time is { } instant
            ? TimeZoneInfo.ConvertTime(instant, zone).DateTime.ToString(TimeFormat, CultureInfo.InvariantCulture)
            : string.IsNullOrWhiteSpace(row.TimeText) ? "-" : row.TimeText;
        return "Match " + time;
    }

    /// <summary>The outcome of a search that reached the end of history: no more matches above (older) or below (newer).</summary>
    /// <param name="older">Whether the search went toward older events.</param>
    public static string Boundary(bool older) => older ? "No more matches above" : "No more matches below";

    /// <summary>A member that could not be read during a search, as the status line shows a read error.</summary>
    /// <param name="error">The error.</param>
    public static string Problem(MemberReadError error) => $"{Path.GetFileName(error.Path)}: {error.Message}";

    /// <summary>A search that failed with an exception.</summary>
    /// <param name="message">The exception's message.</param>
    public static string Failed(string message) => "Search failed: " + message;
}
