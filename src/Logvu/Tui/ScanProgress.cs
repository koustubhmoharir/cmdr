namespace Logvu.Tui;

/// <summary>
/// The progress of a scan, reported to the UI once the scan has run for a delay, and then at most once per interval: the first report at
/// or after the delay at once, then the next one only when the interval has passed since the last one that was passed on. Reports before
/// the delay are dropped. The delay is measured from the clock's value when the throttle is created. It is called from the scan's thread,
/// so the throttle is locked.
/// </summary>
internal sealed class ScanProgress : IProgress<long>
{
    private readonly Action<long> _report;
    private readonly long _delayMs;
    private readonly long _intervalMs;
    private readonly Func<long> _clockMs;
    private readonly long _startMs;
    private readonly object _lock = new();
    private bool _reported;
    private long _lastMs;

    /// <summary>Creates the throttle, and starts the delay now.</summary>
    /// <param name="report">Receives each value that passes the throttle.</param>
    /// <param name="delayMs">The time from now before the first value is passed on, in milliseconds. Zero passes values from the start.</param>
    /// <param name="intervalMs">The least time between two values passed on, in milliseconds.</param>
    /// <param name="clockMs">The clock, in milliseconds, that the delay and the interval are measured on.</param>
    /// <exception cref="ArgumentNullException">A delegate is <c>null</c>.</exception>
    public ScanProgress(Action<long> report, long delayMs, long intervalMs, Func<long> clockMs)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(clockMs);
        _report = report;
        _delayMs = delayMs;
        _intervalMs = intervalMs;
        _clockMs = clockMs;
        _startMs = clockMs();
    }

    /// <inheritdoc/>
    public void Report(long value)
    {
        long now = _clockMs();
        lock (_lock)
        {
            if (now - _startMs < _delayMs)
            {
                return;
            }

            if (_reported && now - _lastMs < _intervalMs)
            {
                return;
            }

            _reported = true;
            _lastMs = now;
        }

        _report(value);
    }
}
