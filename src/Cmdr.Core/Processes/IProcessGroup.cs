namespace Cmdr.Core.Processes;

/// <summary>Control over the whole process tree that belongs to one launched command.</summary>
/// <remarks>
/// Disposing the group releases its resources. On Windows, Dispose kills the tree: the job object is created with
/// KILL_ON_JOB_CLOSE, so any processes still in it are terminated when its handle closes (intentional).
/// </remarks>
internal interface IProcessGroup : IDisposable
{
    /// <summary>Sends the graceful interrupt (Ctrl+C equivalent) to every process in the tree.</summary>
    void Interrupt();

    /// <summary>Force-kills every process in the tree.</summary>
    void Kill();

    /// <summary>Number of live (non-zombie) processes in the tree, including the leader if it is alive.</summary>
    int CountLiveProcesses();

    /// <summary>
    /// Stops supervising the tree without killing it: after this call, <see cref="Dispose"/> leaves the remaining processes
    /// running. The group must not be used afterwards except to Dispose it.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The supervision could not be released.</exception>
    void Detach();
}
