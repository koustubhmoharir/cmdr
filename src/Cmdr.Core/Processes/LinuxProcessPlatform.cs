using System.Diagnostics;
using System.Text;
using Cmdr.Core.Config;

namespace Cmdr.Core.Processes;

/// <summary>
/// Linux implementation: the program is launched through <c>setsid</c>, which execs in place, so the
/// leader's pid is also its process group id (and session id).
/// </summary>
internal sealed class LinuxProcessPlatform : IProcessPlatform
{
    private const int GroupCreationTimeoutMs = 2000;

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

        psi.ArgumentList.Add("--"); // a program name starting with '-' must not be read as a setsid option
        psi.ArgumentList.Add(spec.Program);
        foreach (string arg in spec.Args)
        {
            psi.ArgumentList.Add(arg);
        }

        EnvExpansion.ApplyLevels(psi.Environment, spec.EnvLevels, spec.ConfigDir, Path.PathSeparator);
        return new PreparedLaunch(psi, InputAfterInterrupt: null);
    }

    /// <inheritdoc />
    /// <remarks>Starts the leader and waits, bounded, until setsid has given it its own group. If that fails, the leader is killed.</remarks>
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
            WaitForOwnGroup(process.Id);
            return new LaunchedTree(process, new LinuxProcessGroup(process.Id));
        }
        catch
        {
            if (started)
            {
                KillLeader(process);
            }

            process.Dispose();
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
    /// lost (ESRCH). Waits, bounded, until the leader leads its own group or has exited.
    /// </summary>
    private static void WaitForOwnGroup(int pid)
    {
        long deadline = Environment.TickCount64 + GroupCreationTimeoutMs;
        var spinner = new SpinWait();
        while (Environment.TickCount64 < deadline)
        {
            int? pgid = LinuxProcessGroup.ReadProcessGroupId(pid);
            if (pgid is null || pgid == pid)
            {
                return;
            }

            spinner.SpinOnce(sleep1Threshold: 10);
        }
    }
}
