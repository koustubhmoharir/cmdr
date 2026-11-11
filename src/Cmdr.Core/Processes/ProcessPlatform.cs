namespace Cmdr.Core.Processes;

/// <summary>Selects the process platform implementation for the current operating system.</summary>
internal static class ProcessPlatform
{
    private static readonly IProcessPlatform s_current = Create();

    /// <summary>The platform implementation for this operating system.</summary>
    public static IProcessPlatform Current => s_current;

    private static IProcessPlatform Create() =>
        OperatingSystem.IsLinux()
            ? new LinuxProcessPlatform()
            : new UnsupportedProcessPlatform();
}
