using System.Runtime.InteropServices;

namespace Shared.Processes;

// Win32 process creation structures, laid out as in the Windows SDK (x64 natural alignment; pointer-sized fields are nint,
// so the 32-bit layout follows from the same declarations). Sizes are asserted in the unit tests.
// These types carry no platform attribute: they are plain data, and the tests use them on every OS.

/// <summary>STARTUPINFOW (104 bytes on x64).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct STARTUPINFOW
{
    public int Cb;
    public nint LpReserved;
    public nint LpDesktop;
    public nint LpTitle;
    public int DwX;
    public int DwY;
    public int DwXSize;
    public int DwYSize;
    public int DwXCountChars;
    public int DwYCountChars;
    public int DwFillAttribute;
    public int DwFlags;
    public short WShowWindow;
    public short CbReserved2;
    public nint LpReserved2;
    public nint HStdInput;
    public nint HStdOutput;
    public nint HStdError;
}

/// <summary>STARTUPINFOEXW (112 bytes on x64): STARTUPINFOW followed by the attribute list. Cb must be this struct's size.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct STARTUPINFOEXW
{
    public STARTUPINFOW StartupInfo;
    public nint LpAttributeList;
}

/// <summary>PROCESS_INFORMATION (24 bytes on x64).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_INFORMATION
{
    public nint HProcess;
    public nint HThread;
    public int DwProcessId;
    public int DwThreadId;
}

/// <summary>FILETIME: a 64-bit count of 100-nanosecond intervals, as two 32-bit halves.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FILETIME
{
    public uint DwLowDateTime;
    public uint DwHighDateTime;
}

/// <summary>
/// WIN32_FIND_DATAW (592 bytes on x64), the result of FindFirstFileW. For an app execution alias <see cref="DwFileAttributes"/> has
/// the reparse point bit and <see cref="DwReserved0"/> holds the reparse tag. The name buffers are fixed-size, as in the SDK, so the
/// struct is blittable and can be passed by pointer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WIN32_FIND_DATAW
{
    public uint DwFileAttributes;
    public FILETIME FtCreationTime;
    public FILETIME FtLastAccessTime;
    public FILETIME FtLastWriteTime;
    public uint NFileSizeHigh;
    public uint NFileSizeLow;
    public uint DwReserved0;
    public uint DwReserved1;
    public fixed char CFileName[260];
    public fixed char CAlternateFileName[14];
}
