using Logvu.Core.Parsing;
using Shared.Core;

namespace Logvu.Core.Filter;

/// <summary>
/// The settings of a filter, as the filter editor holds them. Every predicate that is set must hold: the predicates are ANDed. A
/// <c>null</c> or empty predicate is not set. <see cref="EventFilter"/> is built from a spec and copies it.
/// </summary>
/// <param name="MinLevel">The lowest level that matches. An event of <see cref="LogLevel.Unknown"/> never matches a minimum.</param>
/// <param name="Levels">The levels that match. Applies together with <paramref name="MinLevel"/> when both are set.</param>
/// <param name="SourceContains">A substring of the event's source, compared ignoring case. An event without a source never matches it.</param>
/// <param name="FileContains">A substring of the member's file name, compared ignoring case.</param>
/// <param name="From">The earliest time that matches, inclusive. Events without a time never match a time bound.</param>
/// <param name="To">The latest time that matches, inclusive.</param>
/// <param name="Text">The text to search the event's full text and, for a JSON event, its rendered message for. Literal or regex, with its options.</param>
/// <param name="Fields">The field-value predicates, each of which must hold.</param>
public sealed record FilterSpec(
    LogLevel? MinLevel = null,
    IReadOnlySet<LogLevel>? Levels = null,
    string? SourceContains = null,
    string? FileContains = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    SearchQuery? Text = null,
    IReadOnlyList<FieldPredicate>? Fields = null);
