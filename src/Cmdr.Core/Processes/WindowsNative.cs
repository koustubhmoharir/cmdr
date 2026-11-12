using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Cmdr.Core.Processes;

/// <summary>P/Invoke declarations for the Win32 job object, console and control-event APIs.</summary>
/// <remarks>
/// All parameters are blittable (<see cref="nint"/>, <see cref="uint"/>, <see cref="int"/> for BOOL, or structs passed by
/// <c>ref</c>), so the generated marshalling is plain. Job handles are passed as raw <see cref="nint"/> values: callers keep them
/// alive with <see cref="SafeJobHandle"/>'s DangerousAddRef/DangerousRelease. BOOL return values are declared as <see cref="int"/>: non-zero is success.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static partial class WindowsNative
{
    private const string Kernel32 = "kernel32.dll";

    /// <summary>JOBOBJECT_INFORMATION_CLASS: JobObjectBasicAccountingInformation.</summary>
    internal const int JobObjectBasicAccountingInformation = 1;

    /// <summary>JOBOBJECT_INFORMATION_CLASS: JobObjectExtendedLimitInformation.</summary>
    internal const int JobObjectExtendedLimitInformation = 9;

    /// <summary>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: terminate all processes in the job when its last handle closes.</summary>
    internal const uint JobObjectLimitKillOnJobClose = 0x2000;

    /// <summary>CTRL_C_EVENT for <see cref="GenerateConsoleCtrlEvent"/>.</summary>
    internal const uint CtrlCEvent = 0;

    /// <summary>Creates an unnamed job object. Returns the raw handle (0 on failure); wrap it in <see cref="SafeJobHandle"/>.</summary>
    [LibraryImport(Kernel32, EntryPoint = "CreateJobObjectW", SetLastError = true)]
    internal static partial nint CreateJobObjectW(nint attributes, nint name);

    [LibraryImport(Kernel32, EntryPoint = "SetInformationJobObject", SetLastError = true)]
    internal static partial int SetInformationJobObject(nint job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

    [LibraryImport(Kernel32, EntryPoint = "AssignProcessToJobObject", SetLastError = true)]
    internal static partial int AssignProcessToJobObject(nint job, nint process);

    [LibraryImport(Kernel32, EntryPoint = "TerminateJobObject", SetLastError = true)]
    internal static partial int TerminateJobObject(nint job, uint exitCode);

    [LibraryImport(Kernel32, EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    internal static partial int QueryInformationJobObject(
        nint job,
        int infoClass,
        ref JOBOBJECT_BASIC_ACCOUNTING_INFORMATION info,
        uint length,
        nint returnLength);

    [LibraryImport(Kernel32, EntryPoint = "CloseHandle", SetLastError = true)]
    internal static partial int CloseHandle(nint handle);

    [LibraryImport(Kernel32, EntryPoint = "FreeConsole", SetLastError = true)]
    internal static partial int FreeConsole();

    [LibraryImport(Kernel32, EntryPoint = "AttachConsole", SetLastError = true)]
    internal static partial int AttachConsole(uint processId);

    /// <summary>Installs or removes a handler. A null <paramref name="handler"/> with <paramref name="add"/> true makes the calling process ignore Ctrl+C.</summary>
    [LibraryImport(Kernel32, EntryPoint = "SetConsoleCtrlHandler", SetLastError = true)]
    internal static partial int SetConsoleCtrlHandler(nint handler, int add);

    /// <summary>Sends a control event to every process attached to the console (group id 0).</summary>
    [LibraryImport(Kernel32, EntryPoint = "GenerateConsoleCtrlEvent", SetLastError = true)]
    internal static partial int GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    /// <summary>STD_INPUT_HANDLE for <see cref="GetStdHandle"/> and <see cref="SetStdHandle"/> (-10 as a DWORD).</summary>
    internal const uint StdInputHandle = unchecked((uint)-10);

    /// <summary>Returns this process's standard handle (the process-wide one, not a per-call value). Zero or -1 when there is none.</summary>
    [LibraryImport(Kernel32, EntryPoint = "GetStdHandle", SetLastError = true)]
    internal static partial nint GetStdHandle(uint standardHandle);

    /// <summary>Sets this process's standard handle. Affects processes started later; a zero handle means none.</summary>
    [LibraryImport(Kernel32, EntryPoint = "SetStdHandle", SetLastError = true)]
    internal static partial int SetStdHandle(uint standardHandle, nint handle);
}
