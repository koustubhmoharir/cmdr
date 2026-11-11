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

        EnvironmentOverlay.Apply(psi.Environment, spec.Env);
        return new PreparedLaunch(psi, InputAfterInterrupt: null);
    }

    /// <inheritdoc />
    public IProcessGroup Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return new LinuxProcessGroup(process.Id);
    }
}
