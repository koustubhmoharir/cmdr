using System.Runtime.InteropServices;

namespace Cmdr.Core.Processes;

/// <summary>P/Invoke declarations for libc.</summary>
internal static partial class LinuxNative
{
    /// <summary>Sends <paramref name="sig"/> to <paramref name="pid"/>; a negative pid addresses a process group.</summary>
    /// <returns>0 on success, -1 on failure (the error is available from <see cref="Marshal.GetLastPInvokeError"/>).</returns>
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    internal static partial int Kill(int pid, int sig);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    internal static partial uint GetEffectiveUserId();
}
