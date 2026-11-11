using System.Diagnostics;

namespace Cmdr.Core.Processes;

/// <summary>Placeholder for operating systems without a process supervision implementation yet.</summary>
internal sealed class UnsupportedProcessPlatform : IProcessPlatform
{
    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">Always.</exception>
    public PreparedLaunch Prepare(LaunchSpec spec) =>
        throw new PlatformNotSupportedException("Process supervision is not implemented on this operating system yet.");

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">Always.</exception>
    public IProcessGroup Attach(Process process) =>
        throw new PlatformNotSupportedException("Process supervision is not implemented on this operating system yet.");
}
