using System.Diagnostics;
using System.Text;

namespace Shared.Processes;

/// <summary>
/// Linux implementation: the program is launched through <c>setsid</c>, which execs in place, so the
/// leader's pid is also its process group id (and session id).
/// </summary>
internal sealed class LinuxProcessPlatform : IProcessPlatform
{
    private static readonly TimeSpan DefaultGroupTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _groupTimeout;
    private readonly Func<int, int?> _readGroupId;

    /// <summary>Creates the platform with the real group timeout and the real <c>/proc</c> reader.</summary>
    public LinuxProcessPlatform()
        : this(DefaultGroupTimeout, LinuxProcessGroup.ReadProcessGroupId)
    {
    }

    /// <summary>
    /// Creates the platform with an injected group timeout and pgid reader (<paramref name="readGroupId"/> returns the pgid
    /// of a pid, or null when the process is gone). For tests.
    /// </summary>
    internal LinuxProcessPlatform(TimeSpan groupTimeout, Func<int, int?> readGroupId)
    {
        ArgumentNullException.ThrowIfNull(readGroupId);
        _groupTimeout = groupTimeout;
        _readGroupId = readGroupId;
    }

    /// <inheritdoc />
    public PreparedLaunch Prepare(LaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var psi = new ProcessStartInfo
        {
            FileName = "setsid",
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        // The environment first: the arguments may refer to the final environment of the command.
        EnvExpansion.ApplyLevels(psi.Environment, spec.EnvLevels, spec.ConfigDir, Path.PathSeparator, spec.Inputs);
        string[] args = spec.ExpandArgs(
            name => psi.Environment.TryGetValue(name, out string? value) ? value : null,
            Path.PathSeparator,
            spec.Inputs);

        psi.ArgumentList.Add("--"); // a program name starting with '-' must not be read as a setsid option
        psi.ArgumentList.Add(spec.Program);
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return new PreparedLaunch(psi, InputAfterInterrupt: null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Starts the leader and waits, bounded, until setsid has given it its own group. If that fails, the leader is killed and
    /// the launch throws; the leader's exit callback may still run, and the caller ignores it.
    /// </remarks>
    public LaunchedTree Launch(PreparedLaunch launch, Action<ILaunchedProcess> onExited)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(onExited);

        var process = new ProcessLaunched(launch.StartInfo, onExited);
        bool started = false;
        try
        {
            process.Start();
            started = true;
            if (!WaitForOwnGroup(process.Id))
            {
                throw new IOException($"The process did not get its own process group within {_groupTimeout.TotalSeconds} s.");
            }

            return new LaunchedTree(process, new LinuxProcessGroup(process.Id));
        }
        catch
        {
            if (started)
            {
                KillLeader(process);

                // Not disposed here: Process.Dispose waits for the lock that Process holds while it raises Exited, and the
                // caller (ManagedProcess.StartLocked) holds its own lock, which the Exited callback needs. Disposing on the
                // thread pool lets this call return and release the caller's lock first.
                _ = Task.Run(process.Dispose);
            }
            else
            {
                process.Dispose();
            }

            throw;
        }
    }

    private static void KillLeader(ProcessLaunched process)
    {
        try
        {
            process.Process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Could not be killed; the original exception is the one reported.
        }
    }

    /// <summary>
    /// Process.Start returns before setsid has created the new group, so a group signal sent right away would be
    /// lost (ESRCH). Waits, bounded, until the leader leads its own group or has exited. Returns false if the leader
    /// is still in another group when the timeout passes.
    /// </summary>
    private bool WaitForOwnGroup(int pid)
    {
        long deadline = Environment.TickCount64 + (long)_groupTimeout.TotalMilliseconds;
        var spinner = new SpinWait();
        while (true)
        {
            int? pgid = _readGroupId(pid);
            if (pgid is null || pgid == pid)
            {
                return true;
            }

            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }

            spinner.SpinOnce(sleep1Threshold: 10);
        }
    }
}
