using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Cmdr.Core.Processes;

/// <summary>Owning handle to a job object. Releasing it closes the job, which kills its processes when KILL_ON_JOB_CLOSE is set.</summary>
[SupportedOSPlatform("windows")]
internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Takes ownership of <paramref name="handle"/> (a value returned by CreateJobObjectW, which must be valid).</summary>
    internal SafeJobHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle() => WindowsNative.CloseHandle(handle) != 0;
}
