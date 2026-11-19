using System.Globalization;

namespace Logvu.Tui;

/// <summary>
/// The status texts of the filter: the active filter, a scan in progress, a completed scan without matches, a timed-out pattern, and the
/// short notices of the filter keys. Pure.
/// </summary>
internal static class FilterMessages
{
    /// <summary>The notice after <c>F</c> clears the filter.</summary>
    public const string Cleared = "Filter cleared";

    /// <summary>The status of a completed scan that found no event.</summary>
    public const string NoMatches = "No matching events";

    /// <summary>The status when the filter's text pattern timed out, so that it matches nothing from then on.</summary>
    public const string TimedOut = "Filter pattern timed out";

    /// <summary>The status of paused arrivals that have not been seen yet. The count is not shown.</summary>
    public const string NewEvents = "new events";

    /// <summary>
    /// The notice after <c>-</c> leaves no minimum level. While there is a minimum, the active filter's description on the status line
    /// shows it, so a step to a level needs no notice.
    /// </summary>
    public const string AnyLevel = "Min level: any";

    /// <summary>The notice after <c>f</c> in the detail on a line that is not a field.</summary>
    public const string NotAField = "f filters on a field line";

    /// <summary>The most characters of a filter's description on the status line; longer ones end in an ellipsis.</summary>
    public const int MaxDescriptionLength = 48;

    private const double BytesPerMegabyte = 1024.0 * 1024.0;

    /// <summary>The status of an active filter: <c>Filter: </c> and its description, truncated.</summary>
    /// <param name="description">The filter's description.</param>
    public static string Active(string description) => "Filter: " + Truncate(description, MaxDescriptionLength);

    /// <summary>The status of a scan in progress: <c>Scanning… 12.3 MB</c>, the bytes scanned so far.</summary>
    /// <param name="bytes">The bytes scanned.</param>
    public static string Scanning(long bytes) =>
        "Scanning… " + (bytes / BytesPerMegabyte).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    /// <summary>The text cut to <paramref name="max"/> characters, the last of them an ellipsis when it is cut.</summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The most characters, at least 1.</param>
    public static string Truncate(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
