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
}

/// <summary>A started leader together with the process group or job that tracks its tree.</summary>
internal sealed record LaunchedTree(ILaunchedProcess Process, IProcessGroup Group);
