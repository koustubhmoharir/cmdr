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
        string firstLine = newline < 0 ? eventText : eventText[..newline];

        Match match;
        try
        {
            match = _header.Match(firstLine);
        }
        catch (RegexMatchTimeoutException)
        {
            return LogEvent.Raw(eventText, "header pattern timed out");
        }

        if (!match.Success)
        {
            return LogEvent.Raw(eventText, null);
        }

        // eventText[newline..] is "\n" plus every continuation line, blank ones included.
        string message = match.Groups["message"].Value + (newline < 0 ? string.Empty : eventText[newline..]);

        Group timeGroup = match.Groups["time"];
        string? timeText = timeGroup.Success ? timeGroup.Value : null;
        DateTimeOffset? time = null;
        if (timeText is not null && DateTime.TryParseExact(timeText, _timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            time = TimeParsing.InZone(local, context);
        }

        Group levelGroup = match.Groups["level"];
        string? levelText = levelGroup.Success ? levelGroup.Value : null;

        Group loggerGroup = match.Groups["logger"];

        var fields = new LogField[_fieldNames.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            Group group = match.Groups[_fieldNames[i]];
            fields[i] = new LogField(_fieldNames[i], group.Success ? group.Value : string.Empty);
        }

        return new LogEvent
        {
            Time = time,
            TimeText = timeText,
            Level = LogLevels.Normalize(levelText),
            LevelText = levelText,
            Source = loggerGroup.Success ? loggerGroup.Value : null,
            Message = message,
            Fields = fields,
        };
    }
}
