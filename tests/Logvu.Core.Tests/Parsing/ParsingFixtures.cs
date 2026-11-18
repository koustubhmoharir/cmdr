using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Parsing;

/// <summary>Shared helpers for the parsing tests: fixture access, contexts and assertions.</summary>
internal static class ParsingFixtures
{
    /// <summary>Offset-free times are read as UTC.</summary>
    public static ParseContext Utc { get; } = new(TimeZoneInfo.Utc);

    /// <summary>Offset-free times are read in a fixed +05:30 zone, so a test can see the zone was used.</summary>
    public static ParseContext Custom { get; } =
        new(TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(5.5), "t", "t"));

    /// <summary>The lines of a fixture file, as written.</summary>
    public static string[] Lines(string fileName) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    /// <summary>
    /// Splits a text fixture into events the way real framing will: a header starts a new event, and lines before the
    /// first header form one orphan event.
    /// </summary>
    public static List<string> TextEvents(string fileName, ILogScheme scheme)
    {
        var events = new List<string>();
        var current = new List<string>();
        foreach (string line in Lines(fileName))
        {
            if (current.Count > 0 && scheme.IsHeader(line))
            {
                events.Add(string.Join('\n', current));
                current.Clear();
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            events.Add(string.Join('\n', current));
        }

        return events;
    }

    /// <summary>The non-blank lines of a JSON Lines fixture, one event each.</summary>
    public static List<string> JsonEvents(string fileName) =>
        Lines(fileName).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

    /// <summary>Asserts a time has the given wall-clock value and offset, not just the same instant.</summary>
    public static void AssertTime(DateTimeOffset? actual, DateTime local, TimeSpan offset)
    {
        Assert.NotNull(actual);
        Assert.Equal(new DateTimeOffset(local, offset), actual.Value);
        Assert.Equal(offset, actual.Value.Offset);
    }

    /// <summary>Asserts the fields by name and value, in order.</summary>
    public static void AssertFields(LogEvent e, params (string Name, string Value)[] expected) =>
        Assert.Equal(expected.Select(f => new LogField(f.Name, f.Value)), e.Fields);
}
