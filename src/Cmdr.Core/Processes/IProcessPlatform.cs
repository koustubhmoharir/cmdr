namespace Cmdr.Core.Processes;

/// <summary>Platform-specific launch and process-tree control.</summary>
internal interface IProcessPlatform
{
    /// <summary>
    /// Builds the start info that launches <paramref name="spec"/> in its own process group or job, plus any stdin
    /// input the platform needs after a Ctrl+C.
    /// </summary>
    PreparedLaunch Prepare(LaunchSpec spec);

    /// <summary>
    /// Starts the process described by <paramref name="launch"/> in its own process group or job, and returns it with
    /// that group. The process must be in the group from its creation, so that nothing it starts can escape the group.
    /// </summary>
    /// <param name="launch">The result of <see cref="Prepare"/>.</param>
    /// <param name="onExited">
    /// Invoked exactly once, on a thread-pool thread, after the leader exits. It may run before <see cref="Launch"/>
    /// returns, so callers must tolerate that. It receives the same instance that <see cref="LaunchedTree.Process"/> returns.
    /// </param>
    /// <returns>The started leader and its group.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">The process could not be started or attached to its group.</exception>
    /// <exception cref="IOException">The process started but did not get its own group in time (Linux).</exception>
    /// <remarks>
    /// On failure nothing is left running or untracked: the platform kills whatever it started and disposes what it created
    /// (possibly after returning, so as not to dispose under the caller's lock). If the leader had started,
    /// <paramref name="onExited"/> may still be invoked for it, with an instance the caller never received; callers ignore it.
    /// </remarks>
    LaunchedTree Launch(PreparedLaunch launch, Action<ILaunchedProcess> onExited);
}
