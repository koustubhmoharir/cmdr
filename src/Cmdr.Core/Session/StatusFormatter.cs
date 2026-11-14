using System.Globalization;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

/// <summary>Kind of status shown for a command. The UI maps each kind to a color.</summary>
public enum StatusKind
{
    /// <summary>Never started.</summary>
    Idle,

    /// <summary>Waiting in a sequence.</summary>
    Queued,

    /// <summary>Leader is running.</summary>
    Running,

    /// <summary>A graceful stop was requested.</summary>
    Stopping,

    /// <summary>Exited with code 0.</summary>
    Success,

    /// <summary>Exited with a non-zero code, or failed to start.</summary>
    Failure,

    /// <summary>Exited, but processes from the run are still alive.</summary>
    Warning,

    /// <summary>Exited after a graceful stop or a force kill was requested. The exit code does not decide success.</summary>
    Stopped,

    /// <summary>Running, and the command's ready pattern has not matched yet (or a busy pattern matched last).</summary>
    Starting,

    /// <summary>Running, and the command's ready pattern matched last.</summary>
    Ready,
}

/// <summary>The text shown for a command in the list.</summary>
/// <param name="Glyph">Single status glyph.</param>
/// <param name="Kind">Status kind, for coloring.</param>
/// <param name="Started">Local start time as <c>HH:mm:ss</c>, or an em dash.</param>
/// <param name="Detail">Elapsed time, stop or exit information.</param>
public readonly record struct StatusText(string Glyph, StatusKind Kind, string Started, string Detail);

/// <summary>Turns process snapshots into the status text of the command list.</summary>
public static class StatusFormatter
{
    private const string NoValue = "—";

    /// <summary>
    /// Formats the status of a command. A running or stopping process is shown as such even if it is queued;
    /// otherwise a queued command shows as queued.
    /// </summary>
    /// <param name="snapshot">The process snapshot.</param>
    /// <param name="queued">Whether the command waits in a sequence.</param>
    /// <param name="now">The current time, used for elapsed durations.</param>
    public static StatusText Format(ProcessSnapshot snapshot, bool queued, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string started = snapshot.StartedAt is { } startedAt
            ? startedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : NoValue;

        switch (snapshot.State)
        {
            case ProcessState.Running:
                return snapshot.Readiness switch
                {
                    ProcessReadiness.Starting => new StatusText(
                        "⧗",
                        StatusKind.Starting,
                        started,
                        FormattableString.Invariant($"starting {Duration(Between(snapshot.ReadinessSince, now))}")),
                    ProcessReadiness.Ready => new StatusText(
                        "●",
                        StatusKind.Ready,
                        started,
                        FormattableString.Invariant($"ready · {Duration(Between(snapshot.StartedAt, now))}")),
                    _ => new StatusText("⧗", StatusKind.Running, started, Duration(Between(snapshot.StartedAt, now))),
                };

            case ProcessState.Stopping:
                return new StatusText(
                    "◌",
                    StatusKind.Stopping,
                    started,
                    FormattableString.Invariant($"stopping {Duration(Between(snapshot.StopRequestedAt, now))}"));
        }

        if (queued)
        {
            return new StatusText("…", StatusKind.Queued, NoValue, "queued");
        }

        switch (snapshot.State)
        {
            case ProcessState.NotStarted:
                return new StatusText("·", StatusKind.Idle, NoValue, string.Empty);

            case ProcessState.FailedToStart:
                return new StatusText("✗", StatusKind.Failure, NoValue, "failed to start");

            case ProcessState.Exited when snapshot.HasLeftovers:
                return new StatusText(
                    "⚠",
                    StatusKind.Warning,
                    started,
                    FormattableString.Invariant(
                        $"exit {ExitCodeText(snapshot)} · {(snapshot.LeftoverCountUnknown ? "?" : snapshot.LeftoverCount)} left"));

            case ProcessState.Exited when snapshot.StopRequested:
                return new StatusText(
                    "∎",
                    StatusKind.Stopped,
                    started,
                    FormattableString.Invariant(
                        $"stopped · exit {ExitCodeText(snapshot)} · {Duration(Span(snapshot.StartedAt, snapshot.ExitedAt))}"));

            case ProcessState.Exited:
                string code = ExitCodeText(snapshot);
                string runtime = Duration(Span(snapshot.StartedAt, snapshot.ExitedAt));
                return snapshot.ExitCode == 0
                    ? new StatusText("✓", StatusKind.Success, started, $"exit {code} · {runtime}")
                    : new StatusText("✗", StatusKind.Failure, started, $"exit {code} · {runtime}");

            default:
                throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.State, "Unknown process state.");
        }
    }

    /// <summary>
    /// Formats a duration: negative values become zero; under an hour <c>m:ss</c>; under a day <c>h:mm:ss</c>;
    /// otherwise <c>{days}d hh:mm:ss</c>.
    /// </summary>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalDays >= 1)
        {
            return FormattableString.Invariant($"{span.Days}d {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}");
        }

        if (span.TotalHours >= 1)
        {
            return FormattableString.Invariant($"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}");
        }

        return FormattableString.Invariant($"{span.Minutes}:{span.Seconds:00}");
    }

    private static TimeSpan Between(DateTimeOffset? start, DateTimeOffset now) =>
        start is { } s ? now - s : TimeSpan.Zero;

    private static TimeSpan Span(DateTimeOffset? start, DateTimeOffset? end) =>
        start is { } s && end is { } e ? e - s : TimeSpan.Zero;

    private static string ExitCodeText(ProcessSnapshot snapshot) =>
        snapshot.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "?";
}
