namespace Cmdr.Core.Processes;

/// <summary>Immutable view of a managed command's state and the tracked facts of its latest run.</summary>
/// <param name="State">Current lifecycle state.</param>
/// <param name="Pid">Leader process id of the current or latest run.</param>
/// <param name="StartedAt">When the latest run was started.</param>
/// <param name="ExitedAt">When the leader exited, if it has.</param>
/// <param name="StopRequestedAt">When a graceful stop was first requested in the latest run.</param>
/// <param name="ExitCode">Exit code of the leader, if it has exited.</param>
/// <param name="Error">Launch error message when <see cref="ProcessState.FailedToStart"/>.</param>
/// <param name="LeftoverCount">Live processes left in the group after the leader exited; 0 otherwise.</param>
/// <param name="StopRequested">True when a graceful stop or a force kill was requested while the run was active. Set when the
/// leader exits and kept afterwards; the exit code of a stopped run says nothing about whether it succeeded.</param>
/// <param name="Readiness">Readiness of the current run, from the command's ready patterns. <see cref="ProcessReadiness.None"/> when
/// the command has none, or is not running.</param>
/// <param name="ReadinessSince">When the current <see cref="Readiness"/> phase began; null when <see cref="Readiness"/> is None.</param>
public sealed record ProcessSnapshot(
    ProcessState State,
    int? Pid,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExitedAt,
    DateTimeOffset? StopRequestedAt,
    int? ExitCode,
    string? Error,
    int LeftoverCount,
    bool StopRequested = false,
    ProcessReadiness Readiness = ProcessReadiness.None,
    DateTimeOffset? ReadinessSince = null);

/// <summary>Readiness of a running command, as declared by its ready patterns.</summary>
public enum ProcessReadiness
{
    /// <summary>The command declares no ready patterns, or is not running.</summary>
    None,

    /// <summary>Running, and not (or no longer) showing its ready output.</summary>
    Starting,

    /// <summary>Running, and the last decisive output line matched the ready pattern.</summary>
    Ready,
}
