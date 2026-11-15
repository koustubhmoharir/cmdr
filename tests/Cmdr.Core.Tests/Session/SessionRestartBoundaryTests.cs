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
        CommandSpec old = Sh("old", $"trap '' INT; echo ready; while [ ! -f '{release}' ]; do sleep 0.05; done");
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
        CommandSpec old = Sh("old", $"trap '' INT; echo ready; while [ ! -f '{release}' ]; do sleep 0.05; done");
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
}
