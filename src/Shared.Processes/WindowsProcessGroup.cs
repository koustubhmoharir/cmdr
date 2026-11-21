using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Shared.Processes;

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

    /// <summary>The console host's image path, as QueryFullProcessImageNameW reports it (the system directory is System32 even for a 32-bit process).</summary>
    private static readonly string s_consoleHostPath = Path.Combine(Environment.SystemDirectory, "conhost.exe");

    private readonly SafeJobHandle _job;
    private readonly int _leaderPid;
    private readonly CtrlCHelperCommand _helper;
    private volatile bool _detached;

    /// <summary>The job object handle, for tests that check membership. Valid until the group is disposed.</summary>
    internal SafeJobHandle Job => _job;

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
    /// <remarks>Starts the Ctrl+C helper for the leader's console and waits up to 5 seconds for it to finish. Does nothing after <see cref="Detach"/>.</remarks>
    /// <exception cref="Win32Exception">The helper could not be started, timed out, or reported a failure.</exception>
    public void Interrupt()
    {
        if (_detached)
        {
            return;
        }

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

        // Only the start is serialized: Process.Start inherits every inheritable handle, so it must not run while a launch has
        // its pipe client ends inheritable (see WindowsProcessPlatform.InheritableHandleLock). The wait below is not locked.
        Process? started;
        lock (WindowsProcessPlatform.InheritableHandleLock)
        {
            started = Process.Start(psi);
        }

        using Process helper = started ?? throw new Win32Exception("Could not start the Ctrl+C helper.");
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
    /// <remarks>Terminates every process in the job with exit code 1. Ignored if the job is already closed or after <see cref="Detach"/>.</remarks>
    /// <exception cref="Win32Exception">TerminateJobObject failed.</exception>
    public void Kill()
    {
        if (_detached)
        {
            return;
        }

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
    /// the job is already closed. From the start of the call, Interrupt and Kill do nothing.
    /// </remarks>
    /// <exception cref="Win32Exception">SetInformationJobObject failed.</exception>
    public void Detach()
    {
        _detached = true;
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
    /// <remarks>
    /// Lists the job's process ids and counts them, leaving out console hosts (the system's <c>conhost.exe</c>). The child is created inside
    /// the job, so the hidden console's host process is created in the job too. It is not a process the command started: it
    /// exits on its own once its last client detaches, which is shortly after the leader exits, so counting it would report a
    /// phantom leftover for every command. A console host serving real leftovers is not needed in the count either, because
    /// its clients are counted. Returns 0 once disposed.
    /// </remarks>
    /// <exception cref="Win32Exception">QueryInformationJobObject failed.</exception>
    public int CountLiveProcesses()
    {
        try
        {
            int count = 0;
            foreach (uint pid in ListProcessIds())
            {
                if (!IsConsoleHost(pid))
                {
                    count++;
                }
            }

            return count;
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

    /// <summary>The ids of the processes currently in the job (JobObjectBasicProcessIdList), growing the buffer until they fit.</summary>
    /// <exception cref="Win32Exception">QueryInformationJobObject failed.</exception>
    /// <exception cref="ObjectDisposedException">The job handle has been closed.</exception>
    private List<uint> ListProcessIds()
    {
        // JOBOBJECT_BASIC_PROCESS_ID_LIST: two DWORDs (assigned, in list), then ULONG_PTR ids starting at offset 8.
        const int header = 8;
        int capacity = 32;
        while (true)
        {
            int bytes = header + capacity * nint.Size;
            nint buffer = Marshal.AllocHGlobal(bytes);
            try
            {
                int error = 0;
                int ok = UseJob(job =>
                {
                    int result = WindowsNative.QueryInformationJobObject(
                        job, WindowsNative.JobObjectBasicProcessIdList, buffer, (uint)bytes, 0);
                    error = result == 0 ? Marshal.GetLastPInvokeError() : 0;
                    return result;
                });

                int assigned = Marshal.ReadInt32(buffer, 0);
                if (ok == 0 && error != WindowsNative.ErrorMoreData)
                {
                    throw new Win32Exception(error);
                }

                int inList = Marshal.ReadInt32(buffer, 4);
                if (ok == 0 || inList < assigned)
                {
                    // Processes joined since the last attempt, or the buffer was too small: retry with room to spare.
                    capacity = Math.Max(capacity * 2, assigned + 8);
                    continue;
                }

                var ids = new List<uint>(inList);
                for (int i = 0; i < inList; i++)
                {
                    ids.Add((uint)(nuint)Marshal.ReadIntPtr(buffer, header + i * nint.Size));
                }

                return ids;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>
    /// True if <paramref name="pid"/> runs the system's console host (<c>conhost.exe</c> in the system directory; a program of
    /// the same name elsewhere is an ordinary process). A process that cannot be opened or queried (for example because it
    /// exited after the list was read) is not a console host, so it is counted: a failure must not hide a real leftover.
    /// </summary>
    private static bool IsConsoleHost(uint pid)
    {
        nint process = WindowsNative.OpenProcess(WindowsNative.ProcessQueryLimitedInformation, 0, pid);
        if (process == 0)
        {
            return false;
        }

        const int capacity = 1024;
        nint buffer = Marshal.AllocHGlobal(capacity * sizeof(char));
        try
        {
            uint size = capacity;
            if (WindowsNative.QueryFullProcessImageNameW(process, 0, buffer, ref size) == 0)
            {
                return false;
            }

            string path = Marshal.PtrToStringUni(buffer, (int)size);
            return string.Equals(path, s_consoleHostPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            WindowsNative.CloseHandle(process);
        }
    }

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
