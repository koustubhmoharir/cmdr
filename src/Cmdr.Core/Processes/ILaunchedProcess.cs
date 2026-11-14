namespace Cmdr.Core.Processes;

/// <summary>A started leader process: its id, its redirected streams, and its exit.</summary>
internal interface ILaunchedProcess : IDisposable
{
    int Id { get; }

    StreamReader StandardOutput { get; }

    StreamReader StandardError { get; }

    /// <summary>Null when stdin is not redirected (StdinMode.Console).</summary>
    StreamWriter? StandardInput { get; }

    /// <summary>Valid once the exit callback has run.</summary>
    int ExitCode { get; }

    /// <summary>
    /// Releases the process resources. Disposing before the exit was reported cancels the exit callback: it does not run after this
    /// returns. A callback that had already started may still run. Does not close the redirected streams, which their owner disposes.
    /// </summary>
    new void Dispose();
}

/// <summary>A started leader together with the process group or job that tracks its tree.</summary>
internal sealed record LaunchedTree(ILaunchedProcess Process, IProcessGroup Group);
