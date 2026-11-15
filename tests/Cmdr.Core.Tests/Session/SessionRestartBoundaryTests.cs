using System.Text.RegularExpressions;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>
/// The boundaries of a restart that is already starting things, or whose tree changed while it waited: revocation and the
/// caller's cancellation are checked before each start, cleanup does not wait for a graceful stop, edits invalidate a
/// restart of commands, and a group restart starts only the commands it was called for. Real processes (Linux only).
/// </summary>
public class SessionRestartBoundaryTests
{
    [Fact]
    public async Task CallerCancellationWhileStartingAParallelBatch_StartsNoMoreAndStopsWhatStarted()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sleeper("a"), Sleeper("b")));
        using var cts = new CancellationTokenSource();
        CommandEntry a = session.AllEntries[0];
        CommandEntry b = session.AllEntries[1];
        int fired = 0;
        session.EntryChanged += e =>
        {
            if (e == a && e.Process.Snapshot.State == ProcessState.Running && Interlocked.Exchange(ref fired, 1) == 0)
            {
                cts.Cancel();
            }
        };

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RestartAsync(new[] { a, b }, ct: cts.Token));

            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            WaitUntil(() => a.Process.Snapshot.State == ProcessState.Exited, "the started command to be stopped", a.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task RevocationWhileStartingAParallelBatch_StartsNoLaterTarget()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sleeper("a"), Sleeper("b")));
        CommandEntry a = session.AllEntries[0];
        CommandEntry b = session.AllEntries[1];
        int fired = 0;
        session.EntryChanged += e =>
        {
            if (e == a && e.Process.Snapshot.State == ProcessState.Running && Interlocked.Exchange(ref fired, 1) == 0)
            {
                session.ForceKillAll();
            }
        };

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RestartAsync(new[] { a, b }));

            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task CleanupAfterCancellation_DoesNotWaitForABlockedInputStop()
    {
        if (!OperatingSystem.IsLinux()) return;

        // The caller's token is cancelled as the command starts, so nothing else stops it: the restart's cleanup sends the
        // graceful stop itself. The stop input is more than a pipe holds and the command never reads stdin, so the write
        // blocks until the command dies. A cleanup that waited for it would never finish; one that does not finishes while
        // the command is still alive.
        using var dir = new TempDir();
        var spec = new CommandSpec
        {
            Name = "blocked",
            Program = "sleep",
            Args = new List<string> { "30" },
            Stop = new StopSpec { Method = StopMethod.Input, Input = new string('x', 2 * 1024 * 1024) },
        };
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, spec));
        CommandEntry a = session.AllEntries[0];
        using var cts = new CancellationTokenSource();
        int fired = 0;
        session.EntryChanged += e =>
        {
            if (e.Process.Snapshot.State == ProcessState.Running && Interlocked.Exchange(ref fired, 1) == 0)
            {
                cts.Cancel();
            }
        };

        Task<string?> restart = Task.Run(() => session.RestartAsync(new[] { a }, ct: cts.Token));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restart).WaitAsync(TimeSpan.FromSeconds(30));

            // The stop is sent in the background; it marks the command stopping, and its write stays blocked.
            WaitUntil(() => a.Process.Snapshot.State == ProcessState.Stopping, "the cleanup's stop to be requested", a.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task CommandMovedAndItsGroupRemovedWhileWaiting_RestartIsCancelledAndRegistersNoRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string release = dir.File("release");
        CommandSpec old = Sh("old", $"trap '' INT; echo ready; while [ ! -f '{release}' ] && [ -d '{dir.Path}' ]; do sleep 0.05; done");
        CmdrSession session = OpenWith(dir, Group("original", GroupMode.Sequential, old), Group("destination", GroupMode.Parallel));
        GroupEntry original = session.Groups[0];
        GroupEntry destination = session.Groups[1];
        CommandEntry a = original.Commands[0];
        try
        {
            Assert.Null(session.Activate(new[] { a }));
            WaitForText(a.Process, "ready");

            Task<string?> restart = session.RestartAsync(new[] { a });
            WaitUntil(
                () => a.Process.Snapshot.State == ProcessState.Stopping && !original.IsSequenceRunning,
                "the old run to be cancelled and the command stopping",
                a.Process);

            session.UpdateCommand(a, a.Spec, destination);
            session.RemoveGroup(original);
            File.WriteAllText(release, "go");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restart).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(original.IsSequenceRunning);
            Assert.Single(Regex.Matches(Text(a.Process), "ready"));
        }
        finally
        {
            File.WriteAllText(release, "go");
            Cleanup(session);
        }
    }

    [Fact]
    public async Task CommandAddedToTheGroupWhileWaiting_IsNotStartedByTheGroupRestart()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string release = dir.File("release");
        CommandSpec old = Sh("old", $"trap '' INT; echo ready; while [ ! -f '{release}' ] && [ -d '{dir.Path}' ]; do sleep 0.05; done");
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, old));
        GroupEntry group = session.Groups[0];
        CommandEntry a = group.Commands[0];
        try
        {
            Assert.Null(session.Activate(group));
            WaitForText(a.Process, "ready");

            int? firstPid = a.Process.Snapshot.Pid;
            Task<string?> restart = session.RestartAsync(group);
            WaitUntil(() => a.Process.Snapshot.State == ProcessState.Stopping, "the command to be stopping", a.Process);

            CommandEntry added = session.AddCommand(group, Sleeper("added"));
            File.WriteAllText(release, "go");

            Assert.Null(await restart.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(ProcessState.NotStarted, added.Process.Snapshot.State);
            Assert.NotEqual(firstPid, a.Process.Snapshot.Pid); // the original command was restarted
        }
        finally
        {
            File.WriteAllText(release, "go");
            Cleanup(session);
        }
    }

    [Fact]
    public async Task ForceKillForAPendingRestart_KeepsTheRestart_WhichStartsTheCommandAgain()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        // Ignores Ctrl+C, so the restart waits for it until it is killed.
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("stubborn", "trap '' INT; echo ready; while :; do sleep 0.1; done"), Sleeper("other")));
        CommandEntry stubborn = session.Groups[0].Commands[0];
        CommandEntry other = session.Groups[0].Commands[1];
        try
        {
            Assert.Null(session.Activate(new[] { stubborn }));
            WaitForText(stubborn.Process, "ready");
            int? firstPid = stubborn.Process.Snapshot.Pid;
            Assert.Empty(session.PendingRestartsOf(new[] { stubborn, other }));

            Task<string?> restart = session.RestartAsync(new[] { stubborn });
            WaitUntil(() => stubborn.Process.Snapshot.State == ProcessState.Stopping, "the restart to be stopping it", stubborn.Process);
            Assert.Equal(new[] { stubborn }, session.PendingRestartsOf(new[] { stubborn, other }).Select(p => p.Command));

            session.ForceKillForPendingRestart(session.PendingRestartsOf(new[] { stubborn }));

            Assert.Null(await restart.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(ProcessState.Running, stubborn.Process.Snapshot.State);
            Assert.NotEqual(firstPid, stubborn.Process.Snapshot.Pid);
            Assert.Empty(session.PendingRestartsOf(new[] { stubborn }));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task ForceKillForAPendingRestartTakenBeforeItsRunEnded_LeavesTheReplacementRunAlone()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        // Ignores Ctrl+C and keeps running until the temp dir is removed (or the release file appears), so the replacement
        // run stays alive after the restart has started it.
        string release = dir.File("release");
        CommandSpec stubborn = Sh("stubborn", $"trap '' INT; echo ready; while [ ! -f '{release}' ] && [ -d '{dir.Path}' ]; do sleep 0.05; done");
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, stubborn));
        CommandEntry entry = session.Groups[0].Commands[0];
        try
        {
            Assert.Null(session.Activate(new[] { entry }));
            WaitForText(entry.Process, "ready");
            int oldRunId = entry.Process.RunId;

            Task<string?> restart = session.RestartAsync(new[] { entry });
            WaitUntil(() => entry.Process.Snapshot.State == ProcessState.Stopping, "the restart to be stopping it", entry.Process);
            IReadOnlyList<PendingRestart> handles = Array.Empty<PendingRestart>();
            WaitUntil(
                () => (handles = session.PendingRestartsOf(new[] { entry })).Count == 1,
                "the restart to be pending for the command",
                entry.Process);
            Assert.Equal(oldRunId, handles[0].RunId);

            // The old run ends on its own terms, and the restart starts the replacement.
            entry.Process.ForceKill();
            Assert.Null(await restart.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(ProcessState.Running, entry.Process.Snapshot.State);
            int replacementRunId = entry.Process.RunId;
            Assert.NotEqual(oldRunId, replacementRunId);

            // The handle is stale: its restart has finished. Acting on it must not touch the replacement.
            session.ForceKillForPendingRestart(handles);
            await Task.Delay(500);

            Assert.Equal(ProcessState.Running, entry.Process.Snapshot.State);
            Assert.Equal(replacementRunId, entry.Process.RunId);
            Assert.False(entry.Process.Snapshot.StopRequested);
        }
        finally
        {
            File.WriteAllText(release, "go");
            Cleanup(session);
        }
    }

    [Fact]
    public async Task RestartWhileASequenceIsBeingLaunched_WaitsForTheRunToEnd_AndStartsTheCommand()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Sequential, Sh("a", "echo \"pid:$$\"; exec sleep 30")));
        GroupEntry group = session.Groups[0];
        CommandEntry a = group.Commands[0];
        using var released = new ManualResetEventSlim(false);
        int blocked = 0;
        session.EntryChanged += e =>
        {
            // Launch announces the planned commands after the run is registered and before the run is started. Holding the
            // activating thread here leaves the run registered and not yet running.
            if (e == a && e.IsQueued && Interlocked.Exchange(ref blocked, 1) == 0)
            {
                released.Wait(TimeSpan.FromSeconds(30));
            }
        };

        try
        {
            Task<string?> activation = Task.Run(() => session.Activate(group));
            WaitUntil(() => Volatile.Read(ref blocked) == 1, "the activation to be held while its run is registered");

            Task<string?> restart = Task.Run(() => session.RestartAsync(new[] { a }));
            WaitUntil(() => !a.IsQueued, "the restart to cancel the registered run");
            Thread.Sleep(200); // lets the restart reach the point where it looks at the group's active run
            released.Set();

            Assert.Null(await activation.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Null(await restart.WaitAsync(TimeSpan.FromSeconds(10)));
            WaitForText(a.Process, "pid:");
            Assert.Equal(ProcessState.Running, a.Process.Snapshot.State);
            Assert.Single(Regex.Matches(Text(a.Process), "pid:"));
        }
        finally
        {
            released.Set();
            Cleanup(session);
        }
    }

    [Fact]
    public async Task CallerCancelledBeforeTheFirstLaunch_RestartOfCommandsInASequence_StartsNothingAndThrows()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Sequential, Sleeper("a"), Sleeper("b")));
        GroupEntry group = session.Groups[0];
        CommandEntry a = group.Commands[0];
        CommandEntry b = group.Commands[1];
        using var cts = new CancellationTokenSource();
        // The planned commands are announced as queued after the run is registered and before it is launched.
        session.EntryChanged += e =>
        {
            if (e.IsQueued)
            {
                cts.Cancel();
            }
        };

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => session.RestartAsync(new[] { a, b }, ct: cts.Token))
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ProcessState.NotStarted, a.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.False(a.IsQueued);
            Assert.False(b.IsQueued);
            Assert.False(group.IsSequenceRunning);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task CallerCancelledBeforeTheFirstLaunch_RestartOfASequentialGroup_StartsNothingAndThrows()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Sequential, Sleeper("a"), Sleeper("b")));
        GroupEntry group = session.Groups[0];
        CommandEntry a = group.Commands[0];
        CommandEntry b = group.Commands[1];
        using var cts = new CancellationTokenSource();
        session.EntryChanged += e =>
        {
            if (e.IsQueued)
            {
                cts.Cancel();
            }
        };

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => session.RestartAsync(group, ct: cts.Token))
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ProcessState.NotStarted, a.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.False(a.IsQueued);
            Assert.False(b.IsQueued);
            Assert.False(group.IsSequenceRunning);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task CallerCancelledAfterTheFirstCommandStarted_DoesNotStopTheSequence()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Sequential, Sh("a", "echo a-done"), Sh("b", "echo b-ran; exec sleep 30")));
        GroupEntry group = session.Groups[0];
        CommandEntry a = group.Commands[0];
        CommandEntry b = group.Commands[1];
        using var cts = new CancellationTokenSource();
        int fired = 0;
        session.EntryChanged += e =>
        {
            // Runs inside the first launch, after the launch has been decided: the caller's cancellation comes too late.
            if (e == a && e.Process.Snapshot.State == ProcessState.Running && Interlocked.Exchange(ref fired, 1) == 0)
            {
                cts.Cancel();
            }
        };

        try
        {
            Assert.Null(await session.RestartAsync(new[] { a, b }, ct: cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));

            WaitForText(b.Process, "b-ran");
            Assert.Equal(ProcessState.Running, b.Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task CommandMovedOutOfAParallelBatchBetweenLaunches_IsNotStartedAndTheEarlierLaunchIsStoppedAgain()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sleeper("a"), Sleeper("b")), Group("destination", GroupMode.Parallel));
        GroupEntry destination = session.Groups[1];
        CommandEntry a = session.Groups[0].Commands[0];
        CommandEntry b = session.Groups[0].Commands[1];
        int fired = 0;
        session.EntryChanged += e =>
        {
            // Start's Running notification is raised on this thread, before the batch launches b.
            if (e == a && e.Process.Snapshot.State == ProcessState.Running && Interlocked.Exchange(ref fired, 1) == 0)
            {
                session.UpdateCommand(b, b.Spec, destination);
            }
        };

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => session.RestartAsync(new[] { a, b }))
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.Same(destination, b.Group);
            WaitUntil(() => a.Process.Snapshot.State == ProcessState.Exited, "the command launched by the batch to be stopped again", a.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task CommandOfAGroupMovedOutWhileTheRestartWaits_CancelsTheRestart()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string release = dir.File("release");
        CommandSpec old = Sh("old", $"trap '' INT; echo ready; while [ ! -f '{release}' ] && [ -d '{dir.Path}' ]; do sleep 0.05; done");
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, old, Sleeper("b")), Group("destination", GroupMode.Parallel));
        GroupEntry group = session.Groups[0];
        GroupEntry destination = session.Groups[1];
        CommandEntry a = group.Commands[0];
        CommandEntry b = group.Commands[1];
        try
        {
            Assert.Null(session.Activate(new[] { a }));
            WaitForText(a.Process, "ready");

            Task<string?> restart = session.RestartAsync(group);
            WaitUntil(() => a.Process.Snapshot.State == ProcessState.Stopping, "the restart to be stopping a", a.Process);

            session.UpdateCommand(b, b.Spec, destination);
            File.WriteAllText(release, "go");

            await Assert.ThrowsAsync<OperationCanceledException>(() => restart).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            WaitUntil(() => a.Process.Snapshot.State == ProcessState.Exited, "the old run to end", a.Process);
            Assert.Single(Regex.Matches(Text(a.Process), "ready"));
        }
        finally
        {
            File.WriteAllText(release, "go");
            Cleanup(session);
        }
    }
}
