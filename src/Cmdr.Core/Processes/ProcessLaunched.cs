using System.Diagnostics;

namespace Cmdr.Core.Processes;

/// <summary>
/// <see cref="ILaunchedProcess"/> over <see cref="Process"/>. Shared by both platforms for now; the Windows launch
/// will create its process directly, so this wrapper is expected to go.
/// </summary>
internal sealed class ProcessLaunched : ILaunchedProcess
{
    private readonly Process _process;
    private readonly bool _redirectStdin;

    /// <summary>
    /// Creates the process without starting it. <paramref name="onExited"/> is subscribed before <see cref="Start"/>, so an
    /// immediate exit is not missed; it receives this instance.
    /// </summary>
    public ProcessLaunched(ProcessStartInfo startInfo, Action<ILaunchedProcess> onExited)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(onExited);

        _redirectStdin = startInfo.RedirectStandardInput;
        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        _process.Exited += (_, _) => onExited(this);
    }

    /// <summary>The wrapped process, for platform work that needs it, such as attaching it to a job object.</summary>
    internal Process Process => _process;

    /// <inheritdoc />
    public int Id => _process.Id;

    /// <inheritdoc />
    public StreamReader StandardOutput => _process.StandardOutput;

    /// <inheritdoc />
    public StreamReader StandardError => _process.StandardError;

    /// <inheritdoc />
    /// <remarks>Process.StandardInput throws when stdin is not redirected, so it is read only when it is.</remarks>
    public StreamWriter? StandardInput => _redirectStdin ? _process.StandardInput : null;

    /// <inheritdoc />
    public int ExitCode => _process.ExitCode;

    /// <summary>Starts the process. Platforms call this, wrapped as they need.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The process could not be started.</exception>
    public void Start() => _process.Start();

    /// <inheritdoc />
    public void Dispose() => _process.Dispose();
}
