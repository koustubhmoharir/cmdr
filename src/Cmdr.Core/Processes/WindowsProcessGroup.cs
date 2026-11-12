using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Cmdr.Core.Processes;

/// <summary>
/// A job object on Windows that holds the leader and every process it spawned. The job is created with
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>, so <see cref="Dispose"/> closes the handle and kills any processes still in the job.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsProcessGroup : IProcessGroup
{
    private const int HelperTimeoutMilliseconds = 5000;
    private const int ErrorTimeout = 1460; // ERROR_TIMEOUT
    private const uint TerminateExitCode = 1;

    private readonly SafeJobHandle _job;
    private readonly int _leaderPid;
    private readonly CtrlCHelperCommand _helper;

    /// <summary>Wraps an existing job; takes ownership of <paramref name="job"/>.</summary>
    internal WindowsProcessGroup(SafeJobHandle job, int leaderPid, CtrlCHelperCommand helper)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(helper);
        _job = job;
        _leaderPid = leaderPid;
        _helper = helper;
    }

    /// <inheritdoc />
    /// <remarks>Starts the Ctrl+C helper for the leader's console and waits up to 5 seconds for it to finish.</remarks>
    /// <exception cref="Win32Exception">The helper could not be started, timed out, or reported a failure.</exception>
    public void Interrupt()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _helper.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (string arg in _helper.ArgumentsFor(_leaderPid))
        {
            psi.ArgumentList.Add(arg);
        }

        using Process helper = Process.Start(psi) ?? throw new Win32Exception("Could not start the Ctrl+C helper.");
        Task<string> stdErrText = helper.StandardError.ReadToEndAsync();

        if (!helper.WaitForExit(HelperTimeoutMilliseconds))
        {
            try
            {
                helper.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Exited in the meantime.
            }

            throw new Win32Exception(ErrorTimeout, "Ctrl+C helper timed out.");
        }

        string text = stdErrText.Wait(TimeSpan.FromSeconds(1)) ? stdErrText.Result.Trim() : string.Empty;
        int exitCode = helper.ExitCode;
        if (exitCode != 0)
        {
            throw new Win32Exception(exitCode, $"Ctrl+C helper failed: {text}");
        }
    }

    /// <inheritdoc />
    /// <remarks>Terminates every process in the job with exit code 1. Ignored if the job is already closed.</remarks>
    /// <exception cref="Win32Exception">TerminateJobObject failed.</exception>
    public void Kill()
    {
        try
        {
            if (UseJob(job => WindowsNative.TerminateJobObject(job, TerminateExitCode)) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        catch (ObjectDisposedException)
        {
            // Already closed: nothing left to terminate.
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Clears KILL_ON_JOB_CLOSE, so a later Dispose closes the job handle without terminating its processes. Ignored if
    /// the job is already closed.
    /// </remarks>
    /// <exception cref="Win32Exception">SetInformationJobObject failed.</exception>
    public void Detach()
    {
        try
        {
            // A zeroed limit struct sets no limits: the job was created with KILL_ON_JOB_CLOSE as its only limit.
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            uint size = (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            if (UseJob(job => WindowsNative.SetInformationJobObject(
                job, WindowsNative.JobObjectExtendedLimitInformation, ref info, size)) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        catch (ObjectDisposedException)
        {
            // Already closed: nothing to detach from.
        }
    }

    /// <inheritdoc />
    /// <remarks>Reads ActiveProcesses from the job's basic accounting information. Returns 0 once disposed.</remarks>
    /// <exception cref="Win32Exception">QueryInformationJobObject failed.</exception>
    public int CountLiveProcesses()
    {
        try
        {
            var info = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
            uint size = (uint)Marshal.SizeOf<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>();
            int ok = UseJob(job => WindowsNative.QueryInformationJobObject(
                job, WindowsNative.JobObjectBasicAccountingInformation, ref info, size, 0));
            if (ok == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return checked((int)info.ActiveProcesses);
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Closes the job handle. Because the job has KILL_ON_JOB_CLOSE, any process still in it is terminated: this is intentional,
    /// so that disposing a supervisor (or cmdr exiting) never leaves a tree running.
    /// </summary>
    public void Dispose() => _job.Dispose();

    /// <summary>
    /// Runs <paramref name="call"/> with the raw job handle, holding a reference so a concurrent Dispose cannot close it mid-call.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The job handle has been closed.</exception>
    private int UseJob(Func<nint, int> call)
    {
        bool added = false;
        try
        {
            _job.DangerousAddRef(ref added);
            return call(_job.DangerousGetHandle());
        }
        finally
        {
            if (added)
            {
                _job.DangerousRelease();
            }
        }
    }
}
