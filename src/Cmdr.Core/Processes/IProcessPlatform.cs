using System.Diagnostics;

namespace Cmdr.Core.Processes;

/// <summary>Platform-specific launch and process-tree control.</summary>
internal interface IProcessPlatform
{
    /// <summary>
    /// Builds the start info that launches <paramref name="spec"/> in its own process group or job, plus any stdin
    /// input the platform needs after a Ctrl+C.
    /// </summary>
    PreparedLaunch Prepare(LaunchSpec spec);

    /// <summary>Attaches to a process that was just started from <see cref="Prepare"/>.</summary>
    IProcessGroup Attach(Process process);
}
