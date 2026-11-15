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
/// A pending restart is owned by the session: Stop, ForceKill, StopAll, ForceKillAll and Dispose revoke it, and a cancelled
/// token stops it before it starts anything. Real processes (Linux only).
/// </summary>
public class SessionRestartOwnershipTests
{
    /// <summary>Ignores Ctrl+C and prints its pid once the trap is installed; loops until killed.</summary>
    private const string Stubborn = "trap '' INT; echo \"pid:$$\"; while :; do sleep 0.1; done";

    [Fact]
    public async Task ForceKillAll_RevokesPendingCommandRestart_NoRelaunch()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("stubborn", Stubborn)));
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { entry }));
            WaitForText(entry.Process, "pid:");

            Task<string?> restart = session.RestartAsync(new[] { entry });
            WaitUntil(() => entry.Process.Snapshot.State == ProcessState.Stopping, "restart stopping", entry.Process);

            session.ForceKillAll();

            await AssertCancelledAsync(restart);
            await Task.Delay(1000);
            Assert.Equal(1, PidCount(entry.Process));
            WaitUntil(() => session.ActiveEntries.Count == 0, "nothing active", entry.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task ForceKillAll_RevokesPendingGroupRestart_NoRelaunch()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("stubborn", Stubborn)));
        try
        {
            GroupEntry group = session.Groups[0];
            CommandEntry entry = group.Commands[0];
            Assert.Null(session.Activate(group));
            WaitForText(entry.Process, "pid:");

            Task<string?> restart = session.RestartAsync(group);
            WaitUntil(() => entry.Process.Snapshot.State == ProcessState.Stopping, "restart stopping", entry.Process);

            session.ForceKillAll();

            await AssertCancelledAsync(restart);
            await Task.Delay(1000);
            Assert.Equal(1, PidCount(entry.Process));
            WaitUntil(() => session.ActiveEntries.Count == 0, "nothing active", entry.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task StopAll_RevokesPendingRestart_EvenWhenProcessExitsLater()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string release = dir.File("release");
        // Ignores Ctrl+C and keeps running until the test releases it, so the restart waits for its exit.
        string script = $"trap '' INT; echo \"pid:$$\"; while [ ! -f '{release}' ] && [ -d '{dir.Path}' ]; do sleep 0.05; done";
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("gated", script)));
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { entry }));
            WaitForText(entry.Process, "pid:");

            Task<string?> restart = session.RestartAsync(new[] { entry });
            WaitUntil(() => entry.Process.Snapshot.State == ProcessState.Stopping, "restart stopping", entry.Process);

            session.StopAll();
            File.WriteAllText(release, "go");

            await AssertCancelledAsync(restart);
            await Task.Delay(1000);
            Assert.Equal(1, PidCount(entry.Process));
            WaitForExited(entry.Process);
            WaitUntil(() => session.ActiveEntries.Count == 0, "nothing active", entry.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task Dispose_RevokesPendingRestart_NoRelaunch()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string marker = dir.File("starts.txt");
        string release = dir.File("release");
        // Dispose does not kill on Linux, so the shell is released by the file at the end of the test.
        string script = $"trap '' INT; echo start >> '{marker}'; while [ ! -f '{release}' ] && [ -d '{dir.Path}' ]; do sleep 0.05; done";
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("gated", script)));
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { entry }));
            WaitUntil(() => StartCount(marker) == 1, "first start marker", entry.Process);

            Task<string?> restart = session.RestartAsync(new[] { entry });
            WaitUntil(() => entry.Process.Snapshot.State == ProcessState.Stopping, "restart stopping", entry.Process);

            session.Dispose();

            await AssertCancelledAsync(restart);
            await Task.Delay(1000);
            Assert.Equal(1, StartCount(marker));
        }
        finally
        {
            File.WriteAllText(release, "go");
            session.Dispose();
        }
    }

    [Fact]
    public async Task CancelledToken_CommandRestart_ThrowsAndNeverStartsCommand()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("idle", "echo idle")));
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await AssertCancelledAsync(session.RestartAsync(new[] { entry }, ct: cts.Token));

            Assert.Equal(ProcessState.NotStarted, entry.Process.Snapshot.State);
            Assert.Empty(session.ActiveEntries);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task CancelledToken_GroupRestart_ThrowsAndNeverStartsCommand()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("idle", "echo idle")));
        try
        {
            GroupEntry group = session.Groups[0];
            CommandEntry entry = group.Commands[0];
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await AssertCancelledAsync(session.RestartAsync(group, ct: cts.Token));

            Assert.Equal(ProcessState.NotStarted, entry.Process.Snapshot.State);
            Assert.Empty(session.ActiveEntries);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task CancelledToken_WhileStopDispatchBlocked_CompletesWithoutForceKill()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        // The child never reads stdin, so a stop input larger than the pipe buffer blocks the stop dispatch.
        CommandSpec spec = Sh("blocked", "echo ready; sleep 30");
        spec.Stop = new StopSpec { Method = StopMethod.Input, Input = new string('x', 2 * 1024 * 1024) };
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, spec));
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { entry }));
            WaitForText(entry.Process, "ready");

            using var cts = new CancellationTokenSource();
            Task<string?> restart = session.RestartAsync(new[] { entry }, ct: cts.Token);
            WaitUntil(() => entry.Process.Snapshot.State == ProcessState.Stopping, "stop dispatch blocked", entry.Process);

            cts.Cancel();

            await AssertCancelledAsync(restart, TimeSpan.FromSeconds(2));
            // Not force-killed: the leader is still there, stuck on its stdin write.
            Assert.Equal(ProcessState.Stopping, entry.Process.Snapshot.State);

            session.ForceKill(new[] { entry });
            WaitForExited(entry.Process);
            await Task.Delay(1000);
            Assert.Single(Regex.Matches(Text(entry.Process), "ready"));
            WaitUntil(() => session.ActiveEntries.Count == 0, "nothing active", entry.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task Stop_OfTargets_RevokesPendingRestart_NoRelaunch()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("stubborn", Stubborn)));
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { entry }));
            WaitForText(entry.Process, "pid:");

            Task<string?> restart = session.RestartAsync(new[] { entry });
            WaitUntil(() => entry.Process.Snapshot.State == ProcessState.Stopping, "restart stopping", entry.Process);

            Assert.Null(session.Stop(new[] { entry }));

            await AssertCancelledAsync(restart);
            await Task.Delay(1000);
            Assert.Equal(1, PidCount(entry.Process));
        }
        finally
        {
            Cleanup(session);
        }
    }

    /// <summary>Awaits a task that must end with <see cref="OperationCanceledException"/> within the timeout.</summary>
    private static async Task AssertCancelledAsync(Task task, TimeSpan? timeout = null)
    {
        Task assertion = Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await assertion.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
    }

    /// <summary>Number of pid markers the stubborn command printed: one per start.</summary>
    private static int PidCount(ManagedProcess process) => Regex.Matches(Text(process), @"pid:\d+").Count;

    /// <summary>Number of lines in the marker file, or zero before the first start.</summary>
    private static int StartCount(string marker) => File.Exists(marker) ? File.ReadAllLines(marker).Length : 0;
}
