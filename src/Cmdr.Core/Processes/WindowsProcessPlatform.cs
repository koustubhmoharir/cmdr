using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Cmdr.Core.Config;

namespace Cmdr.Core.Processes;

/// <summary>
/// Windows implementation: each command runs in its own hidden console (<c>CreateNoWindow</c>) and is assigned to a
/// kill-on-close job object. Ctrl+C is delivered by the cmdr helper mode (see <see cref="CtrlCHelper"/>).
/// </summary>
/// <remarks>
/// Known race: the process is assigned to the job after <see cref="Process.Start()"/> returns, so a child it spawns in
/// that window is not in the job and is not tracked or killed. If the leader has already exited when assignment is
/// attempted, the assignment is refused and no tree is tracked for that run.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsProcessPlatform : IProcessPlatform
{
    private const int ErrorAccessDenied = 5;

    private readonly CtrlCHelperCommand _ctrlCHelper;

    /// <summary>Creates the platform; the Ctrl+C helper defaults to the current cmdr executable.</summary>
    public WindowsProcessPlatform()
        : this(CtrlCHelperCommand.ForCurrentProcess())
    {
    }

    /// <summary>Creates the platform with an explicit Ctrl+C helper command.</summary>
    internal WindowsProcessPlatform(CtrlCHelperCommand ctrlCHelper)
    {
        ArgumentNullException.ThrowIfNull(ctrlCHelper);
        _ctrlCHelper = ctrlCHelper;
    }

    /// <inheritdoc />
    public ProcessStartInfo CreateStartInfo(LaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var psi = new ProcessStartInfo
        {
            FileName = spec.Program,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true, // gives the child its own hidden console, which the Ctrl+C mechanism needs
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        foreach (string arg in spec.Args)
        {
            psi.ArgumentList.Add(arg);
        }

        EnvironmentOverlay.Apply(psi.Environment, spec.Env);
        return psi;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Creates a kill-on-close job and assigns the leader to it. If that fails, the leader is killed so that an
    /// untracked process is never left running, and the failure is rethrown.
    /// </remarks>
    /// <exception cref="Win32Exception">The job could not be created or configured, or the leader could not be assigned.</exception>
    public IProcessGroup Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            SafeJobHandle job = CreateKillOnCloseJob();
            try
            {
                AssignLeader(job, process);
            }
            catch
            {
                job.Dispose();
                throw;
            }

            return new WindowsProcessGroup(job, process.Id, _ctrlCHelper);
        }
        catch
        {
            KillUntracked(process);
            throw;
        }
    }

    private static SafeJobHandle CreateKillOnCloseJob()
    {
        nint handle = WindowsNative.CreateJobObjectW(0, 0);
        int error = Marshal.GetLastPInvokeError();

        var job = new SafeJobHandle(handle);
        if (job.IsInvalid)
        {
            job.Dispose();
            throw new Win32Exception(error);
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = WindowsNative.JobObjectLimitKillOnJobClose;
        uint size = (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        if (WindowsNative.SetInformationJobObject(job.DangerousGetHandle(), WindowsNative.JobObjectExtendedLimitInformation, ref info, size) == 0)
        {
            error = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw new Win32Exception(error);
        }

        return job;
    }

    private static void AssignLeader(SafeJobHandle job, Process process)
    {
        if (WindowsNative.AssignProcessToJobObject(job.DangerousGetHandle(), process.Handle) != 0)
        {
            return;
        }

        int error = Marshal.GetLastPInvokeError();

        // A leader that exits quickly (before assignment) makes Windows refuse the assignment. That is not a failure
        // of supervision: there is simply no live tree to track.
        if (error == ErrorAccessDenied && process.HasExited)
        {
            return;
        }

        throw new Win32Exception(error);
    }

    private static void KillUntracked(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Could not be killed; the original exception is the one reported.
        }
    }
}
