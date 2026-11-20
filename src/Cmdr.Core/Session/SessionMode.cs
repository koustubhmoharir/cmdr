namespace Cmdr.Core.Session;

/// <summary>
/// What a session allows. In run mode commands can be started, stopped and restarted, and the tree cannot be edited. In edit
/// mode groups and commands can be added, changed, moved and removed, and nothing can be started. Stopping and saving are
/// allowed in both modes.
/// </summary>
public enum SessionMode
{
    /// <summary>Commands can be started, stopped and restarted. The tree cannot be edited.</summary>
    Run,

    /// <summary>Groups and commands can be added, changed, moved and removed. Nothing can be started.</summary>
    Edit,
}
