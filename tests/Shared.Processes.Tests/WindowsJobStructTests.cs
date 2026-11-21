using System.Runtime.CompilerServices;

namespace Shared.Processes.Tests;

/// <summary>Checks the job object structure sizes against the Win32 layouts (64-bit only; runs on every OS).</summary>
public class WindowsJobStructTests
{
    [Fact]
    public void ExtendedLimitInformationIs144BytesOnX64()
    {
        if (!Environment.Is64BitProcess) return;

        Assert.Equal(144, Unsafe.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
    }

    [Fact]
    public void BasicLimitInformationIs64BytesOnX64()
    {
        if (!Environment.Is64BitProcess) return;

        Assert.Equal(64, Unsafe.SizeOf<JOBOBJECT_BASIC_LIMIT_INFORMATION>());
    }

    [Fact]
    public void IoCountersIs48Bytes()
    {
        Assert.Equal(48, Unsafe.SizeOf<IO_COUNTERS>());
    }
}
