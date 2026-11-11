using Cmdr.Core.Processes;

namespace Cmdr.Tui;

/// <summary>What Enter does to one target, as far as the hint is concerned.</summary>
/// <param name="State">The process state of the command.</param>
/// <param name="LeftoverCount">Processes left over after the leader exited.</param>
/// <param name="CanStart">Whether the command may be started now.</param>
internal readonly record struct EnterTarget(ProcessState State, int LeftoverCount, bool CanStart);

/// <summary>
/// The word shown after "Enter" in the hint bar. Mirrors <c>Session.Activate</c>: when any target is stopping or exited
/// with leftovers, Enter kills; otherwise it starts the startable targets, unless their group's sequence is already
/// running. Pure.
/// </summary>
internal static class EnterHint
{
    /// <summary>
    /// "Kill", "Start", or null when Enter would do nothing for these targets.
    /// <paramref name="sequenceBlocksStart"/> is true for a sequential group whose sequence is running.
    /// </summary>
    public static string? Describe(IEnumerable<EnterTarget> targets, bool sequenceBlocksStart)
    {
        ArgumentNullException.ThrowIfNull(targets);

        bool kill = false;
        bool start = false;
        foreach (EnterTarget target in targets)
        {
            if (target.State == ProcessState.Stopping || (target.State == ProcessState.Exited && target.LeftoverCount > 0))
            {
                kill = true;
            }
            else if (target.CanStart)
            {
                start = true;
            }
        }

        if (kill)
        {
            return "Kill";
        }

        return start && !sequenceBlocksStart ? "Start" : null;
    }
}
