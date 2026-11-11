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
public sealed record ProcessSnapshot(
    ProcessState State,
    int? Pid,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExitedAt,
    DateTimeOffset? StopRequestedAt,
    int? ExitCode,
    string? Error,
    int LeftoverCount);
