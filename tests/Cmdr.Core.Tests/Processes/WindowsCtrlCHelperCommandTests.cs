using Cmdr.Core.Processes;

namespace Cmdr.Core.Tests.Processes;

/// <summary>Pure tests of how the Ctrl+C helper command line is chosen (runs on every OS).</summary>
public class WindowsCtrlCHelperCommandTests
{
    [Fact]
    public void ApphostWindowsPathUsesItselfWithoutPrefixArgs()
    {
        CtrlCHelperCommand command = CtrlCHelperCommand.Resolve(@"C:\tools\Cmdr.exe", @"C:\tools\Cmdr.dll");

        Assert.Equal(@"C:\tools\Cmdr.exe", command.FileName);
        Assert.Empty(command.PrefixArgs);
    }

    [Fact]
    public void ApphostUnixPathUsesItselfWithoutPrefixArgs()
    {
        CtrlCHelperCommand command = CtrlCHelperCommand.Resolve("/usr/bin/cmdr", "/usr/bin/Cmdr.dll");

        Assert.Equal("/usr/bin/cmdr", command.FileName);
        Assert.Empty(command.PrefixArgs);
    }

    [Fact]
    public void DotnetWindowsHostPassesEntryAssemblyAsPrefixArg()
    {
        CtrlCHelperCommand command = CtrlCHelperCommand.Resolve(@"C:\Program Files\dotnet\dotnet.exe", @"C:\src\Cmdr\bin\Cmdr.dll");

        Assert.Equal(@"C:\Program Files\dotnet\dotnet.exe", command.FileName);
        Assert.Equal(new[] { @"C:\src\Cmdr\bin\Cmdr.dll" }, command.PrefixArgs);
    }

    [Fact]
    public void DotnetUnixHostPassesEntryAssemblyAsPrefixArg()
    {
        CtrlCHelperCommand command = CtrlCHelperCommand.Resolve("/usr/share/dotnet/dotnet", "/home/u/cmdr/Cmdr.dll");

        Assert.Equal("/usr/share/dotnet/dotnet", command.FileName);
        Assert.Equal(new[] { "/home/u/cmdr/Cmdr.dll" }, command.PrefixArgs);
    }

    [Fact]
    public void DotnetHostMatchIsCaseInsensitive()
    {
        CtrlCHelperCommand command = CtrlCHelperCommand.Resolve(@"C:\dotnet\DOTNET.EXE", "C:/x/Cmdr.dll");

        Assert.Single(command.PrefixArgs);
    }

    [Fact]
    public void DotnetHostWithoutEntryAssemblyThrows()
    {
        Assert.Throws<InvalidOperationException>(() => CtrlCHelperCommand.Resolve("/usr/share/dotnet/dotnet", null));
    }

    [Fact]
    public void ArgumentsFor_AppendsFlagAndPidAfterPrefixArgs()
    {
        var command = new CtrlCHelperCommand("dotnet", new[] { "Cmdr.dll" });

        Assert.Equal(new[] { "Cmdr.dll", "--send-ctrl-c", "4242" }, command.ArgumentsFor(4242));
    }
}
