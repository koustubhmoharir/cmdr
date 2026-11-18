namespace Logvu.Tui;

/// <summary>
/// The progress of a scan, reported to the UI at most once per interval: the first report at once, then the next one only when the
/// interval has passed since the last one that was passed on. It is called from the scan's thread, so the throttle is locked.
/// </summary>
internal sealed class ScanProgress : IProgress<long>
{
    private readonly Action<long> _report;
    private readonly long _intervalMs;
    private readonly Func<long> _clockMs;
    private readonly object _lock = new();
    private bool _reported;
    private long _lastMs;

    /// <summary>Creates the throttle.</summary>
    /// <param name="report">Receives each value that passes the throttle.</param>
    /// <param name="intervalMs">The least time between two values passed on, in milliseconds.</param>
    /// <param name="clockMs">The clock, in milliseconds, that the interval is measured on.</param>
    /// <exception cref="ArgumentNullException">A delegate is <c>null</c>.</exception>
    public ScanProgress(Action<long> report, long intervalMs, Func<long> clockMs)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(clockMs);
        _report = report;
        _intervalMs = intervalMs;
        _clockMs = clockMs;
    }

    /// <inheritdoc/>
    public void Report(long value)
    {
        long now = _clockMs();
        lock (_lock)
        {
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
