using Cmdr.Core.Processes;

namespace Cmdr.Core.Tests.Processes;

/// <summary>Pure tests of the Windows launch decision (runs on every OS).</summary>
public class WindowsLaunchPlanTests
{
    private const string ComSpec = @"C:\Windows\system32\cmd.exe";
    private const string SystemDirectory = @"C:\Windows\System32";

    [Fact]
    public void CmdFileRunsThroughComSpecWithRawArgumentsAndBatchAnswer()
    {
        WindowsLaunchPlan plan = WindowsCommandResolver.PlanLaunch(
            "npm",
            @"C:\Program Files\nodejs\npm.cmd",
            ComSpec,
            SystemDirectory,
            new[] { "run", "build" });

        Assert.Equal(ComSpec, plan.FileName);
        Assert.Equal(
            WindowsCmdEscaping.BuildCmdArguments(@"C:\Program Files\nodejs\npm.cmd", new[] { "run", "build" }),
            plan.Arguments);
        Assert.Empty(plan.ArgumentList);
        Assert.Equal("Y\r\n", plan.InputAfterInterrupt);
    }

    [Fact]
    public void UppercaseBatExtensionIsTreatedAsBatchFile()
    {
        WindowsLaunchPlan plan = WindowsCommandResolver.PlanLaunch("run", @"C:\tools\RUN.BAT", ComSpec, SystemDirectory, Array.Empty<string>());

        Assert.Equal(ComSpec, plan.FileName);
        Assert.Equal("Y\r\n", plan.InputAfterInterrupt);
    }

    [Fact]
    public void RootRelativeBatchResolutionLaunchesTheRootLevelBatchFile()
    {
        string resolved = WindowsCommandResolver.Resolve(
            @"\tools\run", @"C:\child", null, null,
            path => string.Equals(path, @"C:\tools\run.CMD", StringComparison.OrdinalIgnoreCase))!;

        WindowsLaunchPlan plan = WindowsCommandResolver.PlanLaunch(
            @"\tools\run", resolved, ComSpec, SystemDirectory, Array.Empty<string>());

        Assert.Equal(ComSpec, plan.FileName);
        Assert.Equal(WindowsCmdEscaping.BuildCmdArguments(@"C:\tools\run.CMD", Array.Empty<string>()), plan.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cmd.exe")]
    [InlineData(@"tools\cmd.exe")]
    [InlineData(@"\Windows\System32\cmd.exe")]
    [InlineData(@"C:cmd.exe")]
    public void MissingOrRelativeComSpecUsesCmdExeInTheSystemDirectory(string? comSpec)
    {
        WindowsLaunchPlan plan = WindowsCommandResolver.PlanLaunch("run", @"C:\tools\run.cmd", comSpec, SystemDirectory, Array.Empty<string>());

        Assert.Equal(@"C:\Windows\System32\cmd.exe", plan.FileName);
    }

    [Theory]
    [InlineData(@"D:\shells\cmd.exe")]
    [InlineData(@"\\server\share\cmd.exe")]
    public void FullPathComSpecIsUsed(string comSpec)
    {
        WindowsLaunchPlan plan = WindowsCommandResolver.PlanLaunch("run", @"C:\tools\run.cmd", comSpec, SystemDirectory, Array.Empty<string>());

        Assert.Equal(comSpec, plan.FileName);
    }

    [Fact]
    public void ExecutableRunsDirectlyWithArgumentList()
    {
        string[] args = { "a b", "--flag" };

        WindowsLaunchPlan plan = WindowsCommandResolver.PlanLaunch("x", @"C:\bin\x.exe", ComSpec, SystemDirectory, args);

        Assert.Equal(@"C:\bin\x.exe", plan.FileName);
        Assert.Null(plan.Arguments);
        Assert.Equal(args, plan.ArgumentList);
        Assert.Null(plan.InputAfterInterrupt);
    }

    [Fact]
    public void ExplicitNonPathExtFileRunsDirectlyWithArgumentList()
    {
        string[] args = { "--check" };

        WindowsLaunchPlan plan = WindowsCommandResolver.PlanLaunch("tool.bin", @"C:\child\tool.bin", ComSpec, SystemDirectory, args);

        Assert.Equal(@"C:\child\tool.bin", plan.FileName);
        Assert.Null(plan.Arguments);
        Assert.Equal(args, plan.ArgumentList);
        Assert.Null(plan.InputAfterInterrupt);
    }
}
