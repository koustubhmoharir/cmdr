using Cmdr.Core.Output;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using Shared.Processes;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;

namespace Cmdr.Core.Tests.Processes;

/// <summary>Readiness tracking of <see cref="ManagedProcess"/> against real processes (Linux only).</summary>
/// <remarks>
/// The scripts wait for gate files that the test creates, so each output line is written only after the test has observed
/// the previous state. Waiting for a gate is a shell poll, not a test sleep.
/// </remarks>
public class ManagedProcessReadinessTests
{
    private const int BufferLines = 1000;

    /// <summary>Prints a line, then waits for the gate file of the next step. <c>wait_for</c> is defined in each script.</summary>
    private const string Prelude = "wait_for() { while [ ! -f \"$1\" ]; do sleep 0.02; done; }\n";

    private static LaunchSpec WithReady(LaunchSpec spec, string pattern, string? busy = null) =>
        spec with { Ready = new ReadySpec { Pattern = pattern, Busy = busy } };

    [Fact]
    public void Readiness_FollowsReadyAndBusyLinesAcrossRuns()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            string script = Prelude + """
                echo booting
                wait_for g1
                echo "READY now"
                wait_for g2
                echo "rebuild started"
                wait_for g3
                echo "READY again"
                wait_for g4
                exit 0
                """;
            p.Start(WithReady(Sh(script, dir.Path), "READY", "rebuild"));

            WaitUntil(() => Text(p).Contains("booting", StringComparison.Ordinal), "booting", p);
            Assert.Equal(ProcessReadiness.Starting, p.Snapshot.Readiness);
            Assert.NotNull(p.Snapshot.ReadinessSince);
            Assert.Equal(p.Snapshot.StartedAt, p.Snapshot.ReadinessSince);

            Touch(dir, "g1");
            WaitUntil(() => p.Snapshot.Readiness == ProcessReadiness.Ready, "Ready after 'READY now'", p);
            DateTimeOffset? readySince = p.Snapshot.ReadinessSince;
            Assert.True(readySince >= p.Snapshot.StartedAt);

            Touch(dir, "g2");
            WaitUntil(() => p.Snapshot.Readiness == ProcessReadiness.Starting, "Starting after 'rebuild started'", p);
            Assert.True(p.Snapshot.ReadinessSince > readySince);

            Touch(dir, "g3");
            WaitUntil(() => p.Snapshot.Readiness == ProcessReadiness.Ready, "Ready after 'READY again'", p);

            Touch(dir, "g4");
            WaitForExited(p);
            Assert.Equal(ProcessReadiness.None, p.Snapshot.Readiness);
            Assert.Null(p.Snapshot.ReadinessSince);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void Readiness_IsNoneWhileRunningWithoutReadySpec()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh(Prelude + "echo READY\nwait_for g1\nexit 0", dir.Path));
            WaitUntil(() => Text(p).Contains("READY", StringComparison.Ordinal), "READY", p);

            Assert.Equal(ProcessState.Running, p.Snapshot.State);
            Assert.Equal(ProcessReadiness.None, p.Snapshot.Readiness);
            Assert.Null(p.Snapshot.ReadinessSince);

            Touch(dir, "g1");
            WaitForExited(p);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void Readiness_MatchesAnsiColoredLinesOnStdOutAndStdErr()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            string script = Prelude + """
                printf '\033[32mREADY\033[0m ansi\n'
                wait_for g1
                printf '\033[1mrebuild\033[0m\n'
                wait_for g2
                echo "READY on stderr" 1>&2
                wait_for g3
                exit 0
                """;
            p.Start(WithReady(Sh(script, dir.Path), "^READY", "rebuild"));

            WaitUntil(() => p.Snapshot.Readiness == ProcessReadiness.Ready, "Ready from coloured stdout", p);

            Touch(dir, "g1");
            WaitUntil(() => p.Snapshot.Readiness == ProcessReadiness.Starting, "Starting from coloured busy line", p);

            Touch(dir, "g2");
            WaitUntil(() => p.Snapshot.Readiness == ProcessReadiness.Ready, "Ready from stderr", p);

            Touch(dir, "g3");
            WaitForExited(p);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void Readiness_PartialLineDoesNotMatchUntilCompleted()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(WithReady(Sh(Prelude + "printf READY\nwait_for g1\necho\nwait_for g2\nexit 0", dir.Path), "READY"));
            WaitUntil(
                () => Lines(p).Any(l => l.Source == OutputSource.StdOut && l.PlainText == "READY"),
                "partial 'READY' to appear",
                p);

            // The partial line is visible but has not completed, so it cannot have matched.
            Assert.Equal(ProcessReadiness.Starting, p.Snapshot.Readiness);

            Touch(dir, "g1");
            WaitUntil(() => p.Snapshot.Readiness == ProcessReadiness.Ready, "Ready once the line completes", p);

            Touch(dir, "g2");
            WaitForExited(p);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void Readiness_BothPatternsOnOneLine_ReadyWins()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(WithReady(Sh(Prelude + "echo 'rebuild READY'\nwait_for g1\nexit 0", dir.Path), "READY", "rebuild"));
            WaitUntil(() => p.Snapshot.Readiness == ProcessReadiness.Ready, "Ready", p);

            Touch(dir, "g1");
            WaitForExited(p);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void Readiness_ConcurrentStdOutAndStdErrEndReady()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            // Both streams write a lot at once; the matcher must neither deadlock nor lose the final ready line.
            string script = """
                i=0
                while [ $i -lt 400 ]; do echo "noise $i"; echo "err $i" 1>&2; i=$((i+1)); done
                echo "READY at the end"
                wait_for() { while [ ! -f "$1" ]; do sleep 0.02; done; }
                wait_for g1
                exit 0
                """;
            p.Start(WithReady(Sh(script, dir.Path), "READY"));

            WaitUntil(() => p.Snapshot.Readiness == ProcessReadiness.Ready, "Ready after noisy output", p);
            Assert.Equal(ProcessReadiness.Ready, p.Snapshot.Readiness);

            Touch(dir, "g1");
            WaitForExited(p);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void Readiness_ForceKillReturnsToNone()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(WithReady(Sh("echo READY; while :; do sleep 0.1; done", dir.Path), "READY"));
            WaitUntil(() => p.Snapshot.Readiness == ProcessReadiness.Ready, "Ready", p);

            p.ForceKill();
            WaitForExited(p);

            Assert.Equal(ProcessReadiness.None, p.Snapshot.Readiness);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void InvalidPatternAtLaunch_IsIgnoredWithANotice()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            // Validation rejects this in the configuration; a launch that bypasses it must not crash the supervisor.
            p.Start(WithReady(Sh("echo hi; exit 0", dir.Path), "(unclosed"));
            WaitForExited(p);

            Assert.Equal(ProcessReadiness.None, p.Snapshot.Readiness);
            Assert.Contains(Lines(p), l => l.Source == OutputSource.System && l.PlainText.StartsWith("ready patterns ignored:", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(p);
        }
    }

    private static void Touch(TempDir dir, string name) => File.WriteAllText(dir.File(name), string.Empty);
}
