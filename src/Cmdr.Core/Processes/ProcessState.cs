namespace Cmdr.Core.Processes;

/// <summary>Lifecycle state of a managed command.</summary>
public enum ProcessState
{
    /// <summary>Never started in this session.</summary>
    NotStarted,

    /// <summary>The leader is running.</summary>
    Running,

    /// <summary>A graceful stop was requested and the leader is still running.</summary>
    Stopping,

    /// <summary>The leader has exited. Descendants may still be alive (see <see cref="ProcessSnapshot.HasLeftovers"/>).</summary>
    Exited,

    /// <summary>The leader could not be launched.</summary>
    FailedToStart,
}
