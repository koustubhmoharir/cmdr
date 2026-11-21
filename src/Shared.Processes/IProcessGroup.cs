namespace Shared.Processes;

/// <summary>Control over the whole process tree that belongs to one launched command.</summary>
/// <remarks>
/// Disposing the group releases its resources. On Windows, Dispose kills the tree: the job object is created with
/// KILL_ON_JOB_CLOSE, so any processes still in it are terminated when its handle closes (intentional).
/// </remarks>
public interface IProcessGroup : IDisposable
{
    /// <summary>
    /// Sends the graceful interrupt (Ctrl+C equivalent) to every process in the tree. Does nothing once <see cref="Detach"/>
    /// has been called: a signal that was already on its way when the tree was detached is dropped.
    /// </summary>
    void Interrupt();

    /// <summary>
    /// Force-kills every process in the tree. Does nothing once <see cref="Detach"/> has been called, like <see cref="Interrupt"/>.
    /// </summary>
    void Kill();

    /// <summary>Number of live (non-zombie) processes in the tree, including the leader if it is alive.</summary>
    int CountLiveProcesses();

    /// <summary>
    /// Stops supervising the tree without killing it: after this call, <see cref="Dispose"/> leaves the remaining processes
    /// running, and <see cref="Interrupt"/> and <see cref="Kill"/> do nothing. The group must not be used afterwards except to
    /// Dispose it.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The supervision could not be released.</exception>
    void Detach();
}
