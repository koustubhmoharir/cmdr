using System.Globalization;
using System.Text;
using Logvu.Core.Parsing;
using Logvu.Core.Store;
using Shared.Core;

namespace Logvu.Core.Filter;

/// <summary>
/// The filter editor's predicate over events: minimum or set of levels, source and file name substrings, a time interval, text, and
/// field-value predicates, all ANDed. Immutable, and safe to share between reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order.</b> <see cref="Matches"/> checks the row-level predicates first (level, source, file name, time), then the field predicates,
/// and the text last, since a regex is the most expensive. An event is read as text only when its row passes.
/// </para>
/// <para>
/// <b>File names.</b> A row holds only its member's generation, so the file name is found through a snapshot of the timeline. The filter
/// is built unbound, and the readers bind it to the snapshot they read with (<see cref="IBindableRowFilter"/>). A bound copy shares the
/// text pattern with the filter it came from, so <see cref="TimedOut"/> reports on both. An unbound filter with a file predicate throws
/// when it matches a row, rather than matching by a name it does not have.
/// </para>
/// <para>
/// <b>Time window.</b> <see cref="TimeWindow"/> gives the time bounds, so that <see cref="HistoryReader"/> can skip members whose time
/// range lies outside them.
/// </para>
/// </remarks>
public sealed class EventFilter : IBindableRowFilter
{
    private readonly LogLevel? _minLevel;
    private readonly IReadOnlySet<LogLevel>? _levels;
    private readonly string? _sourceContains;
    private readonly string? _fileContains;
    private readonly DateTimeOffset? _from;
    private readonly DateTimeOffset? _to;
    private readonly SearchQuery? _text;
    private readonly SearchPattern? _pattern;
    private readonly IReadOnlyList<FieldPredicate> _fields;
    private readonly CatalogSnapshot? _snapshot;
    private readonly string _description;

    /// <summary>Builds an unbound filter from a spec.</summary>
    /// <param name="spec">The predicates. Empty strings and empty lists are not set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// The text is not a valid pattern, a field path is empty, or <see cref="FilterSpec.From"/> is after <see cref="FilterSpec.To"/>.
    /// </exception>
    public EventFilter(FilterSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.From is { } from && spec.To is { } to && from > to)
        {
            throw new ArgumentException("The time interval ends before it starts.", nameof(spec));
        }

        _minLevel = spec.MinLevel;
        _levels = spec.Levels is { Count: > 0 } levels ? new HashSet<LogLevel>(levels) : null;
        _sourceContains = NonEmpty(spec.SourceContains);
        _fileContains = NonEmpty(spec.FileContains);
        _from = spec.From;
        _to = spec.To;
        _fields = (spec.Fields ?? []).Select(Checked).ToArray();

        if (spec.Text is { Text.Length: > 0 } text)
        {
            _text = text;
            _pattern = SearchPattern.Create(text, out string? error)
                ?? throw new ArgumentException($"The text is not a valid pattern: {error}.", nameof(spec));
        }

        _description = Describe(spec);
    }

    private EventFilter(EventFilter source, CatalogSnapshot snapshot)
    {
        _minLevel = source._minLevel;
        _levels = source._levels;
        _sourceContains = source._sourceContains;
        _fileContains = source._fileContains;
        _from = source._from;
        _to = source._to;
        _text = source._text;
        _pattern = source._pattern;
        _fields = source._fields;
        _description = source._description;
        _snapshot = snapshot;
    }

    /// <summary>The lowest level that matches, or <c>null</c> when no minimum is set.</summary>
    public LogLevel? MinLevel => _minLevel;

    /// <summary>The levels that match, or <c>null</c> when no set is given.</summary>
    public IReadOnlySet<LogLevel>? Levels => _levels;

    /// <summary>The source substring, or <c>null</c>.</summary>
    public string? SourceContains => _sourceContains;

    /// <summary>The file name substring, or <c>null</c>.</summary>
    public string? FileContains => _fileContains;

    /// <summary>The earliest matching time, or <c>null</c>.</summary>
    public DateTimeOffset? From => _from;

    /// <summary>The latest matching time, or <c>null</c>.</summary>
    public DateTimeOffset? To => _to;

    /// <summary>The text query, or <c>null</c> when there is no text predicate.</summary>
    public SearchQuery? Text => _text;

    /// <summary>The field-value predicates, each of which must hold. Empty when there are none.</summary>
    public IReadOnlyList<FieldPredicate> Fields => _fields;

    /// <summary>Whether the filter is bound to a timeline snapshot. Only a bound filter can match a file-name predicate.</summary>
    public bool IsBound => _snapshot is not null;

    /// <summary>
    /// Whether the text pattern timed out. It is set by a match and then stays set. From then on the text predicate matches nothing, so
    /// an event matches only when the other predicates hold and the text predicate does not apply. Read the flag to tell the user why
    /// results stopped. It is false when there is no text predicate.
    /// </summary>
    public bool TimedOut => _pattern?.TimedOut ?? false;

    /// <summary>
    /// Whether reads should stop: the text pattern timed out, so the filter matches nothing from now on. Equal to <see cref="TimedOut"/>.
    /// </summary>
    public bool Abandoned => TimedOut;

    /// <summary>The time interval the filter's events lie in, for <see cref="HistoryReader"/> to skip members outside it.</summary>
    public (DateTimeOffset? From, DateTimeOffset? To) TimeWindow => (_from, _to);

    /// <summary>Whether no predicate is set, so that every event matches.</summary>
    public bool IsEmpty =>
        _minLevel is null
        && _levels is null
        && _sourceContains is null
        && _fileContains is null
        && _from is null
        && _to is null
        && _pattern is null
        && _fields.Count == 0;

    /// <summary>
    /// A short description of the set predicates for the status line, such as <c>level ≥ WARN · source ~ Orders · text ~ timeout ·
    /// Properties.OrderId = 42</c>. It is empty when <see cref="IsEmpty"/>.
    /// </summary>
    public string Describe() => _description;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The filter has a file name predicate and is not bound to a snapshot.</exception>
    public IRowFilter Bind(CatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new EventFilter(this, snapshot);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The filter has a file name predicate and is not bound to a snapshot.</exception>
    public bool Matches(EventRow row, LogEvent ev, string text)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(text);

        if (!MatchesRow(row))
        {
            return false;
        }

        foreach (FieldPredicate predicate in _fields)
        {
            if (!MatchesField(ev, predicate))
            {
                return false;
            }
        }

        return _pattern is null || EventSearch.Matches(_pattern, ev, text);
    }

    /// <inheritdoc/>
    /// <remarks>True when a level, source, file name or time predicate is set: those are on the header line.</remarks>
    public bool ChecksHead =>
        _minLevel is not null
        || _levels is not null
        || _sourceContains is not null
        || _fileContains is not null
        || _from is not null
        || _to is not null;

    /// <inheritdoc/>
    /// <remarks>True when a time bound is set: the time is the only header field that is parsed with a format.</remarks>
    public bool ChecksHeadTime => _from is not null || _to is not null;

    /// <inheritdoc/>
    /// <remarks>
    /// True when a text pattern is set and no field predicate is: <see cref="Matches"/> checks the pattern last, after the row and the
    /// fields, so with no fields the pattern is decided by the text alone.
    /// </remarks>
    public bool ChecksText => _pattern is not null && _fields.Count == 0;

    /// <inheritdoc/>
    public bool MayMatchHead(EventRow head)
    {
        ArgumentNullException.ThrowIfNull(head);
        return MatchesRow(head);
    }

    /// <inheritdoc/>
    public bool MayMatchText(string text, ILogScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(text);
        return !ChecksText || EventSearch.MayMatch(_pattern!, text, scheme);
    }

    /// <summary>Checks the level, source, file name and time, which are all on the row.</summary>
    private bool MatchesRow(EventRow row)
    {
        if (_minLevel is { } minimum && (row.Level == LogLevel.Unknown || row.Level < minimum))
        {
            return false;
        }

        if (_levels is not null && !_levels.Contains(row.Level))
        {
            return false;
        }

        if (_sourceContains is not null && (row.Source is null || !row.Source.Contains(_sourceContains, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (_fileContains is not null && !MatchesFileName(row.Key.Generation))
        {
            return false;
        }

        if (_from is not null || _to is not null)
        {
            if (row.Time is not { } time)
            {
                return false;
            }

            if (_from is { } from && time < from)
            {
                return false;
            }

            if (_to is { } to && time > to)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Tells whether the member of a generation has a file name that contains the substring.</summary>
    private bool MatchesFileName(int generation)
    {
        if (_snapshot is null)
        {
            throw new InvalidOperationException("The filter has a file name predicate and must be bound to a snapshot before it matches.");
        }

        string? name = _snapshot.Find(generation)?.Name;
        return name is not null && name.Contains(_fileContains!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tells whether an event has the field's value. A JSON event is looked up by its dotted path. A text event has its named fields,
    /// and one of them with the path as its name must have the value.
    /// </summary>
    private static bool MatchesField(LogEvent ev, FieldPredicate predicate)
    {
        if (ev.Json is { } json)
        {
            return JsonFieldPath.TryGetText(json, predicate.Path, out string value) && value == predicate.Value;
        }

        foreach (LogField field in ev.Fields)
        {
            if (field.Name == predicate.Path && field.Value == predicate.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Null or empty becomes null, so that an empty predicate is not set.</summary>
    private static string? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    /// <summary>Checks a field predicate's path.</summary>
    private static FieldPredicate Checked(FieldPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrEmpty(predicate.Path);
        ArgumentNullException.ThrowIfNull(predicate.Value);
        return predicate;
    }

    /// <summary>Builds the status description of the set predicates, in the order the editor lists them.</summary>
    private static string Describe(FilterSpec spec)
    {
        var parts = new List<string>();
        if (spec.MinLevel is { } minimum)
        {
            parts.Add($"level ≥ {LevelName(minimum)}");
        }

        if (spec.Levels is { Count: > 0 } levels)
        {
            string names = string.Join(", ", levels.OrderBy(level => level).Select(LevelName));
            parts.Add($"level ∈ {{{names}}}");
        }

        if (NonEmpty(spec.SourceContains) is { } source)
        {
            parts.Add($"source ~ {source}");
        }

        if (NonEmpty(spec.FileContains) is { } file)
        {
            parts.Add($"file ~ {file}");
        }

        if (spec.From is { } from)
        {
            parts.Add($"time ≥ {from.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        }

        if (spec.To is { } to)
        {
            parts.Add($"time ≤ {to.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        }

        if (spec.Text is { Text.Length: > 0 } text)
        {
            var part = new StringBuilder(text.Regex ? $"text ~ /{text.Text}/" : $"text ~ {text.Text}");
            if (text.CaseSensitive)
            {
                part.Append(" (case-sensitive)");
            }

            if (text.WholeWord)
            {
                part.Append(" (whole word)");
            }

            parts.Add(part.ToString());
        }

        foreach (FieldPredicate field in spec.Fields ?? [])
        {
            parts.Add($"{field.Path} = {field.Value}");
        }

        return string.Join(" · ", parts);
    }

    private static string LevelName(LogLevel level) => level.ToString().ToUpperInvariant();
}
