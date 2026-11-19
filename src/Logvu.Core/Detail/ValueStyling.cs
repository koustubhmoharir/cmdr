using System.Text.RegularExpressions;

namespace Logvu.Core.Detail;

/// <summary>
/// The spans of the values a detail shows: text-event field values, message lines, and JSON string values. A value takes a base style over
/// all of its text, overlaid by sub-ranges (timestamp parts, exception type names and links). Pure: no views involved.
/// </summary>
/// <remarks>
/// Spans are ordered, non-overlapping and relative to the start of the value's text. An overlay that overlaps one before it in the list
/// yields to it, so exception type names win over links, and timestamp parts over both.
/// </remarks>
internal static partial class ValueStyling
{
    // A character of a path: anything but whitespace, quotes, angle brackets, pipes, wildcards, or a comma or semicolon followed by a space.
    private const string PathChar = @"(?:[^\s""<>|*?,;]|[,;](?!\s))";

    // Characters that end a link when they are its last character. A closing parenthesis is handled separately: it ends a link only when
    // the link does not hold a matching opening one.
    private const string TrailingPunctuation = ".,;:!?]}'\\";

    /// <summary>
    /// A link: a URL (<c>scheme://</c> and anything up to a space, quote or angle bracket), a Windows path (<c>X:\</c> or <c>X:/</c>), a UNC
    /// path (<c>\\server\share</c>), an absolute Unix-like path with at least two segments whose first segment starts with a letter, <c>.</c>,
    /// <c>_</c> or <c>~</c>, or a <c>~/</c> path. The backslashes of a JSON string's escaped text match as separators.
    /// </summary>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9+.\-])[A-Za-z][A-Za-z0-9+.\-]*://[^\s""'<>]+"
        + @"|(?<![A-Za-z0-9_])[A-Za-z]:[\\/]" + PathChar + "*"
        + @"|(?<![\\\w])\\{2,}[A-Za-z0-9._$\-]+\\+[A-Za-z0-9._$\-]" + PathChar + "*"
        + @"|(?<![\w/.:])/[A-Za-z._~][^\s/""<>|*?,;]*/" + PathChar + "*"
        + @"|(?<![\w/.:~])~/" + PathChar + "+",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex LinkPattern();

    /// <summary>
    /// A dotted identifier whose last segment ends in <c>Exception</c>, as a whole word: <c>System.IO.IOException</c>, but not
    /// <c>Exception</c>, <c>MyException</c> or <c>System.ExceptionHandler</c>.
    /// </summary>
    [GeneratedRegex(
        @"(?<![\w.])[A-Za-z_][A-Za-z0-9_`]*(?:\.[A-Za-z_][A-Za-z0-9_`]*)*\.[A-Za-z_][A-Za-z0-9_]*Exception(?![\w])",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex ExceptionTypePattern();

    /// <summary>
    /// An ISO 8601 or RFC 3339 timestamp, anchored to the whole value: a date, a <c>T</c> or space, a time to the minute or second, an
    /// optional fraction with a dot or comma, and an optional offset.
    /// </summary>
    [GeneratedRegex(
        @"^(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})(?<sep>[T ])(?<time>[0-9]{2}:[0-9]{2}(?::[0-9]{2})?)(?<fraction>[.,][0-9]+)?(?<offset>[Zz]|[+-][0-9]{2}(?::?[0-9]{2})?)?$",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex TimestampPattern();

    /// <summary>A number with ASCII digits: an optional minus, an optional fraction and an optional exponent.</summary>
    [GeneratedRegex(@"^-?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex NumberPattern();

    /// <summary>
    /// The spans of a text-event field value. A complete timestamp is drawn in parts, a number in <see cref="SpanStyle.JsonNumber"/>, and
    /// anything else in <see cref="SpanStyle.JsonString"/>, with its links in <see cref="SpanStyle.Link"/>. Empty values have no spans.
    /// </summary>
    /// <param name="value">The value as written.</param>
    public static List<StyledSpan> Field(string value)
    {
        if (value.Length == 0)
        {
            return [];
        }

        if (TimestampParts(value) is { } parts)
        {
            return Layer(value.Length, null, parts);
        }

        if (NumberPattern().IsMatch(value))
        {
            return Whole(value, SpanStyle.JsonNumber);
        }

        return Layer(value.Length, SpanStyle.JsonString, Links(value));
    }

    /// <summary>
    /// The spans of a JSON string value's raw text, quotes included. The text is <see cref="SpanStyle.JsonString"/> with its links in
    /// <see cref="SpanStyle.Link"/>. A complete timestamp, whose raw text has no escapes, is drawn in parts between its quotes.
    /// </summary>
    /// <param name="raw">The value's text as written, quotes included. This is the text shown.</param>
    /// <param name="text">The decoded value.</param>
    public static List<StyledSpan> JsonString(string raw, string text)
    {
        var overlays = new List<StyledSpan>();
        if (raw == "\"" + text + "\"" && TimestampParts(text) is { } parts)
        {
            overlays.Add(new StyledSpan(0, 1, SpanStyle.Punctuation));
            overlays.AddRange(parts.Select(part => part with { Start = part.Start + 1 }));
            overlays.Add(new StyledSpan(raw.Length - 1, 1, SpanStyle.Punctuation));
        }

        overlays.AddRange(Links(raw));
        return Layer(raw.Length, SpanStyle.JsonString, overlays);
    }

    /// <summary>
    /// The spans of a message line that the stack analysis left as plain text. A text event's line is <see cref="SpanStyle.JsonString"/>, with
    /// its exception type names in <see cref="SpanStyle.ExceptionType"/>; any other message line has no base style. Both take links.
    /// </summary>
    /// <param name="line">The line's text.</param>
    /// <param name="textEvent">Whether the line is a message of a text event or a raw event.</param>
    public static List<StyledSpan> Message(string line, bool textEvent)
    {
        var overlays = new List<StyledSpan>();
        if (textEvent)
        {
            overlays.AddRange(ExceptionTypes(line));
        }

        overlays.AddRange(Links(line));
        return Layer(line.Length, textEvent ? SpanStyle.JsonString : null, overlays);
    }

    /// <summary>
    /// The analyzed lines of a message, with the spans of every <see cref="StackLineKind.Other"/> line replaced by <see cref="Message"/>'s.
    /// Lines the analysis styles keep their spans.
    /// </summary>
    /// <param name="analysis">The analyzed lines.</param>
    /// <param name="textEvent">Whether the message belongs to a text event or a raw event.</param>
    public static IReadOnlyList<StackLine> StyleOtherLines(IReadOnlyList<StackLine> analysis, bool textEvent)
    {
        var result = new StackLine[analysis.Count];
        for (int i = 0; i < analysis.Count; i++)
        {
            StackLine line = analysis[i];
            result[i] = line.Kind == StackLineKind.Other ? line with { Spans = Message(line.Text, textEvent) } : line;
        }

        return result;
    }

    /// <summary>The spans of a value whose whole text is one style: none when the text is empty.</summary>
    public static List<StyledSpan> Whole(string text, SpanStyle style) =>
        text.Length == 0 ? [] : [new StyledSpan(0, text.Length, style)];

    /// <summary>
    /// Lays out a text: <paramref name="baseStyle"/> over every part no overlay covers, and the overlays where they are. An overlay yields to
    /// the overlays before it in the list where they overlap. The result is sorted by start.
    /// </summary>
    private static List<StyledSpan> Layer(int length, SpanStyle? baseStyle, IReadOnlyList<StyledSpan> overlays)
    {
        var claimed = new List<StyledSpan>();
        foreach (StyledSpan overlay in overlays)
        {
            if (overlay.Length <= 0)
            {
                continue;
            }

            foreach ((int start, int end) in Uncovered(overlay.Start, overlay.Start + overlay.Length, claimed).ToList())
            {
                claimed.Add(overlay with { Start = start, Length = end - start });
            }
        }

        if (baseStyle is { } style)
        {
            foreach ((int start, int end) in Uncovered(0, length, claimed).ToList())
            {
                claimed.Add(new StyledSpan(start, end - start, style));
            }
        }

        claimed.Sort((a, b) => a.Start.CompareTo(b.Start));
        return claimed;
    }

    /// <summary>The parts of <c>[start, end)</c> that no covering span covers, in order.</summary>
    private static IEnumerable<(int Start, int End)> Uncovered(int start, int end, List<StyledSpan> covering)
    {
        int cursor = start;
        foreach (StyledSpan cover in covering.OrderBy(c => c.Start))
        {
            int coverStart = cover.Start;
            int coverEnd = cover.Start + cover.Length;
            if (coverEnd <= cursor || coverStart >= end)
            {
                continue;
            }

            if (coverStart > cursor)
            {
                yield return (cursor, coverStart);
            }

            cursor = Math.Max(cursor, coverEnd);
        }

        if (cursor < end)
        {
            yield return (cursor, end);
        }
    }

    /// <summary>The timestamp parts of a text, relative to its start, or null when it is not a complete timestamp.</summary>
    private static List<StyledSpan>? TimestampParts(string text)
    {
        Match match = TimestampPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var spans = new List<StyledSpan>();
        Add(match.Groups["date"], SpanStyle.Date);
        Add(match.Groups["sep"], SpanStyle.Muted);
        Add(match.Groups["time"], SpanStyle.Time);
        Add(match.Groups["fraction"], SpanStyle.Muted);
        Add(match.Groups["offset"], SpanStyle.Muted);
        return spans;

        void Add(Group group, SpanStyle style)
        {
            if (group.Success)
            {
                spans.Add(new StyledSpan(group.Index, group.Length, style));
            }
        }
    }

    /// <summary>The link spans of a text, with their trailing punctuation left out. A text whose matching times out has none.</summary>
    private static List<StyledSpan> Links(string text)
    {
        var spans = new List<StyledSpan>();
        try
        {
            foreach (Match match in LinkPattern().Matches(text))
            {
                spans.Add(new StyledSpan(match.Index, TrimmedLength(text, match.Index, match.Length), SpanStyle.Link));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }

        return spans;
    }

    /// <summary>The exception type spans of a text. A text whose matching times out has none.</summary>
    private static List<StyledSpan> ExceptionTypes(string text)
    {
        var spans = new List<StyledSpan>();
        try
        {
            foreach (Match match in ExceptionTypePattern().Matches(text))
            {
                spans.Add(new StyledSpan(match.Index, match.Length, SpanStyle.ExceptionType));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }

        return spans;
    }

    /// <summary>
    /// The length of a link without its trailing punctuation. A closing parenthesis stays when the link has a matching opening one. The
    /// trailing backslash of an escaped quote in JSON text is dropped too.
    /// </summary>
    private static int TrimmedLength(string text, int start, int length)
    {
        int end = start + length;
        while (end > start)
        {
            char last = text[end - 1];
            bool trim = last == ')'
                ? CountOf(text, start, end, ')') > CountOf(text, start, end, '(')
                : TrailingPunctuation.Contains(last);
            if (!trim)
            {
                break;
            }

            end--;
        }

        return end - start;
    }

    private static int CountOf(string text, int start, int end, char c)
    {
        int count = 0;
        for (int i = start; i < end; i++)
        {
            if (text[i] == c)
            {
                count++;
            }
        }

        return count;
    }
}
