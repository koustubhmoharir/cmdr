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

    /// <summary>JOBOBJECT_INFORMATION_CLASS: JobObjectBasicProcessIdList (JOBOBJECT_BASIC_PROCESS_ID_LIST).</summary>
    internal const int JobObjectBasicProcessIdList = 3;

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

    /// <summary>Queries into a caller-allocated buffer (used for <see cref="JobObjectBasicProcessIdList"/>).</summary>
    [LibraryImport(Kernel32, EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    internal static partial int QueryInformationJobObject(nint job, int infoClass, nint info, uint length, nint returnLength);

    /// <summary>ERROR_MORE_DATA: the process id list buffer is too small.</summary>
    internal const int ErrorMoreData = 234;

    /// <summary>PROCESS_QUERY_LIMITED_INFORMATION for <see cref="OpenProcess"/>.</summary>
    internal const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>Opens a process by id. Returns 0 on failure; close the handle with <see cref="CloseHandle"/>.</summary>
    [LibraryImport(Kernel32, EntryPoint = "OpenProcess", SetLastError = true)]
    internal static partial nint OpenProcess(uint access, int inheritHandle, uint processId);

    /// <summary>
    /// Writes the Win32 path of the process's image into <paramref name="buffer"/> (UTF-16). <paramref name="size"/> is the
    /// buffer's capacity in characters on input and the path length (without the NUL) on output. Non-zero is success.
    /// </summary>
    [LibraryImport(Kernel32, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    internal static partial int QueryFullProcessImageNameW(nint process, uint flags, nint buffer, ref uint size);

    /// <summary>Fills <paramref name="info"/> for an open file. Non-zero is success.</summary>
    [LibraryImport(Kernel32, EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    internal static partial int GetFileInformationByHandle(nint file, out BY_HANDLE_FILE_INFORMATION info);

    /// <summary>BY_HANDLE_FILE_INFORMATION: the attributes, the volume serial number and the file index (which with the serial identifies a file).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

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

    /// <summary>STD_INPUT_HANDLE for <see cref="GetStdHandle"/> (-10 as a DWORD).</summary>
    internal const uint StdInputHandle = unchecked((uint)-10);

    /// <summary>Returns this process's standard handle (the process-wide one, not a per-call value). Zero or -1 when there is none.</summary>
    [LibraryImport(Kernel32, EntryPoint = "GetStdHandle", SetLastError = true)]
    internal static partial nint GetStdHandle(uint standardHandle);

    /// <summary>PROC_THREAD_ATTRIBUTE_HANDLE_LIST: with bInheritHandles, the child inherits only the listed handles (Windows Vista+).</summary>
    internal const nint ProcThreadAttributeHandleList = 0x00020002;

    /// <summary>PROC_THREAD_ATTRIBUTE_JOB_LIST: the child is created inside the listed jobs (Windows 10 version 1607+).</summary>
    internal const nint ProcThreadAttributeJobList = 0x0002000D;

    /// <summary>EXTENDED_STARTUPINFO_PRESENT: lpStartupInfo is a STARTUPINFOEXW with an attribute list.</summary>
    internal const uint ExtendedStartupInfoPresent = 0x00080000;

    /// <summary>CREATE_NO_WINDOW: the console application gets a new hidden console.</summary>
    internal const uint CreateNoWindow = 0x08000000;

    /// <summary>CREATE_UNICODE_ENVIRONMENT: the environment block is UTF-16.</summary>
    internal const uint CreateUnicodeEnvironment = 0x00000400;

    /// <summary>STARTF_USESTDHANDLES: hStdInput, hStdOutput and hStdError of STARTUPINFO are used (a zero value means none).</summary>
    internal const int StartfUseStdHandles = 0x00000100;

    /// <summary>
    /// Initializes an attribute list. Called with a null list it fails with ERROR_INSUFFICIENT_BUFFER and sets <paramref name="size"/>
    /// to the bytes needed (that is the sizing call).
    /// </summary>
    [LibraryImport(Kernel32, EntryPoint = "InitializeProcThreadAttributeList", SetLastError = true)]
    internal static partial int InitializeProcThreadAttributeList(nint attributeList, int attributeCount, int flags, ref nint size);

    [LibraryImport(Kernel32, EntryPoint = "UpdateProcThreadAttribute", SetLastError = true)]
    internal static partial int UpdateProcThreadAttribute(
        nint attributeList,
        uint flags,
        nint attribute,
        nint value,
        nint size,
        nint previousValue,
        nint returnSize);

    [LibraryImport(Kernel32, EntryPoint = "DeleteProcThreadAttributeList")]
    internal static partial void DeleteProcThreadAttributeList(nint attributeList);

    /// <summary>
    /// Creates a process. Strings are UTF-16. <paramref name="commandLine"/> is a writable, null-terminated UTF-16 buffer
    /// (CreateProcessW may modify it), and <paramref name="environment"/> is a UTF-16 block with <see cref="CreateUnicodeEnvironment"/>.
    /// Non-zero is success.
    /// </summary>
    [LibraryImport(Kernel32, EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int CreateProcessW(
        string? applicationName,
        nint commandLine,
        nint processAttributes,
        nint threadAttributes,
        int inheritHandles,
        uint creationFlags,
        nint environment,
        string? currentDirectory,
        ref STARTUPINFOEXW startupInfo,
        out PROCESS_INFORMATION processInformation);

    /// <summary>DUPLICATE_SAME_ACCESS for <see cref="DuplicateHandle"/>.</summary>
    internal const uint DuplicateSameAccess = 0x00000002;

    /// <summary>The pseudo handle of the current process (what GetCurrentProcess returns); it needs no closing.</summary>
    internal static readonly nint CurrentProcess = -1;

    /// <summary>Duplicates a handle. Non-zero is success; close <paramref name="targetHandle"/> with <see cref="CloseHandle"/>.</summary>
    [LibraryImport(Kernel32, EntryPoint = "DuplicateHandle", SetLastError = true)]
    internal static partial int DuplicateHandle(
        nint sourceProcess,
        nint sourceHandle,
        nint targetProcess,
        out nint targetHandle,
        uint desiredAccess,
        int inheritHandle,
        uint options);

    /// <summary>Reads a process's exit code. Valid once the process has exited; non-zero is success.</summary>
    [LibraryImport(Kernel32, EntryPoint = "GetExitCodeProcess", SetLastError = true)]
    internal static partial int GetExitCodeProcess(nint process, out uint exitCode);


    /// <summary>Reports whether <paramref name="process"/> is in <paramref name="job"/> (or any job, for a null job). <paramref name="result"/> is non-zero when it is.</summary>
    [LibraryImport(Kernel32, EntryPoint = "IsProcessInJob", SetLastError = true)]
    internal static partial int IsProcessInJob(nint process, nint job, out int result);
}
