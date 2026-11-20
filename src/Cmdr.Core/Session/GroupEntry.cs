using Cmdr.Core.Config;

namespace Cmdr.Core.Session;

/// <summary>
/// A group in the session: its settings, its items (commands and nested groups) in file order, and whether its sequence
/// is running. A top-level group has no <see cref="ItemEntry.Parent"/>.
/// </summary>
public sealed class GroupEntry : ItemEntry
{
    private readonly List<ItemEntry> _items = new();
    private readonly List<CommandEntry> _commands = new();
    private readonly List<GroupEntry> _groups = new();
    private string _name;
    private SequenceRun? _activeSequence;

    internal GroupEntry(string name, string? description, GroupMode mode, ConfigSource source)
    {
        _name = name;
        Description = description;
        Mode = mode;
        Source = source;
    }

    /// <inheritdoc/>
    public override string Name => _name;

    /// <inheritdoc/>
    public override ConfigSource Source { get; }

    /// <summary>Sets the display name.</summary>
    internal void Rename(string name) => _name = name;

    /// <summary>Optional description shown in the UI.</summary>
    public string? Description { get; internal set; }

    /// <summary>How the items are started.</summary>
    public GroupMode Mode { get; internal set; }

    /// <summary>The group's working directory setting as written in the config (null when not set). Kept so saving does not lose it.</summary>
    public string? Cwd { get; internal set; }

    /// <summary>The group's environment setting as written in the config (null when not set). Kept so saving does not lose it.</summary>
    public IReadOnlyDictionary<string, string?>? Env { get; internal set; }

    /// <summary>
    /// The group's environment script as written in the config (null when not set). The session keeps it so that saving does not lose it;
    /// editing the group does not change it. Do not modify the returned object.
    /// </summary>
    public EnvScriptSpec? EnvScript { get; internal set; }

    /// <summary>The group's secret names as written in the config (null when not set). Kept so saving does not lose it; editing the group does not change it.</summary>
    public IReadOnlyList<string>? Secret { get; internal set; }

    /// <summary>
    /// Whether the group starts expanded when the configuration is loaded. Only read at load: the list's collapse state is
    /// not kept in sync with it, and changing it does not change the list.
    /// </summary>
    public bool Expanded { get; internal set; } = true;

    /// <summary>Nesting depth: 0 for a top-level group.</summary>
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;

    /// <summary>The direct items (commands and nested groups) in file order.</summary>
    public IReadOnlyList<ItemEntry> Items => _items;

    /// <summary>The direct command children in file order.</summary>
    public IReadOnlyList<CommandEntry> Commands => _commands;

    /// <summary>The direct nested groups in file order.</summary>
    public IReadOnlyList<GroupEntry> Groups => _groups;

    /// <summary>All commands at any depth, depth-first in file order.</summary>
    public IReadOnlyList<CommandEntry> AllCommands => Walk(_items).OfType<CommandEntry>().ToList();

    /// <summary>True while a sequence of this group is queued or running. Safe to read from any thread.</summary>
    public bool IsSequenceRunning => ActiveSequence is not null;

    /// <summary>The sequence currently queued or running in this group, if any.</summary>
    internal SequenceRun? ActiveSequence
    {
        get => Volatile.Read(ref _activeSequence);
        set => Volatile.Write(ref _activeSequence, value);
    }

    /// <summary>This group and its descendant groups, depth-first in file order.</summary>
    internal IEnumerable<GroupEntry> SelfAndDescendantGroups()
    {
        yield return this;
        foreach (GroupEntry child in Walk(_items).OfType<GroupEntry>())
        {
            yield return child;
        }
    }

    /// <summary>All items at any depth below this group, depth-first in file order.</summary>
    internal static IEnumerable<ItemEntry> Walk(IEnumerable<ItemEntry> items)
    {
        foreach (ItemEntry item in items)
        {
            yield return item;
            if (item is GroupEntry group)
            {
                foreach (ItemEntry descendant in Walk(group._items))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>A group definition with this group's settings; its items are not included.</summary>
    internal GroupSpec ToSpec() => new()
    {
        Name = Name,
        Description = Description,
        Mode = Mode,
        Cwd = Cwd,
        Env = Env is null ? null : new Dictionary<string, string?>(Env),
        EnvScript = EnvScript?.Clone(),
        Secret = Secret is null ? null : new List<string>(Secret),
        Expanded = Expanded ? null : false,
    };

    /// <summary>The definitions of this group and its ancestors, outermost first, for launching and log paths.</summary>
    internal IReadOnlyList<GroupSpec> SpecChain()
    {
        var chain = new List<GroupSpec>();
        for (GroupEntry? group = this; group is not null; group = group.Parent)
        {
            chain.Insert(0, group.ToSpec());
        }

        return chain;
    }

    /// <summary>True if the item is a direct child of this group.</summary>
    internal bool HasItem(ItemEntry item) => _items.Contains(item);

    /// <summary>Index of a direct child, or -1.</summary>
    internal int IndexOf(ItemEntry item) => _items.IndexOf(item);

    /// <summary>Appends an item and sets its parent.</summary>
    internal void Attach(ItemEntry item)
    {
        item.Parent = this;
        _items.Add(item);
        Refresh();
    }

    /// <summary>Removes a direct child. The caller sets what happens to its parent.</summary>
    internal void Detach(ItemEntry item)
    {
        _items.Remove(item);
        Refresh();
    }

    /// <summary>Swaps two direct children by index.</summary>
    internal void Swap(int first, int second)
    {
        (_items[first], _items[second]) = (_items[second], _items[first]);
        Refresh();
    }

    private void Refresh()
    {
        _commands.Clear();
        _groups.Clear();
        foreach (ItemEntry item in _items)
        {
            if (item is CommandEntry command)
            {
                _commands.Add(command);
            }
            else if (item is GroupEntry group)
            {
                _groups.Add(group);
            }
        }
    }
}
