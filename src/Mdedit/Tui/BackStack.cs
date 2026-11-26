namespace Mdedit.Tui;

/// <summary>
/// The places that the followed file links came from, most recent last. The host owns one stack for a run of documents: a window pushes
/// the place it leaves when it follows a file link, and pops the place it goes back to. Ctrl+O and the picker clear it, since they start
/// a new run of links. Not thread-safe; it is used on the application's thread only.
/// </summary>
internal sealed class BackStack
{
    private readonly List<BackEntry> _entries = [];

    /// <summary>The number of places on the stack.</summary>
    public int Count => _entries.Count;

    /// <summary>Puts a place on top of the stack.</summary>
    /// <param name="entry">The place.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <c>null</c>.</exception>
    public void Push(BackEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
    }

    /// <summary>The place on top of the stack, without removing it, or null when the stack is empty.</summary>
    public BackEntry? Peek() => _entries.Count > 0 ? _entries[^1] : null;

    /// <summary>Removes the place on top of the stack and returns it, or null when the stack is empty.</summary>
    public BackEntry? Pop()
    {
        BackEntry? top = Peek();
        if (top is not null)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        return top;
    }

    /// <summary>Removes every place.</summary>
    public void Clear() => _entries.Clear();
}
