namespace Cmdr.Core.Processes;

/// <summary>Selects the process platform implementation for the current operating system.</summary>
internal static class ProcessPlatform
{
    private static readonly IProcessPlatform s_current = Create();

    /// <summary>The platform implementation for this operating system.</summary>
    public static IProcessPlatform Current => s_current;

    private static IProcessPlatform Create()
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxProcessPlatform();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsProcessPlatform();
        }

        return new UnsupportedProcessPlatform();
    }
}
