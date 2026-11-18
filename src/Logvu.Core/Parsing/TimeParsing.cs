using System.Globalization;
using System.Text.RegularExpressions;

namespace Logvu.Core.Parsing;

/// <summary>Timestamp conversion shared by the schemes.</summary>
internal static partial class TimeParsing
{
    /// <summary>Go's RFC 3339 nanosecond fractions are cut to the 7 digits .NET keeps.</summary>
    [GeneratedRegex(@"(\.[0-9]{7})[0-9]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex OverlongFraction();

    /// <summary>A time of day followed by a UTC designator or an offset at the end of the text.</summary>
    [GeneratedRegex(@"[0-9]:[0-9]{2}(?::[0-9]{2}(?:[.,][0-9]+)?)?\s*(?:[Zz]|[+-][0-9]{2}(?::?[0-9]{2})?)$",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex TrailingOffset();

    /// <summary>
    /// Converts a local time without an offset to an instant in the context zone.
    /// </summary>
    public static DateTimeOffset InZone(DateTime local, ParseContext context) =>
        new(local, context.Zone.GetUtcOffset(local));

    /// <summary>
    /// Parses an RFC 3339 style timestamp. A value with an offset keeps it; a value without one is read in the context
    /// zone. Returns <c>null</c> when the text is not a timestamp.
    /// </summary>
    public static DateTimeOffset? ParseOffsetAware(string text, ParseContext context)
    {
        string value = OverlongFraction().Replace(text.Trim(), "$1");
        if (TrailingOffset().IsMatch(value))
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
                ? parsed
                : null;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local)
            ? InZone(local, context)
            : null;
    }
}
