using System.Collections.Concurrent;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>Session process actions against real processes (Linux only).</summary>
public class SessionProcessTests
{
    [Fact]
    public void Activate_ParallelGroup_StartsAllCommands()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "exit 0"), Sh("b", "exit 0")));
        try
        {
            CommandEntry[] entries = session.Groups[0].Commands.ToArray();

            Assert.Null(session.Activate(entries));

            foreach (CommandEntry entry in entries)
            {
                WaitForExited(entry.Process);
                Assert.Equal(0, entry.Process.Snapshot.ExitCode);
            }
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_WithStoppingTarget_OnlyKillsAndStartsNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group(
            "g",
            GroupMode.Parallel,
            Sh("stubborn", "trap '' INT; echo ready; while :; do sleep 0.1; done"),
            Sh("idle", "exit 0")));
        try
        {
            CommandEntry stubborn = session.Groups[0].Commands[0];
            CommandEntry idle = session.Groups[0].Commands[1];

            Assert.Null(session.Activate(new[] { stubborn }));
            WaitUntil(() => stubborn.Process.Snapshot.State == ProcessState.Running, "stubborn running", stubborn.Process);
            // Running is set as soon as the process starts; wait for the trap to be installed before sending Ctrl+C.
            WaitUntil(() => Text(stubborn.Process).Contains("ready", StringComparison.Ordinal), "shell ready", stubborn.Process);
            Assert.Null(session.Stop(new[] { stubborn }));
            WaitUntil(() => stubborn.Process.Snapshot.State == ProcessState.Stopping, "stubborn stopping", stubborn.Process);

            Assert.Null(session.Activate(new[] { stubborn, idle }));

            WaitForExited(stubborn.Process);
            Assert.Equal(ProcessState.NotStarted, idle.Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_SequentialGroup_RunsCommandsInOrderAndClearsQueue()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group(
            "build",
            GroupMode.Sequential,
            Sh("a", "echo a; exit 0"),
            Sh("b", "echo b; exit 0"),
            Sh("c", "echo c; exit 0")));
        try
        {
            GroupEntry group = session.Groups[0];
            CommandEntry a = group.Commands[0];
            CommandEntry b = group.Commands[1];
            CommandEntry c = group.Commands[2];

            Assert.Null(session.Activate(group.Commands));

            WaitUntil(() => !group.IsSequenceRunning, "sequence to finish", a.Process);
            foreach (CommandEntry entry in new[] { a, b, c })
            {
                WaitForExited(entry.Process);
                Assert.Equal(0, entry.Process.Snapshot.ExitCode);
                Assert.False(entry.IsQueued);
            }

            ProcessSnapshot sa = a.Process.Snapshot;
            ProcessSnapshot sb = b.Process.Snapshot;
            ProcessSnapshot sc = c.Process.Snapshot;
            Assert.True(sa.StartedAt < sb.StartedAt);
            Assert.True(sb.StartedAt < sc.StartedAt);
            Assert.True(sb.StartedAt >= sa.ExitedAt);
            Assert.True(sc.StartedAt >= sb.ExitedAt);
            Assert.Contains("b", Text(b.Process), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_SequentialGroup_FailureCancelsRest()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group(
            "build",
            GroupMode.Sequential,
            Sh("first", "exit 0"),
            Sh("second", "exit 3"),
            Sh("third", "echo never; exit 0")));
        try
        {
            GroupEntry group = session.Groups[0];
            CommandEntry second = group.Commands[1];
            CommandEntry third = group.Commands[2];

            Assert.Null(session.Activate(group.Commands));

            WaitUntil(() => !group.IsSequenceRunning, "sequence to end", second.Process);
            WaitForExited(second.Process);
            Assert.Equal(3, second.Process.Snapshot.ExitCode);
            WaitForText(second.Process, "sequence cancelled: exit code 3");
            Assert.Contains("sequence cancelled: exit code 3", Text(second.Process), StringComparison.Ordinal);

            Assert.Equal(ProcessState.NotStarted, third.Process.Snapshot.State);
            Assert.False(third.IsQueued);
            Assert.False(third.IsActive);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Stop_DuringSequence_StopsCurrentAndCancelsQueue()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group(
            "build",
            GroupMode.Sequential,
            Sleeper("first"),
            Sh("second", "echo second; exit 0"),
            Sh("third", "echo third; exit 0")));
        try
        {
            GroupEntry group = session.Groups[0];
            CommandEntry first = group.Commands[0];
            CommandEntry second = group.Commands[1];
            CommandEntry third = group.Commands[2];

            Assert.Null(session.Activate(group.Commands));
            WaitUntil(() => first.Process.Snapshot.State == ProcessState.Running, "first running", first.Process);
            Assert.True(second.IsQueued);
            Assert.True(third.IsQueued);

            Assert.Null(session.Stop(new[] { first }));

            WaitUntil(() => !group.IsSequenceRunning, "sequence cancelled", first.Process);

            WaitUntil(() => first.Process.Snapshot.State == ProcessState.Exited, "first stopped", first.Process);
            Assert.Equal(ProcessState.NotStarted, second.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, third.Process.Snapshot.State);
            Assert.False(second.IsQueued);
            Assert.False(third.IsQueued);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_BusySequentialGroup_ReturnsMessage()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("build", GroupMode.Sequential, Sleeper("loop"), Sh("other", "exit 0")));
        try
        {
            GroupEntry group = session.Groups[0];
            CommandEntry loop = group.Commands[0];
            CommandEntry other = group.Commands[1];
            Assert.Null(session.Activate(new[] { loop }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            string? message = session.Activate(new[] { other });

            Assert.Equal("A sequence is already running in group 'build'.", message);
            Assert.Equal(ProcessState.NotStarted, other.Process.Snapshot.State);

            session.Stop(new[] { loop });
            WaitUntil(() => !group.IsSequenceRunning, "sequence cancelled", loop.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task RestartAsync_ParallelRunningCommand_StartsNewRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sleeper("loop")));
        try
        {
            CommandEntry loop = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { loop }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);
            int firstPid = loop.Process.Snapshot.Pid!.Value;

            string? message = await session.RestartAsync(new[] { loop });

            Assert.Null(message);
            WaitUntil(
                () => loop.Process.Snapshot.State == ProcessState.Running && loop.Process.Snapshot.Pid != firstPid,
                "restarted loop running",
                loop.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_StoppingCommand_ForceKills()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        // Ignores Ctrl+C, so a graceful stop leaves it stopping until it is force-killed.
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("stubborn", "trap '' INT; echo ready; while :; do sleep 0.1; done")));
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { entry }));

            // Wait until the trap is installed; an earlier Ctrl+C would still kill the shell.
            WaitUntil(() => Text(entry.Process).Contains("ready", StringComparison.Ordinal), "shell ready", entry.Process);

            session.Stop(new[] { entry });
            WaitUntil(() => entry.Process.Snapshot.State == ProcessState.Stopping, "stopping", entry.Process);

            Assert.Null(session.Activate(new[] { entry }));
            WaitUntil(() => entry.Process.Snapshot.State == ProcessState.Exited, "force killed", entry.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void ToggleLog_WritesDefaultLogPath_AndDoesNotDirty()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("build", GroupMode.Parallel, Sh("compile", "echo compiled; exit 0")));
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];
            string logPath = dir.File(Path.Combine("logs", "build", "compile.log"));

            session.ToggleLog(entry);

            Assert.True(entry.Process.Output.IsLogging);
            Assert.True(File.Exists(logPath));
            Assert.False(session.IsDirty);
            Assert.Null(entry.Spec.Log.Path);
            Assert.False(entry.Spec.Log.Enabled);

            session.ToggleLog(entry);
            Assert.False(entry.Process.Output.IsLogging);
            Assert.False(session.IsDirty);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void LogEnabledInSpec_LogsOnStart()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CommandSpec spec = Sh("hello", "echo hello; exit 0");
        spec.Log.Enabled = true;
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, spec));
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];

            session.Activate(new[] { entry });
            WaitForExited(entry.Process);

            string logPath = dir.File(Path.Combine("logs", "g", "hello.log"));
            WaitUntil(() => File.Exists(logPath) && File.ReadAllText(logPath).Contains("hello", StringComparison.Ordinal), "log to contain output", entry.Process);
            string logText = File.ReadAllText(logPath);
            Assert.Contains("hello", logText, StringComparison.Ordinal);
            Assert.Contains("=== exited with code 0 ===", logText, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void EntryChanged_IsRaisedForProcessChanges()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "exit 0")));
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];
            var raised = new ConcurrentQueue<CommandEntry>();
            session.EntryChanged += raised.Enqueue;

            session.Activate(new[] { entry });
            WaitForExited(entry.Process);

            WaitUntil(() => raised.Contains(entry), "EntryChanged for the command", entry.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void ForceKillAll_StopsEverythingAndClearsActive()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sleeper("a"), Sleeper("b")));
        try
        {
            CommandEntry[] entries = session.Groups[0].Commands.ToArray();
            session.Activate(entries);
            WaitUntil(() => entries.All(e => e.Process.Snapshot.State == ProcessState.Running), "both running", entries[0].Process);
            Assert.Equal(2, session.ActiveEntries.Count);

            session.ForceKillAll();

            WaitUntil(() => session.ActiveEntries.Count == 0, "all killed", entries[0].Process);
        }
        finally
        {
            Cleanup(session);
        }
    }
}
