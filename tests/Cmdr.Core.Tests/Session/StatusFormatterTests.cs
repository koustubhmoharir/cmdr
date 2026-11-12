using System.Globalization;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;

namespace Cmdr.Core.Tests.Session;

public class StatusFormatterTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 11, 10, 42, 3, TimeSpan.FromHours(2));

    private static string LocalStart => Started.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static ProcessSnapshot Snap(
        ProcessState state,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? exitedAt = null,
        DateTimeOffset? stopRequestedAt = null,
        int? exitCode = null,
        int leftovers = 0,
        bool stopRequested = false,
        ProcessReadiness readiness = ProcessReadiness.None,
        DateTimeOffset? readinessSince = null) =>
        new(state, 42, startedAt, exitedAt, stopRequestedAt, exitCode, null, leftovers, stopRequested, readiness, readinessSince);

    [Fact]
    public void NotStarted_IsIdle()
    {
        StatusText text = StatusFormatter.Format(Snap(ProcessState.NotStarted), queued: false, Started);

        Assert.Equal(new StatusText("·", StatusKind.Idle, "—", string.Empty), text);
    }

    [Fact]
    public void Queued_OverridesIdleAndExitedButNotRunning()
    {
        Assert.Equal(new StatusText("…", StatusKind.Queued, "—", "queued"),
            StatusFormatter.Format(Snap(ProcessState.NotStarted), queued: true, Started));

        Assert.Equal(StatusKind.Queued,
            StatusFormatter.Format(Snap(ProcessState.Exited, Started, Started.AddSeconds(1), exitCode: 0), queued: true, Started).Kind);
    }

    [Fact]
    public void Queued_DoesNotOverrideRunningOrStopping()
    {
        Assert.Equal(StatusKind.Running, StatusFormatter.Format(Snap(ProcessState.Running, Started), queued: true, Started).Kind);
        Assert.Equal(StatusKind.Stopping,
            StatusFormatter.Format(Snap(ProcessState.Stopping, Started, stopRequestedAt: Started), queued: true, Started).Kind);
    }

    [Fact]
    public void Running_WithoutReadySpec_ShowsStartAndElapsed()
    {
        DateTimeOffset now = Started.AddMinutes(12).AddSeconds(51);

        StatusText text = StatusFormatter.Format(Snap(ProcessState.Running, Started), queued: false, now);

        Assert.Equal(new StatusText("⧗", StatusKind.Running, LocalStart, "12:51"), text);
    }

    [Fact]
    public void Running_Starting_ShowsTimeSinceStartingBegan()
    {
        // Started long ago, but the current starting phase began 7 seconds before "now".
        DateTimeOffset now = Started.AddMinutes(3);
        ProcessSnapshot snapshot = Snap(
            ProcessState.Running,
            Started,
            readiness: ProcessReadiness.Starting,
            readinessSince: now.AddSeconds(-7));

        StatusText text = StatusFormatter.Format(snapshot, queued: false, now);

        Assert.Equal(new StatusText("⧗", StatusKind.Starting, LocalStart, "starting 0:07"), text);
    }

    [Fact]
    public void Running_Ready_ShowsTotalRunningTime()
    {
        DateTimeOffset now = Started.AddMinutes(12).AddSeconds(51);
        ProcessSnapshot snapshot = Snap(
            ProcessState.Running,
            Started,
            readiness: ProcessReadiness.Ready,
            readinessSince: now.AddMinutes(-1));

        StatusText text = StatusFormatter.Format(snapshot, queued: false, now);

        Assert.Equal(new StatusText("●", StatusKind.Ready, LocalStart, "ready · 12:51"), text);
    }

    [Fact]
    public void Stopping_ShowsTimeSinceStopRequest()
    {
        ProcessSnapshot snapshot = Snap(ProcessState.Stopping, Started, stopRequestedAt: Started.AddSeconds(10));

        StatusText text = StatusFormatter.Format(snapshot, queued: false, Started.AddSeconds(13));

        Assert.Equal(new StatusText("◌", StatusKind.Stopping, LocalStart, "stopping 0:03"), text);
    }

    [Theory]
    [InlineData(ProcessReadiness.None)]
    [InlineData(ProcessReadiness.Starting)]
    [InlineData(ProcessReadiness.Ready)]
    public void Stopping_KeepsStoppingStatusWhateverTheReadiness(ProcessReadiness readiness)
    {
        ProcessSnapshot snapshot = Snap(
            ProcessState.Stopping,
            Started,
            stopRequestedAt: Started.AddSeconds(10),
            readiness: readiness,
            readinessSince: Started);

        StatusText text = StatusFormatter.Format(snapshot, queued: false, Started.AddSeconds(13));

        Assert.Equal(new StatusText("◌", StatusKind.Stopping, LocalStart, "stopping 0:03"), text);
    }

    [Fact]
    public void Exited_WithLeftovers_IsWarningWithExitCodeAndCount()
    {
        ProcessSnapshot snapshot = Snap(ProcessState.Exited, Started, Started.AddSeconds(5), exitCode: 1, leftovers: 2);

        StatusText text = StatusFormatter.Format(snapshot, queued: false, Started.AddMinutes(1));

        Assert.Equal(new StatusText("⚠", StatusKind.Warning, LocalStart, "exit 1 · 2 left"), text);
    }

    [Fact]
    public void Exited_ZeroCode_IsSuccessWithRuntime()
    {
        ProcessSnapshot snapshot = Snap(ProcessState.Exited, Started, Started.AddSeconds(5), exitCode: 0);

        StatusText text = StatusFormatter.Format(snapshot, queued: false, Started.AddMinutes(1));

        Assert.Equal(new StatusText("✓", StatusKind.Success, LocalStart, "exit 0 · 0:05"), text);
    }

    [Fact]
    public void Exited_NonZeroCode_IsFailureWithRuntime()
    {
        ProcessSnapshot snapshot = Snap(ProcessState.Exited, Started, Started.AddMinutes(2).AddSeconds(3), exitCode: 3);

        StatusText text = StatusFormatter.Format(snapshot, queued: false, Started.AddHours(1));

        Assert.Equal(new StatusText("✗", StatusKind.Failure, LocalStart, "exit 3 · 2:03"), text);
    }

    [Theory]
    [InlineData(255)]
    [InlineData(130)]
    [InlineData(-1073741510)]
    public void Exited_StoppedWithNonZeroCode_IsStoppedNotFailure(int exitCode)
    {
        ProcessSnapshot snapshot = Snap(ProcessState.Exited, Started, Started.AddMinutes(2).AddSeconds(3), exitCode: exitCode, stopRequested: true);

        StatusText text = StatusFormatter.Format(snapshot, queued: false, Started.AddHours(1));

        Assert.Equal(
            new StatusText("∎", StatusKind.Stopped, LocalStart, $"stopped · exit {exitCode} · 2:03"),
            text);
    }

    [Fact]
    public void Exited_StoppedWithZeroCode_IsStopped()
    {
        ProcessSnapshot snapshot = Snap(ProcessState.Exited, Started, Started.AddSeconds(5), exitCode: 0, stopRequested: true);

        StatusText text = StatusFormatter.Format(snapshot, queued: false, Started.AddMinutes(1));

        Assert.Equal(new StatusText("∎", StatusKind.Stopped, LocalStart, "stopped · exit 0 · 0:05"), text);
    }

    [Fact]
    public void Exited_StoppedWithLeftovers_IsStillWarning()
    {
        ProcessSnapshot snapshot = Snap(ProcessState.Exited, Started, Started.AddSeconds(5), exitCode: 255, leftovers: 1, stopRequested: true);

        StatusText text = StatusFormatter.Format(snapshot, queued: false, Started.AddMinutes(1));

        Assert.Equal(new StatusText("⚠", StatusKind.Warning, LocalStart, "exit 255 · 1 left"), text);
    }

    [Fact]
    public void FailedToStart_IsFailure()
    {
        StatusText text = StatusFormatter.Format(Snap(ProcessState.FailedToStart), queued: false, Started);

        Assert.Equal(new StatusText("✗", StatusKind.Failure, "—", "failed to start"), text);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(-5, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(59 * 60 + 59, "59:59")]
    [InlineData(60 * 60, "1:00:00")]
    [InlineData(23 * 3600 + 59 * 60 + 59, "23:59:59")]
    [InlineData(24 * 3600, "1d 00:00:00")]
    [InlineData(24 * 3600 + 3661, "1d 01:01:01")]
    public void Duration_FormatsAtBoundaries(long seconds, string expected)
    {
        Assert.Equal(expected, StatusFormatter.Duration(TimeSpan.FromSeconds(seconds)));
    }
}
