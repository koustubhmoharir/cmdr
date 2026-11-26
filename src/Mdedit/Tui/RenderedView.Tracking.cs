using Mdedit.Core.Render;

namespace Mdedit.Tui;

/// <summary>
/// The view's document can be replaced when the text it renders has changed (see <see cref="SetDocument"/>). The fold state and the table
/// scroll offsets are kept as tracked positions (see <see cref="IOffsetTracker"/>), so that they follow the text: a folded section stays
/// folded at its moved place, and a table keeps its scroll after an edit above it. The positions are created when a key is set, and
/// synchronised after every change of those keys, so that they are current when the text is edited.
/// </summary>
internal sealed partial class RenderedView
{
    // The tracker that creates the positions of the fold state and the scroll offsets.
    private readonly IOffsetTracker _tracker;

    // The tracked position of each folded unit, by the unit's start. Its offset is the key while the document is current.
    private Dictionary<int, ITrackedOffset> _foldHandles = [];

    // The tracked position of each scrolling block, by the block's start. Its offset is the key while the document is current.
    private Dictionary<int, ITrackedOffset> _shiftHandles = [];

    /// <summary>
    /// Replaces the document the view shows, when its text has changed. The caret and the selection keep their offsets (clamped to the new
    /// text), and the top line keeps its row as for a reflow. A fold stays folded when its tracked position is still at a unit's start in
    /// the new document; a position that was deleted, or that is no longer at a unit's start, is dropped. A table's scroll offset is kept
    /// the same way, at the start of its block. The search matches are not recomputed here (the search does that on its own change).
    /// </summary>
    /// <param name="document">The new document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <c>null</c>.</exception>
    public void SetDocument(RenderDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        bool visible = IsCursorVisible();
        bool hadCursor = HasCursor;
        int anchor = visible || !hadCursor ? _offset : TopOffset;
        int row = visible ? _line - _top : 0;

        _document = document;
        AdoptTrackedState(document);
        int length = document.Text.Length;
        _offset = Math.Min(_offset, length);
        _anchor = _anchor is { } start ? Math.Min(start, length) : null;
        _range = null;

        int width = Viewport.Width;
        if (width > 0 && _layout is not null)
        {
            Rebuild(width, Math.Min(anchor, length), row, hadCursor);
        }
        else
        {
            _layout = null;
        }

        SetNeedsDraw();
        PositionChanged?.Invoke();
    }

    /// <summary>
    /// Makes the tracked positions of the fold state and the scroll offsets the keys of the new document: a folded unit stays folded when its
    /// position is at the start of a unit of the document, and a scroll offset is kept when its position is at a block's start.
    /// </summary>
    /// <param name="document">The new document.</param>
    private void AdoptTrackedState(RenderDocument document)
    {
        var starts = new HashSet<int>(document.FoldUnits.Select(unit => unit.Start));
        var folded = new HashSet<int>();
        var foldHandles = new Dictionary<int, ITrackedOffset>();
        foreach (ITrackedOffset handle in _foldHandles.Values)
        {
            if (!handle.IsDeleted && starts.Contains(handle.Offset) && folded.Add(handle.Offset))
            {
                foldHandles[handle.Offset] = handle;
            }
        }

        var blockStarts = new HashSet<int>(document.Blocks.Select(block => block.Start));
        var shift = new Dictionary<int, int>();
        var shiftHandles = new Dictionary<int, ITrackedOffset>();
        foreach ((int key, ITrackedOffset handle) in _shiftHandles)
        {
            if (!handle.IsDeleted && blockStarts.Contains(handle.Offset) && !shift.ContainsKey(handle.Offset))
            {
                shift[handle.Offset] = _shift.GetValueOrDefault(key);
                shiftHandles[handle.Offset] = handle;
            }
        }

        _folded.Clear();
        _folded.UnionWith(folded);
        _foldHandles = foldHandles;
        _shift.Clear();
        foreach ((int key, int value) in shift)
        {
            _shift[key] = value;
        }

        _shiftHandles = shiftHandles;
    }

    /// <summary>
    /// Creates a tracked position for each folded unit and each scrolling block that has none, and drops the positions of those that no
    /// longer have a key. Called after every change of the fold state or the scroll offsets.
    /// </summary>
    private void SyncTracking()
    {
        _foldHandles = TrackKeys(_folded, _foldHandles);
        _shiftHandles = TrackKeys(_shift.Keys, _shiftHandles);
    }

    /// <summary>Returns the positions for a set of keys: the existing position of each key that has one, else a new one at the key.</summary>
    /// <param name="keys">The keys, which are offsets of the current document.</param>
    /// <param name="existing">The positions so far, by key.</param>
    private Dictionary<int, ITrackedOffset> TrackKeys(IEnumerable<int> keys, Dictionary<int, ITrackedOffset> existing)
    {
        var result = new Dictionary<int, ITrackedOffset>();
        foreach (int key in keys)
        {
            result[key] = existing.TryGetValue(key, out ITrackedOffset? handle) && !handle.IsDeleted ? handle : _tracker.Track(key);
        }

        return result;
    }
}
