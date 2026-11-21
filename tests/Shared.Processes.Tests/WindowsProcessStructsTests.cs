using System.Runtime.InteropServices;

namespace Shared.Processes.Tests;

/// <summary>
/// Checks the process creation structure sizes and field offsets against the Win32 layouts, for both pointer sizes (runs on every OS:
/// Marshal.SizeOf and Marshal.OffsetOf work on blittable structs everywhere).
/// </summary>
public class WindowsProcessStructsTests
{
    [Fact]
    public void StartupInfoWHasTheWin32Size()
    {
        Assert.Equal(IntPtr.Size == 8 ? 104 : 68, Marshal.SizeOf<STARTUPINFOW>());
    }

    [Fact]
    public void StartupInfoExWHasTheWin32Size()
    {
        Assert.Equal(IntPtr.Size == 8 ? 112 : 72, Marshal.SizeOf<STARTUPINFOEXW>());
    }

    [Fact]
    public void ProcessInformationHasTheWin32Size()
    {
        Assert.Equal(IntPtr.Size == 8 ? 24 : 16, Marshal.SizeOf<PROCESS_INFORMATION>());
    }

    [Fact]
    public void StartupInfoWStandardHandleOffsets()
    {
        // hStdInput, hStdOutput and hStdError, as the SDK lays them out (x64: 80, 88, 96; x86: 56, 60, 64).
        (long input, long output, long error) = IntPtr.Size == 8 ? (80, 88, 96) : (56, 60, 64);

        Assert.Equal(input, Marshal.OffsetOf<STARTUPINFOW>(nameof(STARTUPINFOW.HStdInput)).ToInt64());
        Assert.Equal(output, Marshal.OffsetOf<STARTUPINFOW>(nameof(STARTUPINFOW.HStdOutput)).ToInt64());
        Assert.Equal(error, Marshal.OffsetOf<STARTUPINFOW>(nameof(STARTUPINFOW.HStdError)).ToInt64());
    }

    [Fact]
    public void StartupInfoExWAttributeListOffset()
    {
        // lpAttributeList follows the STARTUPINFOW (x64: 104; x86: 68).
        long expected = IntPtr.Size == 8 ? 104 : 68;

        Assert.Equal(expected, Marshal.OffsetOf<STARTUPINFOEXW>(nameof(STARTUPINFOEXW.LpAttributeList)).ToInt64());
    }
}
