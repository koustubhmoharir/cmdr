using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Cmdr.Core.Config;
using Microsoft.Win32.SafeHandles;

namespace Cmdr.Core.Processes;

/// <summary>
/// Windows implementation: each command runs in its own hidden console (<c>CreateNoWindow</c>), created with CreateProcessW
/// inside a new kill-on-close job object. Ctrl+C is delivered by the cmdr helper mode (see <see cref="CtrlCHelper"/>).
/// </summary>
/// <remarks>
/// The child is created already in its job (PROC_THREAD_ATTRIBUTE_JOB_LIST): there is no suspended start and no
/// assignment afterwards, so a child it spawns is tracked from its first instruction. It inherits only its own standard
/// handles (PROC_THREAD_ATTRIBUTE_HANDLE_LIST), so concurrent launches cannot leak each other's pipe ends into the wrong child.
/// Both attributes need Windows 10 version 1607 or later; on older systems the launch fails with a message saying so.
/// With console stdin (<see cref="PreparedLaunch.NullStdin"/>) the child's standard input is the null handle, so it reads its
/// own hidden console rather than cmdr's terminal.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsProcessPlatform : IProcessPlatform
{
    /// <summary>Buffer size of the pipe readers and writer, as System.Diagnostics.Process uses.</summary>
    private const int PipeBufferSize = 4096;

    private static readonly Encoding s_noBomUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Serializes the window in which this process's pipe client ends are inheritable: from their creation in <see cref="Launch"/>
    /// until the child has inherited them and cmdr has closed its copies. Every <c>Process.Start</c> made by cmdr takes this lock
    /// around the start (see <see cref="WindowsProcessGroup.Interrupt"/>), because Process.Start inherits all inheritable handles
    /// and would otherwise pick up another launch's pipe end, delaying that command's end-of-output. Third-party code that starts
    /// processes in this process is outside this guarantee.
    /// </summary>
    internal static readonly object InheritableHandleLock = new();

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
    /// <exception cref="ArgumentException">
    /// The program is a drive-relative path (<c>C:tools\run</c>); or it is a batch file and its working directory is a UNC path,
    /// or one of its arguments contains a line break.
    /// </exception>
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

        // The overlays go first so that PATH, PATHEXT and ComSpec are the effective values of this child, and the arguments
        // are expanded against the same final environment (case-insensitively, as Windows variable names are).
        EnvExpansion.ApplyLevels(psi.Environment, spec.EnvLevels, spec.ConfigDir, Path.PathSeparator, spec.Inputs);
        string[] args = spec.ExpandArgs(
            name => FindEnvironmentValue(psi.Environment, name),
            Path.PathSeparator,
            spec.Inputs);

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

        if (WindowsCommandResolver.IsBatchFile(resolved))
        {
            WindowsCommandResolver.CheckBatchWorkingDirectory(spec.WorkingDirectory);
        }

        WindowsLaunchPlan plan = WindowsCommandResolver.PlanLaunch(
            spec.Program,
            resolved,
            FindEnvironmentValue(psi.Environment, "ComSpec"),
            Environment.SystemDirectory,
            args);

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
    /// The child is created by CreateProcessW inside a new kill-on-close job, with the job list attribute, and inherits only its
    /// three standard handles (the handle list attribute). The pipe client ends are inheritable only inside
    /// <see cref="InheritableHandleLock"/>. Once the child exists the launch wraps its handles, registers the exit wait as its last
    /// fallible step, and returns. On failure the child (if one was created) is killed, the job is closed, the pipes are disposed,
    /// and <paramref name="onExited"/> is not invoked.
    /// </remarks>
    /// <exception cref="Win32Exception">
    /// The process could not be created (its message names the file and directory), the job could not be created, or the
    /// platform does not support creating a process in a job (Windows 10 version 1607 or later is required).
    /// </exception>
    /// <exception cref="ArgumentException">The command line or environment cannot be encoded for CreateProcessW.</exception>
    public LaunchedTree Launch(PreparedLaunch launch, Action<ILaunchedProcess> onExited)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(onExited);

        ProcessStartInfo psi = launch.StartInfo;

        // Built before anything is created, so an invalid argument or variable leaves nothing to clean up.
        string commandLine = WindowsCommandLine.Build(
            psi.FileName,
            psi.ArgumentList.Count == 0 ? psi.Arguments : null,
            psi.ArgumentList);
        string? applicationName = Path.IsPathFullyQualified(psi.FileName) ? psi.FileName : null;
        string environment = WindowsEnvironmentBlock.Build(psi.Environment);

        SafeJobHandle job = CreateKillOnCloseJob();
        PROCESS_INFORMATION info;
        ChildPipes pipes;
        try
        {
            (info, pipes) = CreateChild(psi, applicationName, commandLine, environment, job);
        }
        catch
        {
            job.Dispose();
            throw;
        }

        // The child exists from here on. The thread handle is not needed, and nothing useful can be done if closing it fails.
        CloseThreadHandleIgnoringFailure(info.HThread);
        var processHandle = new SafeProcessHandle(info.HProcess, ownsHandle: true);

        WindowsProcessGroup? group = null;
        WindowsLaunchedProcess? leader = null;
        try
        {
            group = new WindowsProcessGroup(job, info.DwProcessId, _ctrlCHelper);

            var stdout = new StreamReader(
                pipes.Stdout,
                psi.StandardOutputEncoding ?? s_noBomUtf8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: PipeBufferSize);
            var stderr = new StreamReader(
                pipes.Stderr,
                psi.StandardErrorEncoding ?? s_noBomUtf8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: PipeBufferSize);
            StreamWriter? stdin = pipes.Stdin is null
                ? null
                : new StreamWriter(pipes.Stdin, psi.StandardInputEncoding ?? s_noBomUtf8, PipeBufferSize);

            leader = new WindowsLaunchedProcess(processHandle, info.DwProcessId, stdout, stderr, stdin);

            // Nothing fallible follows the registration: it may already have invoked onExited.
            var tree = new LaunchedTree(leader, group);
            leader.BeginExitTracking(onExited);
            return tree;
        }
        catch
        {
            if (group is null)
            {
                job.Dispose();
            }
            else
            {
                // The child may still be running: kill the tree now, then close the job (which kills whatever is left).
                KillQuietly(group);
                group.Dispose();
            }

            if (leader is not null)
            {
                leader.Dispose(); // closes processHandle
            }
            else
            {
                processHandle.Dispose();
            }

            pipes.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates the pipes and the child, all under <see cref="InheritableHandleLock"/>, and closes cmdr's copies of the client ends
    /// before the lock is released. On failure the pipes are disposed (closing their client ends too) and the job is left to the caller.
    /// </summary>
    private static (PROCESS_INFORMATION Info, ChildPipes Pipes) CreateChild(
        ProcessStartInfo psi,
        string? applicationName,
        string commandLine,
        string environment,
        SafeJobHandle job)
    {
        lock (InheritableHandleLock)
        {
            ChildPipes? pipes = null;
            try
            {
                pipes = new ChildPipes(psi.RedirectStandardInput);
                PROCESS_INFORMATION info = CreateInJob(psi, applicationName, commandLine, environment, job, pipes);
                pipes.CloseClientCopies();
                return (info, pipes);
            }
            catch
            {
                pipes?.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Creates the child with CreateProcessW, running, inside <paramref name="job"/>, with only the handles of
    /// <paramref name="pipes"/> inherited. Standard input is the null handle unless it is redirected.
    /// </summary>
    /// <exception cref="Win32Exception">CreateProcessW failed, or the attribute list could not be built (see <see cref="ProcThreadAttributes"/>).</exception>
    private static PROCESS_INFORMATION CreateInJob(
        ProcessStartInfo psi,
        string? applicationName,
        string commandLine,
        string environment,
        SafeJobHandle job,
        ChildPipes pipes)
    {
        using var attributes = new ProcThreadAttributes(pipes.InheritableHandles, job.DangerousGetHandle());

        nint commandLinePtr = 0;
        nint environmentPtr = 0;
        try
        {
            // CreateProcessW may write into the command line, so it is a writable buffer; both are null-terminated.
            commandLinePtr = AllocUnicode(commandLine);
            environmentPtr = AllocUnicode(environment);

            var startup = new STARTUPINFOEXW();
            startup.StartupInfo.Cb = Marshal.SizeOf<STARTUPINFOEXW>();
            startup.StartupInfo.DwFlags = WindowsNative.StartfUseStdHandles;
            startup.StartupInfo.HStdInput = pipes.StdinClient; // zero (null) when stdin is the console
            startup.StartupInfo.HStdOutput = pipes.StdoutClient;
            startup.StartupInfo.HStdError = pipes.StderrClient;
            startup.LpAttributeList = attributes.List;

            uint flags = WindowsNative.ExtendedStartupInfoPresent | WindowsNative.CreateNoWindow | WindowsNative.CreateUnicodeEnvironment;

            // An empty working directory means "inherit", which is passed as a null pointer.
            string? directory = string.IsNullOrEmpty(psi.WorkingDirectory) ? null : psi.WorkingDirectory;

            int ok = WindowsNative.CreateProcessW(
                applicationName,
                commandLinePtr,
                processAttributes: 0,
                threadAttributes: 0,
                inheritHandles: 1,
                flags,
                environmentPtr,
                directory,
                ref startup,
                out PROCESS_INFORMATION info);
            if (ok == 0)
            {
                // Read first: any later call may overwrite the last error.
                int error = Marshal.GetLastPInvokeError();
                throw new Win32Exception(
                    error,
                    $"Could not start '{psi.FileName}' in '{psi.WorkingDirectory}': {new Win32Exception(error).Message}");
            }

            return info;
        }
        finally
        {
            Marshal.FreeHGlobal(commandLinePtr);
            Marshal.FreeHGlobal(environmentPtr);
        }
    }

    /// <summary>
    /// Copies <paramref name="text"/> into unmanaged memory as UTF-16, followed by one NUL. Every character is copied, including
    /// NUL characters inside the text, which an environment block needs. The caller frees it with <see cref="Marshal.FreeHGlobal"/>.
    /// </summary>
    private static nint AllocUnicode(string text)
    {
        char[] chars = new char[text.Length + 1]; // the last element stays '\0'
        text.CopyTo(0, chars, 0, text.Length);
        nint ptr = Marshal.AllocHGlobal(chars.Length * sizeof(char));
        Marshal.Copy(chars, 0, ptr, chars.Length);
        return ptr;
    }

    /// <summary>Creates an unnamed job object that kills its processes when its last handle closes.</summary>
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

    /// <summary>Closes the thread handle of a new child. A failure is ignored: the process handle is what is used, and there is nothing to recover.</summary>
    private static void CloseThreadHandleIgnoringFailure(nint handle) => WindowsNative.CloseHandle(handle);

    /// <summary>Kills the group's processes, ignoring a failure: the failure that caused the cleanup is the one reported.</summary>
    private static void KillQuietly(WindowsProcessGroup group)
    {
        try
        {
            group.Kill();
        }
        catch (Win32Exception)
        {
            // Closing the job still kills the processes (KILL_ON_JOB_CLOSE).
        }
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

    /// <summary>
    /// The anonymous pipes of one child. The server ends stay in cmdr and are not inheritable; the client ends are inheritable,
    /// go to the child, and cmdr's copies are closed by <see cref="CloseClientCopies"/> or, on failure, by <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// Reading <see cref="AnonymousPipeServerStream.ClientSafePipeHandle"/> marks the handle as exposed, so disposing the server
    /// stream alone would leave the client handle open. Every client copy is therefore released explicitly with
    /// <see cref="AnonymousPipeServerStream.DisposeLocalCopyOfClientHandle"/> before the server ends are disposed.
    /// </remarks>
    private sealed class ChildPipes : IDisposable
    {
        private readonly List<AnonymousPipeServerStream> _servers = new();
        private readonly HashSet<AnonymousPipeServerStream> _clientReleased = new();
        private AnonymousPipeServerStream? _stdin;
        private AnonymousPipeServerStream? _stdout;
        private AnonymousPipeServerStream? _stderr;
        private nint _stdinClient;
        private nint _stdoutClient;
        private nint _stderrClient;
        private readonly List<nint> _inheritable = new();

        /// <summary>Creates the pipes. A failure disposes the ones already created.</summary>
        internal ChildPipes(bool redirectStdin)
        {
            try
            {
                _stdout = Create(PipeDirection.In, out _stdoutClient);
                _stderr = Create(PipeDirection.In, out _stderrClient);
                if (redirectStdin)
                {
                    _stdin = Create(PipeDirection.Out, out _stdinClient);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>The stdin client handle, or zero when stdin is not redirected (the child then has a null stdin).</summary>
        internal nint StdinClient => _stdinClient;

        internal nint StdoutClient => _stdoutClient;

        internal nint StderrClient => _stderrClient;

        /// <summary>The handles the child may inherit: the client ends that exist.</summary>
        internal IReadOnlyList<nint> InheritableHandles => _inheritable;

        /// <summary>The server end of the child's stdout (the read side). Owned by the caller once the child exists.</summary>
        internal AnonymousPipeServerStream Stdout => _stdout ?? throw new InvalidOperationException("stdout pipe missing");

        internal AnonymousPipeServerStream Stderr => _stderr ?? throw new InvalidOperationException("stderr pipe missing");

        /// <summary>The server end of the child's stdin (the write side), or null when stdin is not redirected.</summary>
        internal AnonymousPipeServerStream? Stdin => _stdin;

        /// <summary>
        /// Closes cmdr's copies of the client ends. Call once the child has been created, after it has inherited them: otherwise
        /// the child's exit does not produce EOF on the readers. Safe to call more than once.
        /// </summary>
        internal void CloseClientCopies()
        {
            foreach (AnonymousPipeServerStream server in _servers)
            {
                if (_clientReleased.Add(server))
                {
                    server.DisposeLocalCopyOfClientHandle();
                }
            }
        }

        /// <summary>
        /// Releases every client copy that is still open, then disposes the server ends. Safe on partial construction and when
        /// called repeatedly.
        /// </summary>
        public void Dispose()
        {
            CloseClientCopies();
            foreach (AnonymousPipeServerStream server in _servers)
            {
                server.Dispose();
            }
        }

        /// <summary>Creates one anonymous pipe. The server end is registered first, so a failure in <c>ClientSafePipeHandle</c> is disposed too.</summary>
        private AnonymousPipeServerStream Create(PipeDirection serverDirection, out nint clientHandle)
        {
            var server = new AnonymousPipeServerStream(serverDirection, HandleInheritability.Inheritable);
            _servers.Add(server);
            clientHandle = server.ClientSafePipeHandle.DangerousGetHandle();
            _inheritable.Add(clientHandle);
            return server;
        }
    }

    /// <summary>
    /// A process attribute list with the handles the child may inherit and the job it is created in. Owns the list and the
    /// unmanaged value buffers, which must stay valid until CreateProcessW returns.
    /// </summary>
    private sealed class ProcThreadAttributes : IDisposable
    {
        private const int AttributeCount = 2;

        private nint _list;
        private bool _initialized;
        private nint _handleValues;
        private nint _jobValues;

        /// <summary>Builds the list. Throws a <see cref="Win32Exception"/> if an attribute cannot be set; nothing is left allocated then.</summary>
        internal ProcThreadAttributes(IReadOnlyList<nint> inheritableHandles, nint job)
        {
            try
            {
                // The sizing call is expected to fail with ERROR_INSUFFICIENT_BUFFER, and it reports the size needed.
                nint size = 0;
                WindowsNative.InitializeProcThreadAttributeList(0, AttributeCount, 0, ref size);
                if (size <= 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                _list = Marshal.AllocHGlobal(size);
                if (WindowsNative.InitializeProcThreadAttributeList(_list, AttributeCount, 0, ref size) == 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                _initialized = true;

                nint handleBytes = inheritableHandles.Count * nint.Size;
                _handleValues = Marshal.AllocHGlobal(handleBytes);
                for (int i = 0; i < inheritableHandles.Count; i++)
                {
                    Marshal.WriteIntPtr(_handleValues, i * nint.Size, inheritableHandles[i]);
                }

                if (WindowsNative.UpdateProcThreadAttribute(
                        _list, 0, WindowsNative.ProcThreadAttributeHandleList, _handleValues, handleBytes, 0, 0) == 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    throw new Win32Exception(
                        error,
                        $"Could not limit the inherited handles (PROC_THREAD_ATTRIBUTE_HANDLE_LIST, Win32 error {error}): {new Win32Exception(error).Message}");
                }

                _jobValues = Marshal.AllocHGlobal(nint.Size);
                Marshal.WriteIntPtr(_jobValues, job);
                if (WindowsNative.UpdateProcThreadAttribute(
                        _list, 0, WindowsNative.ProcThreadAttributeJobList, _jobValues, nint.Size, 0, 0) == 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    throw new Win32Exception(
                        error,
                        $"Could not create the process in its job (PROC_THREAD_ATTRIBUTE_JOB_LIST, Win32 error {error}). " +
                        $"Windows 10 version 1607 or later is required: {new Win32Exception(error).Message}");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>The attribute list pointer for STARTUPINFOEXW.</summary>
        internal nint List => _list;

        public void Dispose()
        {
            if (_initialized)
            {
                WindowsNative.DeleteProcThreadAttributeList(_list);
                _initialized = false;
            }

            Free(ref _list);
            Free(ref _handleValues);
            Free(ref _jobValues);
        }

        private static void Free(ref nint pointer)
        {
            if (pointer != 0)
            {
                Marshal.FreeHGlobal(pointer);
                pointer = 0;
            }
        }
    }
}
