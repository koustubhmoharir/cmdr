namespace Cmdr.Core.Session;

/// <summary>
/// One pending restart of a set of commands. The session tracks it while it waits, so that an explicit Stop, Stop of a group,
/// ForceKill, StopAll, ForceKillAll or Dispose can revoke it before it starts anything again.
/// </summary>
/// <remarks>
/// <see cref="Cancelled"/>, <see cref="Forced"/> and the disposal bookkeeping are guarded by the session's <c>_sync</c>.
/// <see cref="Token"/> is cancelled by the session after the lock is released, and by the caller's token.
/// The linked source is disposed only once the restart has finished and no revocation still has to cancel it.
/// </remarks>
internal sealed class RestartOperation
{
    private readonly CancellationTokenSource _cts;
    private int _pendingCancels;
    private bool _finished;

    /// <param name="group">The group the restart starts in.</param>
    /// <param name="commands">The commands the restart stops and starts.</param>
    /// <param name="callerToken">The caller's token; cancelling it cancels <see cref="Token"/>.</param>
    internal RestartOperation(GroupEntry group, IReadOnlySet<CommandEntry> commands, CancellationToken callerToken)
    {
        Group = group;
        Commands = commands;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        Token = _cts.Token;
    }

    /// <summary>The group the restart starts in: the targets' group, or the restarted group.</summary>
    internal GroupEntry Group { get; }

    /// <summary>The commands involved: the targets, or every command of the group when the restart was started.</summary>
    internal IReadOnlySet<CommandEntry> Commands { get; }

    /// <summary>Cancelled when the operation is revoked or the caller's token is cancelled.</summary>
    internal CancellationToken Token { get; }

    /// <summary>True once a Stop, ForceKill, StopAll, ForceKillAll or Dispose has revoked the operation. Guarded by <c>_sync</c>.</summary>
    internal bool Cancelled { get; private set; }

    /// <summary>True if a revocation asked for processes to be force-killed rather than stopped. Guarded by <c>_sync</c>.</summary>
    internal bool Forced { get; private set; }

    /// <summary>Marks the operation revoked. Caller holds <c>_sync</c> and must call <see cref="CancelToken"/> after releasing it.</summary>
    internal void Revoke(bool force)
    {
        Cancelled = true;
        Forced |= force;
        _pendingCancels++;
    }

    /// <summary>Cancels <see cref="Token"/>. Call without <c>_sync</c>: cancellation runs callbacks.</summary>
    internal void CancelToken() => _cts.Cancel();

    /// <summary>
    /// Marks the restart finished. Caller holds <c>_sync</c>. Returns true when the source may be disposed now, that is when no
    /// revocation is still going to cancel it.
    /// </summary>
    internal bool Finish()
    {
        if (_finished)
        {
            return false;
        }

        _finished = true;
        return _pendingCancels == 0;
    }

    /// <summary>
    /// Records that a revocation has cancelled <see cref="Token"/>. Caller holds <c>_sync</c>. Returns true when the source may be
    /// disposed now, that is when the restart has finished and this was the last pending cancellation.
    /// </summary>
    internal bool EndCancel()
    {
        _pendingCancels--;
        return _finished && _pendingCancels == 0;
    }

    /// <summary>Disposes the linked source. Call only when <see cref="Finish"/> or <see cref="EndCancel"/> said so.</summary>
    internal void Dispose() => _cts.Dispose();
}
