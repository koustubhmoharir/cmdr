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

    /// <summary>The configuration file this item was loaded from. Fixed for the life of the item.</summary>
    public abstract ConfigSource Source { get; }

    /// <summary>True when the item comes from a base file. Such an item cannot be edited, moved or removed in the session.</summary>
    public bool IsReadOnly => Source.IsBase;

    /// <summary>
    /// Why the item cannot be edited (<c>'name' comes from shared/base.json; edit that file.</c>), or null when it can be.
    /// </summary>
    public string? ReadOnlyMessage => IsReadOnly ? $"'{Name}' comes from {Source.DisplayPath}; edit that file." : null;

    /// <summary>True once the item has been removed from the session. Its process, if any, is disposed.</summary>
    internal bool IsRemoved { get; set; }
}
