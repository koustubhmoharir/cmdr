using System.Globalization;
using System.Text.RegularExpressions;

namespace Logvu.Core.Parsing;

/// <summary>
/// A line-oriented scheme. An event starts at a line matching the header pattern and continues over every following
/// line that does not match, blank lines included.
/// </summary>
/// <remarks>
/// <para>
/// The header pattern is matched against the whole line, so it need not carry its own anchors. Named groups have these
/// meanings: <c>time</c> is parsed with the time formats and becomes <see cref="LogEvent.Time"/>; <c>level</c> is
/// normalized; <c>logger</c> becomes <see cref="LogEvent.Source"/>; <c>message</c> is the start of the message, which
/// is required. Every named group, <c>message</c> excepted, becomes a <see cref="LogField"/> in pattern order.
/// </para>
/// <para>
/// Matching runs with a 250 ms timeout. A header match that times out counts as no match.
/// </para>
/// </remarks>
public sealed class TextScheme : ILogScheme
{
    /// <summary>The longest time a header match may take before it is treated as no match.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private readonly Regex _header;
    private readonly string[] _timeFormats;
    private readonly string[] _fieldNames;
    private readonly int[] _fieldGroups;
    private readonly int _messageGroup;
    private readonly int _timeGroup;
    private readonly int _levelGroup;
    private readonly int _loggerGroup;

    /// <summary>Creates a text scheme.</summary>
    /// <param name="name">The scheme name.</param>
    /// <param name="headerPattern">The header regular expression, with a named group <c>message</c>.</param>
    /// <param name="timeFormats">The exact formats the <c>time</c> group is parsed with, read as local times.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is blank, <paramref name="headerPattern"/> is empty or not a valid regular expression, it
    /// has no <c>message</c> group, or a time format is blank.
    /// </exception>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public TextScheme(string name, string headerPattern, IReadOnlyList<string> timeFormats)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrEmpty(headerPattern);
        ArgumentNullException.ThrowIfNull(timeFormats);

        Regex raw;
        try
        {
            raw = new Regex(headerPattern, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"Header pattern of scheme '{name}' is not a valid regular expression: {ex.Message}", nameof(headerPattern), ex);
        }

        if (!raw.GetGroupNames().Contains("message"))
        {
            throw new ArgumentException($"Header pattern of scheme '{name}' has no named group 'message'.", nameof(headerPattern));
        }

        // A pattern that compiles on its own has balanced parentheses, so wrapping it cannot change its meaning.
        // Compiled: it runs on every line of every scan.
        _header = new Regex(@"\A(?:" + headerPattern + @")\z", RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);

        _fieldNames = raw.GetGroupNames()
            .Where(group => !char.IsAsciiDigit(group[0]) && group != "message")
            .OrderBy(group => raw.GroupNumberFromName(group))
            .ToArray();

        // The wrapper adds no capture, so the group numbers of the compiled header are those of the pattern.
        _fieldGroups = _fieldNames.Select(name => _header.GroupNumberFromName(name)).ToArray();
        _messageGroup = _header.GroupNumberFromName("message");
        _timeGroup = _header.GroupNumberFromName("time");
        _levelGroup = _header.GroupNumberFromName("level");
        _loggerGroup = _header.GroupNumberFromName("logger");

        var formats = new string[timeFormats.Count];
        for (int i = 0; i < formats.Length; i++)
        {
            formats[i] = timeFormats[i] ?? throw new ArgumentNullException(nameof(timeFormats), "A time format is null.");
            if (string.IsNullOrWhiteSpace(formats[i]))
            {
                throw new ArgumentException($"Scheme '{name}' has a blank time format.", nameof(timeFormats));
            }
        }

        Name = name;
        _timeFormats = formats;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public bool IsJson => false;

    /// <inheritdoc/>
    public bool IsHeader(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return IsHeader(line.AsSpan());
    }

    /// <inheritdoc/>
    public bool IsHeader(ReadOnlySpan<char> line)
    {
        try
        {
            return _header.IsMatch(line);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// When the first line is not a header, the result is a raw event with no parse error: the text is orphan text
    /// before any header, not a malformed record.
    /// </remarks>
    public LogEvent Parse(string eventText, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(eventText);
        ArgumentNullException.ThrowIfNull(context);

        int newline = eventText.IndexOf('\n');
        int firstLength = newline < 0 ? eventText.Length : newline;

        // eventText[newline..] is "\n" plus every continuation line, blank ones included.
        string continuation = newline < 0 ? string.Empty : eventText[newline..];
        return Read(eventText, firstLength, continuation, eventText, context, full: true, withTime: true);
    }

    /// <summary>
    /// Parses the header of an event: its first line alone. The result has the header's level and source as <see cref="Parse"/> gives them
    /// for the same event, its time too when <paramref name="withTime"/> is set, and no message and no fields. Used to decide on the header
    /// before the rest of the event is read.
    /// </summary>
    /// <param name="eventText">The event's text, or any text that begins with its first line.</param>
    /// <param name="context">The parse settings.</param>
    /// <param name="withTime">Whether the time is parsed. It is the one header field that costs a format parse.</param>
    internal LogEvent ParseHeader(string eventText, ParseContext context, bool withTime)
    {
        int newline = eventText.IndexOf('\n');
        int firstLength = newline < 0 ? eventText.Length : newline;
        return Read(eventText, firstLength, string.Empty, eventText, context, full: false, withTime);
    }

    /// <summary>
    /// Reads an event from the first <paramref name="firstLength"/> characters of <paramref name="input"/>, which is its first line, and the
    /// continuation that follows it. <paramref name="whole"/> is the text of a raw result.
    /// </summary>
    /// <param name="full">Whether the message and the fields are built. A header-only read leaves them empty.</param>
    /// <param name="withTime">Whether the time is parsed.</param>
    private LogEvent Read(string input, int firstLength, string continuation, string whole, ParseContext context, bool full, bool withTime)
    {
        // The header is matched within its own line, so the anchors and the end of the match are the line's, as for a copy of the line.
        Match match;
        try
        {
            match = _header.Match(input, 0, firstLength);
        }
        catch (RegexMatchTimeoutException)
        {
            return LogEvent.Raw(whole, "header pattern timed out");
        }

        if (!match.Success)
        {
            return LogEvent.Raw(whole, null);
        }

        string? timeText = withTime ? Captured(match, _timeGroup) : null;
        DateTimeOffset? time = null;
        if (timeText is not null && DateTime.TryParseExact(timeText, _timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            time = TimeParsing.InZone(local, context);
        }

        string? levelText = Captured(match, _levelGroup);
        string message = full ? match.Groups[_messageGroup].Value + continuation : string.Empty;

        var fields = full ? new LogField[_fieldNames.Length] : [];
        for (int i = 0; i < fields.Length; i++)
        {
            Group group = match.Groups[_fieldGroups[i]];
            fields[i] = new LogField(_fieldNames[i], group.Success ? group.Value : string.Empty);
        }

        return new LogEvent
        {
            Time = time,
            TimeText = timeText,
            Level = LogLevels.Normalize(levelText),
            LevelText = levelText,
            Source = Captured(match, _loggerGroup),
            Message = message,
            Fields = fields,
        };
    }

    /// <summary>The value of a group, or <c>null</c> when the pattern has no such group or it did not participate in the match.</summary>
    private static string? Captured(Match match, int group) => group >= 0 && match.Groups[group] is { Success: true } captured ? captured.Value : null;
}
