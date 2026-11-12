using System.Diagnostics;
using Cmdr.Core.Config;
using Cmdr.Core.Output;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;

namespace Cmdr.Core.Tests.Processes;

/// <summary>End-to-end tests of <see cref="ManagedProcess"/> on Windows (job object supervision). Skipped elsewhere.</summary>
public class WindowsProcessPlatformTests
{
    private const int BufferLines = 1000;

    [Fact]
    public void CapturesStdoutStderrAndExitCode()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Cmd("echo hello & echo err 1>&2 & exit /b 4", dir.Path));
            WaitForExited(p);

            ProcessSnapshot snap = p.Snapshot;
            Assert.Equal(ProcessState.Exited, snap.State);
            Assert.Equal(4, snap.ExitCode);
            Assert.Equal(0, snap.LeftoverCount);

            IReadOnlyList<OutputLine> lines = Lines(p);
            Assert.Contains(lines, l => l.Source == OutputSource.StdOut && l.PlainText.Trim() == "hello");
            Assert.Contains(lines, l => l.Source == OutputSource.StdErr && l.PlainText.Trim() == "err");
            Assert.Contains(lines, l => l.Source == OutputSource.System && l.PlainText == "=== exited with code 4 ===");
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void AppliesWorkingDirectoryAndEnvironmentOverlay()
    {
        if (!OperatingSystem.IsWindows()) return;

        const string removedVar = "CMDR_TEST_REMOVED";
        string? previous = Environment.GetEnvironmentVariable(removedVar);
        Environment.SetEnvironmentVariable(removedVar, "present");
        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            var env = new Dictionary<string, string?> { ["FOO"] = "bar", [removedVar] = null };
            p.Start(Cmd("cd & echo %FOO% & echo %CMDR_TEST_REMOVED%", dir.Path, env));
            WaitForExited(p);

            string[] texts = Lines(p).Where(l => l.Source == OutputSource.StdOut).Select(l => l.PlainText.Trim()).ToArray();
            Assert.Contains(texts, t => string.Equals(t.TrimEnd('\\'), dir.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            Assert.Contains("bar", texts);
            Assert.Contains("%CMDR_TEST_REMOVED%", texts); // cmd leaves an unknown variable unexpanded
        }
        finally
        {
            Cleanup(p);
            Environment.SetEnvironmentVariable(removedVar, previous);
        }
    }

    [Fact]
    public void ForceKillTerminatesTheWholeTree()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            // ping is a child of cmd: it must be in the job and die with the forced kill.
            p.Start(Cmd("ping -n 30 127.0.0.1 >nul", dir.Path));
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);

            p.ForceKill();
            WaitForExited(p);
            WaitUntil(() => p.Snapshot.LeftoverCount == 0, "no leftovers after force kill", p, TimeSpan.FromSeconds(5));
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void BackgroundChildIsLeftoverUntilForceKilled()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            LaunchSpec spec = Cmd("start /b ping -n 30 127.0.0.1 >nul & exit /b 0", dir.Path);
            p.Start(spec);
            WaitUntil(() => p.Snapshot.State == ProcessState.Exited, "leader to exit", p);

            WaitUntil(() => p.Snapshot.LeftoverCount >= 1, "background ping to remain", p);
            Assert.False(p.CanStart);
            Assert.Throws<InvalidOperationException>(() => p.Start(spec));

            p.ForceKill();
            WaitUntil(() => p.Snapshot.LeftoverCount == 0, "leftovers to clear after force kill", p, TimeSpan.FromSeconds(5));
            Assert.True(p.CanStart);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void DetachedGroupLeavesItsProcessRunningAfterDispose()
    {
        if (!OperatingSystem.IsWindows()) return;

        // The leader is a plain process that outlives the group: after Detach and Dispose it must still be running.
        var psi = new ProcessStartInfo("ping")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add("30");
        psi.ArgumentList.Add("127.0.0.1");

        using Process leader = Process.Start(psi)!;
        try
        {
            IProcessGroup group = new WindowsProcessPlatform().Attach(leader);
            group.Detach();
            group.Dispose();

            Thread.Sleep(1000);
            Assert.False(leader.HasExited);
        }
        finally
        {
            if (!leader.HasExited)
            {
                leader.Kill(entireProcessTree: true);
            }
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

    [Fact]
    public void ConsoleStdinGivesTheChildItsOwnConsoleAndKeepsCmdrStdin()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        nint before = WindowsNative.GetStdHandle(WindowsNative.StdInputHandle);
        var p = new ManagedProcess(BufferLines);
        try
        {
            LaunchSpec spec = ProcessTestHelpers.Exec(
                "powershell",
                new[] { "-NoProfile", "-Command", "[Console]::IsInputRedirected" },
                dir.Path) with { Stdin = StdinMode.Console };
            p.Start(spec);
            WaitForExited(p);

            // False: the child's stdin is its own hidden console, not a redirected pipe or cmdr's terminal.
            Assert.Contains(Lines(p), l => l.Source == OutputSource.StdOut && l.PlainText.Trim() == "False");
            Assert.Equal(before, WindowsNative.GetStdHandle(WindowsNative.StdInputHandle));
        }
        finally
        {
            Cleanup(p);
        }
    }

    private static LaunchSpec Cmd(
        string command,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? env = null) =>
        ProcessTestHelpers.Exec("cmd", new[] { "/c", command }, workingDirectory, env);
}
