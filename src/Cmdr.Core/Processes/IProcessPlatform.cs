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

    /// <summary>
    /// Starts <paramref name="process"/>, which was built from <paramref name="launch"/>. Platforms may need to adjust
    /// process-wide state around the start; see <see cref="PreparedLaunch.NullStdin"/>.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The process could not be started.</exception>
    void Start(Process process, PreparedLaunch launch);

    /// <summary>Attaches to a process that was just started from <see cref="Prepare"/>.</summary>
    IProcessGroup Attach(Process process);
}
