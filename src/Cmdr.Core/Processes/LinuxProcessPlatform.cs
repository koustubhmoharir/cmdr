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
    public void Start(Process process, PreparedLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(process);
        process.Start();
    }

    /// <inheritdoc />
    public IProcessGroup Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        WaitForOwnGroup(process.Id);
        return new LinuxProcessGroup(process.Id);
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
