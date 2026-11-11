using System.Runtime.InteropServices;

namespace Cmdr.Core.Processes;

// Win32 job object structures, laid out as in the Windows SDK (x64 natural alignment; the 32-bit layout
// differs only in SIZE_T width). Sizes are asserted in the unit tests.
// These types carry no platform attribute: they are plain data, and the tests use them on every OS.

/// <summary>JOBOBJECT_BASIC_LIMIT_INFORMATION (64 bytes on x64).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
{
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public uint LimitFlags;
    public nuint MinimumWorkingSetSize;
    public nuint MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public nuint Affinity;
    public uint PriorityClass;
    public uint SchedulingClass;
}

/// <summary>IO_COUNTERS (48 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IO_COUNTERS
{
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
}

/// <summary>JOBOBJECT_EXTENDED_LIMIT_INFORMATION (144 bytes on x64).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
{
    public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
    public IO_COUNTERS IoInfo;
    public nuint ProcessMemoryLimit;
    public nuint JobMemoryLimit;
    public nuint PeakProcessMemoryUsed;
    public nuint PeakJobMemoryUsed;
}

/// <summary>JOBOBJECT_BASIC_ACCOUNTING_INFORMATION (48 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
{
    public long TotalUserTime;
    public long TotalKernelTime;
    public long ThisPeriodTotalUserTime;
    public long ThisPeriodTotalKernelTime;
    public uint TotalPageFaultCount;
    public uint TotalProcesses;
    public uint ActiveProcesses;
    public uint TotalTerminatedProcesses;
}
