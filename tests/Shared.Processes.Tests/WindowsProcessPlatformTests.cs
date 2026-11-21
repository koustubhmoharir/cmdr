using System.Diagnostics;
using static Shared.Processes.Tests.ProcessTestHelpers;

namespace Shared.Processes.Tests;

/// <summary>Windows only: the launch preparation of <see cref="WindowsProcessPlatform"/> (no managed process). Skipped elsewhere.</summary>
[Collection("Windows processes")]
public class WindowsProcessPlatformTests
{
    [Fact]
    public void DetachedGroupLeavesItsProcessRunningAfterDispose()
    {
        if (!OperatingSystem.IsWindows()) return;

        // The leader outlives the group: after Detach and Dispose it must still be running.
        using var dir = new TempDir();
        var platform = new WindowsProcessPlatform();
        PreparedLaunch launch = platform.Prepare(Exec("ping", new[] { "-n", "30", "127.0.0.1" }, dir.Path));
        LaunchedTree tree = platform.Launch(launch, _ => { });
        int pid = tree.Process.Id;
        try
        {
            tree.Group.Detach();
            tree.Group.Dispose();

            Thread.Sleep(1000);
            using Process leader = Process.GetProcessById(pid);
            Assert.False(leader.HasExited, $"ping (pid {pid}) exited after Detach + Dispose");
        }
        finally
        {
            try
            {
                using Process leader = Process.GetProcessById(pid);
                leader.Kill(entireProcessTree: true);
            }
            catch (ArgumentException)
            {
                // Already gone.
            }

            tree.Process.Dispose();
        }
    }

    /// <summary>Builds a launch request running <c>cmd /c command</c>.</summary>
    [Fact]
    public void ConsoleStdinLeavesStdinUnredirected()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        PreparedLaunch pipe = new WindowsProcessPlatform().Prepare(Cmd("echo hi", dir.Path));
        PreparedLaunch console = new WindowsProcessPlatform().Prepare(Cmd("echo hi", dir.Path) with { Stdin = StdinMode.Console });

        Assert.True(pipe.StartInfo.RedirectStandardInput);
        Assert.False(console.StartInfo.RedirectStandardInput);
        Assert.True(console.StartInfo.CreateNoWindow);
        Assert.Null(console.Notice);
    }

    [Fact]
    public void ConsoleStdinOnBatchFileDropsTheAnswerAndSaysSo()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        string script = dir.File("run.cmd");
        File.WriteAllText(script, "@echo off\r\necho hi\r\n");

        PreparedLaunch pipe = new WindowsProcessPlatform().Prepare(ProcessTestHelpers.Exec(script, Array.Empty<string>(), dir.Path));
        PreparedLaunch console = new WindowsProcessPlatform().Prepare(
            ProcessTestHelpers.Exec(script, Array.Empty<string>(), dir.Path) with { Stdin = StdinMode.Console });

        Assert.Equal("Y\r\n", pipe.InputAfterInterrupt);
        Assert.Null(pipe.Notice);
        Assert.Null(console.InputAfterInterrupt);
        Assert.False(console.StartInfo.RedirectStandardInput);
        Assert.Equal("stdin is the console: cmdr cannot answer \"Terminate batch job (Y/N)?\" after Ctrl+C", console.Notice);
    }

    private static LaunchSpec Cmd(
        string command,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? env = null) =>
        ProcessTestHelpers.Exec("cmd", new[] { "/c", command }, workingDirectory, env);
}
