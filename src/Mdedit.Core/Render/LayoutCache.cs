namespace Mdedit.Core.Render;

/// <summary>
/// A bounded cache of block layouts, least recently used out first, so that a rebuild of the document lays out only the blocks
/// whose text or container chain changed. Not thread-safe: the document is built on the UI thread.
/// </summary>
public sealed class LayoutCache
{
    /// <summary>The number of layouts a cache keeps by default.</summary>
    public const int DefaultCapacity = 4096;

    private readonly int _capacity;
    private readonly Dictionary<LayoutKey, LinkedListNode<KeyValuePair<LayoutKey, BlockLayout>>> _entries = [];
    private readonly LinkedList<KeyValuePair<LayoutKey, BlockLayout>> _recent = new();

    /// <summary>Makes an empty cache that keeps <see cref="DefaultCapacity"/> layouts.</summary>
    public LayoutCache()
        : this(DefaultCapacity)
    {
    }

    /// <summary>Makes an empty cache that keeps at most <paramref name="capacity"/> layouts.</summary>
    /// <param name="capacity">The number of layouts to keep, at least 1.</param>
    internal LayoutCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>The number of layouts the cache holds.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// The layout of a block at a width: the cached one when there is one, otherwise a new one that is then cached.
    /// </summary>
    /// <param name="doc">The document the block belongs to, for its source text.</param>
    /// <param name="block">The block.</param>
    /// <param name="width">The screen width.</param>
    internal BlockLayout Get(RenderDocument doc, RenderBlock block, int width)
    {
        var key = new LayoutKey(doc.Text.Substring(block.Start, block.End - block.Start), block, width, doc.ReferenceKey);
        if (_entries.TryGetValue(key, out LinkedListNode<KeyValuePair<LayoutKey, BlockLayout>>? node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Value;
        }

        BlockLayout layout = BlockLayout.Layout(block, width);
        _entries[key] = _recent.AddFirst(new KeyValuePair<LayoutKey, BlockLayout>(key, layout));
        while (_entries.Count > _capacity)
        {
            LinkedListNode<KeyValuePair<LayoutKey, BlockLayout>> oldest = _recent.Last!;
            _entries.Remove(oldest.Value.Key);
            _recent.RemoveLast();
        }

        return layout;
    }
}
