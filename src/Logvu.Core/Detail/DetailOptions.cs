using System.Collections.Frozen;

namespace Logvu.Core.Detail;

/// <summary>
/// What a detail document shows: the view mode and the expansion state. The state is stored as the ids whose expansion differs from their
/// default, so a fresh options value shows every node and fold in its default state, and a toggle is a change of one id.
/// </summary>
public sealed record DetailOptions
{
    /// <summary>The options of a fresh detail: the structured view with every node and fold in its default state.</summary>
    public static DetailOptions Default { get; } = new();

    /// <summary>Whether the raw event text is shown instead of the structured document.</summary>
    public bool Raw { get; init; }

    /// <summary>
    /// The ids whose expansion differs from their default. An id is a JSON node's path (for example <c>Properties.Scope</c> or
    /// <c>cause[0]</c>) or a fold's id (<c>fold:{scope}#{index}</c>). The default is expanded for a fold, and for a JSON node at depth
    /// two or less or on an error path; collapsed for any other node. An id the event does not have is ignored.
    /// </summary>
    public IReadOnlySet<string> Toggled { get; init; } = FrozenSet<string>.Empty;
}
