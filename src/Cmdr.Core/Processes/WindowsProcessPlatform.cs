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
    /// <remarks>
    /// Resolves the program the way Windows would with PATHEXT applied, so shims such as <c>pnpm.cmd</c> launch.
    /// A resolved batch file runs through the command interpreter; see <see cref="WindowsCommandResolver.PlanLaunch"/>.
    /// </remarks>
    public PreparedLaunch Prepare(LaunchSpec spec)
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

        // The overlays go first so that PATH, PATHEXT and ComSpec are the effective values of this child.
        EnvExpansion.ApplyLevels(psi.Environment, spec.EnvLevels, spec.ConfigDir, Path.PathSeparator);

        string? resolved = WindowsCommandResolver.Resolve(
            spec.Program,
            spec.WorkingDirectory,
            FindEnvironmentValue(psi.Environment, "PATH"),
            FindEnvironmentValue(psi.Environment, "PATHEXT"),
            File.Exists);

        WindowsLaunchPlan plan = WindowsCommandResolver.PlanLaunch(
            spec.Program,
            resolved,
            FindEnvironmentValue(psi.Environment, "ComSpec"),
            spec.Args);

        psi.FileName = plan.FileName;
        if (plan.Arguments is not null)
        {
            // Raw, because the escaping is already done; ArgumentList would quote it a second time.
            psi.Arguments = plan.Arguments;
        }
        else
        {
            foreach (string arg in plan.ArgumentList)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        // Ctrl+C on a batch file makes cmd.exe ask "Terminate batch job (Y/N)?" and wait on stdin. The answer is
        // written after the interrupt. Caveat: a child that reads stdin itself (before cmd asks) could consume the
        // "Y" instead; in that case the batch job stays at the prompt and force kill is needed.
        return new PreparedLaunch(psi, plan.InputAfterInterrupt);
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

    /// <summary>Case-insensitive lookup: Windows environment variable names are case-insensitive, and "Path" is common.</summary>
    private static string? FindEnvironmentValue(IDictionary<string, string?> environment, string name)
    {
        foreach (KeyValuePair<string, string?> entry in environment)
        {
            if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
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
