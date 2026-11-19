using System.Globalization;
using Logvu.Core.Detail;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Shared.Core;

namespace Logvu.Tui;

/// <summary>
/// The text of the filter editor's fields, as the user typed it. Times are text until they are parsed, and the predicates are one
/// <c>path = value</c> per line.
/// </summary>
/// <param name="MinLevel">The minimum level, or <c>null</c> for any level.</param>
/// <param name="Source">The source substring. Trimmed when parsed.</param>
/// <param name="File">The file name substring. Trimmed when parsed.</param>
/// <param name="From">The earliest time, or blank.</param>
/// <param name="To">The latest time, or blank.</param>
/// <param name="Text">The text to match. Not trimmed: spaces are part of it.</param>
/// <param name="CaseSensitive">Whether the text is matched case-sensitively.</param>
/// <param name="WholeWord">Whether the text must match whole words.</param>
/// <param name="Regex">Whether the text is a regular expression.</param>
/// <param name="Predicates">The field predicates, one <c>path = value</c> per line.</param>
internal sealed record FilterInput(
    LogLevel? MinLevel,
    string Source,
    string File,
    string From,
    string To,
    string Text,
    bool CaseSensitive,
    bool WholeWord,
    bool Regex,
    string Predicates);

/// <summary>A field of the filter editor that cannot be used. It names the field, for the message the user sees.</summary>
internal sealed class FilterInputException : Exception
{
    /// <summary>Creates the exception for a field.</summary>
    /// <param name="field">The label of the field, such as <c>Time from</c>.</param>
    /// <param name="message">What is wrong with the field's input.</param>
    public FilterInputException(string field, string message)
        : base(message)
    {
        Field = field;
    }

    /// <summary>The label of the field that cannot be used.</summary>
    public string Field { get; }
}

/// <summary>
/// The filter editor's input rules, pure: the level choices, times, field predicates, and the spec and filter that the input builds. The
/// editor shows and edits the text, and a filter is built from it here, so that the rules are testable without a terminal.
/// </summary>
internal static class FilterForm
{
    /// <summary>The separator of a field predicate line: the first occurrence splits the path from the value.</summary>
    public const string PredicateSeparator = " = ";

    /// <summary>The form of a time that the editor shows, and of a time it accepts with a date.</summary>
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The label of the level choice that matches every level.</summary>
    public const string AnyLevel = "(any)";

    private static readonly string[] DatedTimeFormats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];
    private static readonly string[] TimeOnlyFormats = ["HH:mm", "HH:mm:ss"];

    /// <summary>The levels a minimum level steps through, from the lowest. <c>Trace</c> is not one of them.</summary>
    private static readonly LogLevel[] MinLevelSteps = [LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal];

    /// <summary>The minimum-level choices in order: any level first, then the levels from the lowest.</summary>
    public static IReadOnlyList<(string Label, LogLevel? Level)> LevelChoices { get; } =
    [
        (AnyLevel, null),
        (LevelLabel(LogLevel.Trace), LogLevel.Trace),
        (LevelLabel(LogLevel.Debug), LogLevel.Debug),
        (LevelLabel(LogLevel.Info), LogLevel.Info),
        (LevelLabel(LogLevel.Warn), LogLevel.Warn),
        (LevelLabel(LogLevel.Error), LogLevel.Error),
        (LevelLabel(LogLevel.Fatal), LogLevel.Fatal),
    ];

    /// <summary>The label of a minimum level's choice: its name in capitals, as the status line shows it.</summary>
    /// <param name="level">The level.</param>
    public static string LevelLabel(LogLevel level) => level.ToString().ToUpperInvariant();

    /// <summary>The index of the choice for a minimum level: <c>0</c> (any level) for <c>null</c>.</summary>
    /// <param name="level">The minimum level, or <c>null</c>.</param>
    public static int LevelIndex(LogLevel? level)
    {
        int index = LevelChoices.ToList().FindIndex(choice => choice.Level == level);
        return Math.Max(0, index);
    }

    /// <summary>The minimum level of the choice at an index. Null for any level, and for an index that is not a choice.</summary>
    /// <param name="index">The index of the choice.</param>
    public static LogLevel? LevelAt(int? index) =>
        index is { } at && at >= 0 && at < LevelChoices.Count ? LevelChoices[at].Level : null;

    /// <summary>
    /// Parses a time of the editor, in the zone. <c>yyyy-MM-dd HH:mm</c> and <c>yyyy-MM-dd HH:mm:ss</c> are a date and a time; <c>HH:mm</c> and
    /// <c>HH:mm:ss</c> are a time today, the date of <paramref name="now"/> in the zone. A blank text is no time.
    /// </summary>
    /// <param name="text">The text of the field.</param>
    /// <param name="field">The label of the field, for the error.</param>
    /// <param name="zone">The time zone the time is in.</param>
    /// <param name="now">The current instant, for a time of today.</param>
    /// <returns>The instant, or <c>null</c> when the text is blank.</returns>
    /// <exception cref="FilterInputException">The text is not one of the forms.</exception>
    public static DateTimeOffset? ParseTime(string text, string field, TimeZoneInfo zone, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(zone);

        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        DateTime local;
        if (DateTime.TryParseExact(trimmed, DatedTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dated))
        {
            local = dated;
        }
        else if (DateTime.TryParseExact(trimmed, TimeOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime clock))
        {
            local = TimeZoneInfo.ConvertTime(now, zone).Date + clock.TimeOfDay;
        }
        else
        {
            throw new FilterInputException(field, $"Use yyyy-MM-dd HH:mm[:ss], or HH:mm[:ss] for today. \"{trimmed}\" is not a time.");
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>The text of a time in the zone, in the form the editor shows.</summary>
    /// <param name="time">The instant.</param>
    /// <param name="zone">The zone the text is in.</param>
    public static string FormatTime(DateTimeOffset time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(time, zone).ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses the field predicates: one <c>path = value</c> per line, split at the first <see cref="PredicateSeparator"/>. Blank lines are
    /// ignored. The path is trimmed and must not be empty; the value is kept as typed.
    /// </summary>
    /// <param name="text">The text of the field.</param>
    /// <exception cref="FilterInputException">A line has no separator or no path. The message names the line.</exception>
    public static IReadOnlyList<FieldPredicate> ParsePredicates(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var predicates = new List<FieldPredicate>();
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int at = line.IndexOf(PredicateSeparator, StringComparison.Ordinal);
            if (at < 0)
            {
                throw new FilterInputException(FieldLabels.Predicates, $"Line {i + 1} is not path = value: \"{line.Trim()}\".");
            }

            string path = line[..at].Trim();
            if (path.Length == 0)
            {
                throw new FilterInputException(FieldLabels.Predicates, $"Line {i + 1} has no path before \" = \".");
            }

            predicates.Add(new FieldPredicate(path, line[(at + PredicateSeparator.Length)..]));
        }

        return predicates;
    }

    /// <summary>The field predicates as the editor shows them: one <c>path = value</c> per line.</summary>
    /// <param name="predicates">The predicates.</param>
    public static string FormatPredicates(IEnumerable<FieldPredicate> predicates) =>
        string.Join("\n", predicates.Select(predicate => predicate.Path + PredicateSeparator + predicate.Value));

    /// <summary>
    /// Builds the spec of the editor's input. A blank field is not set. The source and file names are trimmed. The times must be in order,
    /// and the text must be a valid pattern.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="zone">The zone the times are in.</param>
    /// <param name="now">The current instant, for a time of today.</param>
    /// <exception cref="FilterInputException">A field cannot be used. The exception names the field.</exception>
    public static FilterSpec Build(FilterInput input, TimeZoneInfo zone, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);

        DateTimeOffset? from = ParseTime(input.From, FieldLabels.From, zone, now);
        DateTimeOffset? to = ParseTime(input.To, FieldLabels.To, zone, now);
        if (from is { } start && to is { } end && start > end)
        {
            throw new FilterInputException(FieldLabels.To, "is before Time from.");
        }

        SearchQuery? text = null;
        if (input.Text.Length > 0)
        {
            var query = new SearchQuery(input.Text, input.CaseSensitive, input.WholeWord, input.Regex);
            if (SearchPattern.Create(query, out _) is null)
            {
                throw new FilterInputException(FieldLabels.Text, "is not a valid regular expression.");
            }

            text = query;
        }

        IReadOnlyList<FieldPredicate> predicates = ParsePredicates(input.Predicates);
        return new FilterSpec(
            MinLevel: input.MinLevel,
            SourceContains: input.Source.Trim(),
            FileContains: input.File.Trim(),
            From: from,
            To: to,
            Text: text,
            Fields: predicates);
    }

    /// <summary>The input that shows a filter in the editor: the fields are pre-filled from it. Null is an empty editor.</summary>
    /// <param name="filter">The active filter, or <c>null</c>.</param>
    /// <param name="zone">The zone that times are shown in.</param>
    public static FilterInput InputOf(EventFilter? filter, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        SearchQuery? text = filter?.Text;
        return new FilterInput(
            MinLevel: filter?.MinLevel,
            Source: filter?.SourceContains ?? string.Empty,
            File: filter?.FileContains ?? string.Empty,
            From: filter?.From is { } from ? FormatTime(from, zone) : string.Empty,
            To: filter?.To is { } to ? FormatTime(to, zone) : string.Empty,
            Text: text?.Text ?? string.Empty,
            CaseSensitive: text?.CaseSensitive ?? false,
            WholeWord: text?.WholeWord ?? false,
            Regex: text?.Regex ?? false,
            Predicates: filter is null ? string.Empty : FormatPredicates(filter.Fields));
    }

    /// <summary>The spec of a filter, with every predicate it has. Null for no filter.</summary>
    /// <param name="filter">The filter, or <c>null</c>.</param>
    public static FilterSpec? SpecOf(EventFilter? filter) =>
        filter is null
            ? null
            : new FilterSpec(
                filter.MinLevel,
                filter.Levels,
                filter.SourceContains,
                filter.FileContains,
                filter.From,
                filter.To,
                filter.Text,
                filter.Fields);

    /// <summary>
    /// The minimum level one step from <paramref name="current"/>. Raising goes to the first step strictly above it, where no minimum counts as
    /// below every step. Lowering goes to the last step strictly below it, or to no minimum when there is none. At an end the result is
    /// <paramref name="current"/> itself: <c>Fatal</c> does not rise, and no minimum does not fall.
    /// </summary>
    /// <param name="current">The minimum level now, or <c>null</c> for none.</param>
    /// <param name="direction">Above zero to raise the minimum, below zero to lower it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is zero.</exception>
    public static LogLevel? StepMinLevel(LogLevel? current, int direction)
    {
        ArgumentOutOfRangeException.ThrowIfZero(direction);
        if (direction > 0)
        {
            foreach (LogLevel step in MinLevelSteps)
            {
                if (current is not { } now || step > now)
                {
                    return step;
                }
            }

            return current;
        }

        LogLevel? lower = null;
        foreach (LogLevel step in MinLevelSteps)
        {
            if (current is { } now && step < now)
            {
                lower = step;
            }
        }

        return lower;
    }

    /// <summary>
    /// The filter with its minimum level replaced and every other predicate kept. It is <c>null</c> when that leaves no predicate, so that
    /// the filter is cleared as an empty editor clears it.
    /// </summary>
    /// <param name="current">The active filter, or <c>null</c> for none.</param>
    /// <param name="minLevel">The new minimum level, or <c>null</c> for any level.</param>
    public static EventFilter? WithMinLevel(EventFilter? current, LogLevel? minLevel)
    {
        FilterSpec spec = SpecOf(current) ?? new FilterSpec();
        var filter = new EventFilter(spec with { MinLevel = minLevel });
        return filter.IsEmpty ? null : filter;
    }

    /// <summary>
    /// The filter with one more field predicate: the predicates of <paramref name="current"/> and <paramref name="predicate"/>, ANDed. A
    /// predicate that is already there is not added twice.
    /// </summary>
    /// <param name="current">The active filter, or <c>null</c> for none.</param>
    /// <param name="predicate">The predicate to add.</param>
    public static EventFilter Extend(EventFilter? current, FieldPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        FilterSpec spec = SpecOf(current) ?? new FilterSpec();
        var fields = spec.Fields?.ToList() ?? [];
        if (!fields.Contains(predicate))
        {
            fields.Add(predicate);
        }

        return new EventFilter(spec with { Fields = fields });
    }

    /// <summary>
    /// The field predicate that a detail line filters on: its scope (the field name or the JSON dotted path) and its value. A line with no
    /// value, such as a message, a fold, or a line of a multi-line string, filters on nothing.
    /// </summary>
    /// <param name="line">The line under the cursor.</param>
    public static FieldPredicate? FieldPredicateOf(DetailLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Value is not { } value || string.IsNullOrEmpty(line.Scope))
        {
            return null;
        }

        return new FieldPredicate(line.Scope, value);
    }

    /// <summary>The labels of the editor's fields, as the errors name them.</summary>
    public static class FieldLabels
    {
        /// <summary>The label of the source field.</summary>
        public const string Source = "Source contains";

        /// <summary>The label of the file name field.</summary>
        public const string File = "File contains";

        /// <summary>The label of the text field.</summary>
        public const string Text = "Text";

        /// <summary>The label of the earliest time field.</summary>
        public const string From = "Time from";

        /// <summary>The label of the latest time field.</summary>
        public const string To = "Time to";

        /// <summary>The label of the field predicates.</summary>
        public const string Predicates = "Field predicates";
    }
}
