using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Cmdr.Core.Processes;

/// <summary>
/// The leader started by <see cref="WindowsProcessPlatform.Launch"/>: its process handle, its id, the server ends of its
/// redirected pipes, and its exit.
/// </summary>
/// <remarks>
/// The exit is detected by a thread-pool wait on the process handle. The wait is registered only by
/// <see cref="BeginExitTracking"/>, which the launch calls as its last step, so a launch that fails never reports an exit.
/// The callback runs once, on a thread-pool thread, outside this object's lock.
/// <para>
/// Owns the process handle only. The streams belong to the caller (ManagedProcess disposes them through its output pumps), so
/// <see cref="Dispose"/> does not close them, as <see cref="System.Diagnostics.Process.Dispose()"/> does not close redirected streams.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsLaunchedProcess : ILaunchedProcess
{
    private readonly object _gate = new();
    private readonly SafeProcessHandle _process;
    private readonly int _id;
    private readonly StreamReader _stdout;
    private readonly StreamReader _stderr;
    private readonly StreamWriter? _stdin;
    private readonly ExitWaitHandle _exitWait;

    // Guarded by _gate.
    private Action<ILaunchedProcess>? _onExited;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    // Written once by the exit callback, before _exited is set.
    private int _exitRecorded;
    private int _exitCode;
    private volatile bool _exited;

    /// <summary>Wraps a started process. Takes ownership of <paramref name="process"/>; exit tracking does not begin until <see cref="BeginExitTracking"/>.</summary>
    internal WindowsLaunchedProcess(
        SafeProcessHandle process,
        int id,
        StreamReader stdout,
        StreamReader stderr,
        StreamWriter? stdin)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        _process = process;
        _id = id;
        _stdout = stdout;
        _stderr = stderr;
        _stdin = stdin;
        _exitWait = new ExitWaitHandle(process.DangerousGetHandle());
    }

    /// <inheritdoc />
    public int Id => _id;

    /// <inheritdoc />
    public StreamReader StandardOutput => _stdout;

    /// <inheritdoc />
    public StreamReader StandardError => _stderr;

    /// <inheritdoc />
    /// <remarks>Null when stdin is the console (not redirected).</remarks>
    public StreamWriter? StandardInput => _stdin;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The process has not exited yet.</exception>
    public int ExitCode
    {
        get
        {
            if (!_exited)
            {
                throw new InvalidOperationException("The process has not exited yet.");
            }

            return _exitCode;
        }
    }

    /// <summary>
    /// Starts watching for the exit. <paramref name="onExited"/> is invoked once, after the exit code is recorded. It may run
    /// before this method returns.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The process has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Exit tracking has already begun.</exception>
    internal void BeginExitTracking(Action<ILaunchedProcess> onExited)
    {
        ArgumentNullException.ThrowIfNull(onExited);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_registration is not null)
            {
                throw new InvalidOperationException("Exit tracking has already begun.");
            }

            _onExited = onExited;
            _registration = ThreadPool.RegisterWaitForSingleObject(
                _exitWait,
                static (state, _) => ((WindowsLaunchedProcess)state!).OnSignaled(),
                this,
                Timeout.Infinite,
                executeOnlyOnce: true);
        }
    }

    /// <summary>
    /// Stops exit tracking and closes the process handle. Disposing before the exit was reported cancels the exit callback: a later
    /// callback sees that the object is disposed and does nothing. A callback that had already started may still invoke
    /// onExited, after this returns. Does not close the streams.
    /// </summary>
    public void Dispose()
    {
        RegisteredWaitHandle? registration;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            registration = _registration;
            _registration = null;
        }

        // Unregister without waiting: the exit callback takes _gate and this method must not block on it.
        registration?.Unregister(null);
        _exitWait.Dispose();
        _process.Dispose();
    }

    /// <summary>Thread-pool callback: records the exit code, then reports the exit exactly once.</summary>
    private void OnSignaled()
    {
        Action<ILaunchedProcess>? onExited;
        lock (_gate)
        {
            if (_disposed || Interlocked.Exchange(ref _exitRecorded, 1) != 0)
            {
                return;
            }

            _exitCode = ReadExitCode();
            _exited = true;
            onExited = _onExited;
        }

        onExited?.Invoke(this);
    }

    /// <summary>
    /// Reads the exit code of the signalled process. Caller holds <see cref="_gate"/> and has checked that the handle is open.
    /// A failure would leave the code unknown; it is reported as -1 because throwing here would crash the thread-pool thread.
    /// The handle was valid a moment ago and the wait has fired, so this is not expected.
    /// </summary>
    private int ReadExitCode()
    {
        if (WindowsNative.GetExitCodeProcess(_process.DangerousGetHandle(), out uint code) == 0)
        {
            return -1;
        }

        return unchecked((int)code);
    }

    /// <summary>
    /// A wait handle over the process handle that does not own it: the wait is on the process object, and <see cref="_process"/>
    /// closes the handle.
    /// </summary>
    private sealed class ExitWaitHandle : WaitHandle
    {
        internal ExitWaitHandle(nint processHandle)
        {
            SafeWaitHandle = new SafeWaitHandle(processHandle, ownsHandle: false);
        }
    }
}
