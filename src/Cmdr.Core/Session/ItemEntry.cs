namespace Cmdr.Core.Session;

/// <summary>An entry of a group: either a <see cref="CommandEntry"/> or a nested <see cref="GroupEntry"/>.</summary>
public abstract class ItemEntry
{
    internal ItemEntry()
    {
    }

    /// <summary>Display name.</summary>
    public abstract string Name { get; }

    /// <summary>The group that directly contains this item; null for a top-level group.</summary>
    public GroupEntry? Parent { get; internal set; }

    /// <summary>True once the item has been removed from the session. Its process, if any, is disposed.</summary>
    internal bool IsRemoved { get; set; }
}
