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

    /// <summary>Serializes the process-wide standard input swap in <see cref="StartWithNullStdin"/>.</summary>
    private static readonly object s_stdinLock = new();

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
    /// A program that does not resolve is a start failure. It is not handed to Process.Start, because CreateProcess
    /// would search cmdr's own directory, the system directories and the parent PATH and could run a different file.
    /// </remarks>
    /// <exception cref="FileNotFoundException">The program does not resolve under the child's working directory or PATH.</exception>
    public PreparedLaunch Prepare(LaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // With Console the child keeps its own hidden console as its stdin instead of a pipe (see StdinMode.Console).
        bool pipeStdin = spec.Stdin == StdinMode.Pipe;
        var psi = new ProcessStartInfo
        {
            FileName = spec.Program,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true, // gives the child its own hidden console, which the Ctrl+C mechanism needs
            RedirectStandardInput = pipeStdin,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = pipeStdin ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : null,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        // The overlays go first so that PATH, PATHEXT and ComSpec are the effective values of this child.
        EnvExpansion.ApplyLevels(psi.Environment, spec.EnvLevels, spec.ConfigDir, Path.PathSeparator);

        string? pathExt = FindEnvironmentValue(psi.Environment, "PATHEXT");
        string? resolved = WindowsCommandResolver.Resolve(
            spec.Program,
            spec.WorkingDirectory,
            FindEnvironmentValue(psi.Environment, "PATH"),
            pathExt,
            File.Exists);

        if (resolved is null)
        {
            throw new FileNotFoundException(
                WindowsCommandResolver.NotFoundMessage(spec.Program, spec.WorkingDirectory, pathExt));
        }

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
        if (pipeStdin)
        {
            return new PreparedLaunch(psi, plan.InputAfterInterrupt);
        }

        // Console stdin: there is no pipe to write the answer to, and the prompt appears on the hidden console, which
        // cmdr does not show. The user is told once at start; the batch job then needs a force kill.
        // Start gives the child a null stdin handle (NullStdin), so the child reads its own console, not cmdr's.
        if (plan.InputAfterInterrupt is not null)
        {
            return new PreparedLaunch(
                psi,
                InputAfterInterrupt: null,
                Notice: "stdin is the console: cmdr cannot answer \"Terminate batch job (Y/N)?\" after Ctrl+C",
                NullStdin: true);
        }

        return new PreparedLaunch(psi, InputAfterInterrupt: null, NullStdin: true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The process is started with <see cref="StartWithNullStdin"/> when <see cref="PreparedLaunch.NullStdin"/> is set, and
    /// is then assigned to a new kill-on-close job. If the assignment fails, the leader is killed so that an untracked
    /// process is never left running, and the failure is rethrown.
    /// </remarks>
    /// <exception cref="Win32Exception">The process could not be started, cmdr's standard input could not be restored, or the job could not be created, configured or assigned.</exception>
    public LaunchedTree Launch(PreparedLaunch launch, Action<ILaunchedProcess> onExited)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(onExited);

        var process = new ProcessLaunched(launch.StartInfo, onExited);
        try
        {
            if (launch.NullStdin)
            {
                StartWithNullStdin(process);
            }
            else
            {
                process.Start();
            }
        }
        catch
        {
            process.Dispose();
            throw;
        }

        try
        {
            SafeJobHandle job = CreateKillOnCloseJob();
            try
            {
                AssignLeader(job, process.Process);
            }
            catch
            {
                job.Dispose();
                throw;
            }

            return new LaunchedTree(process, new WindowsProcessGroup(job, process.Id, _ctrlCHelper));
        }
        catch
        {
            KillUntracked(process.Process);
            process.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Starts the child with a null standard input handle while cmdr's own is unrelated. Stdout and stderr are redirected,
    /// so .NET starts the child with STARTF_USESTDHANDLES and, for the unredirected stdin, passes the value of
    /// <c>GetStdHandle(STD_INPUT_HANDLE)</c> read during <see cref="Process.Start()"/>. Without this, a command such as
    /// <c>dotnet watch</c> would read keys from cmdr's terminal. cmdr's handle is set to null only for that call and
    /// restored right after. A null handle is what makes Windows give the child its new hidden console as its input
    /// (expected behaviour; confirmed on Windows only by the user, not in this environment).
    /// </summary>
    /// <exception cref="Win32Exception">cmdr's standard input could not be changed or restored, or the process could not be started.</exception>
    private static void StartWithNullStdin(ProcessLaunched process)
    {
        lock (s_stdinLock)
        {
            // Saved as it is, whatever it is (a valid handle, or none): the same value is put back afterwards.
            nint saved = WindowsNative.GetStdHandle(WindowsNative.StdInputHandle);
            if (WindowsNative.SetStdHandle(WindowsNative.StdInputHandle, 0) == 0)
            {
                // Nothing has been started yet, so failing here leaves no child behind.
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            bool started = false;
            try
            {
                process.Start();
                started = true;
            }
            finally
            {
                if (WindowsNative.SetStdHandle(WindowsNative.StdInputHandle, saved) == 0)
                {
                    // cmdr's own stdin is now null, which is not recoverable from here. The child is killed rather than
                    // left running untracked, and the failure is reported, not swallowed.
                    int error = Marshal.GetLastPInvokeError();
                    if (started)
                    {
                        KillUntracked(process.Process);
                    }

                    throw new Win32Exception(error);
                }
            }
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
