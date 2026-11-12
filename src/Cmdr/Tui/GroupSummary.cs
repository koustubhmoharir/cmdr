using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>
/// The summary of a collapsed group header: counts of the commands' states, e.g. <c>"2 running · 1 failed · 1 queued"</c>.
/// Idle and succeeded commands are not counted; zero counts are left out. Pure.
/// </summary>
internal static class GroupSummary
{
    private static readonly (StatusKind Kind, string Label)[] Labels =
    {
        (StatusKind.Running, "running"),
        (StatusKind.Starting, "starting"),
        (StatusKind.Ready, "ready"),
        (StatusKind.Stopping, "stopping"),
        (StatusKind.Queued, "queued"),
        (StatusKind.Failure, "failed"),
        (StatusKind.Warning, "with leftovers"),
    };

    /// <summary>The summary for the given command states, or an empty string when nothing is to be counted.</summary>
    public static string Compose(IEnumerable<StatusKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);

        var counts = new Dictionary<StatusKind, int>();
        foreach (StatusKind kind in kinds)
        {
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }

        var parts = new List<string>();
        foreach ((StatusKind kind, string label) in Labels)
        {
            if (counts.GetValueOrDefault(kind) > 0)
            {
                parts.Add(counts[kind] + " " + label);
            }
        }

        return string.Join(" · ", parts);
    }
}
