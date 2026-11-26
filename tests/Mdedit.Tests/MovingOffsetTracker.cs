using Mdedit.Tui;

namespace Mdedit.Tests;

/// <summary>
/// A tracker for the view's positions that the test moves by hand, as the buffer moves its anchors: a position at or after an insertion
/// moves after the inserted text, and a position inside a deletion is deleted.
/// </summary>
internal sealed class MovingOffsetTracker : IOffsetTracker
{
    private readonly List<Position> _positions = [];

    /// <summary>Starts tracking an offset.</summary>
    public ITrackedOffset Track(int offset)
    {
        var position = new Position(offset);
        _positions.Add(position);
        return position;
    }

    /// <summary>Inserts text of a length at an offset: the positions at or after it move past the text.</summary>
    public void Insert(int at, int length)
    {
        foreach (Position position in _positions.Where(p => !p.IsDeleted && p.Offset >= at))
        {
            position.Offset += length;
        }
    }

    /// <summary>Deletes a range: the positions inside it are deleted, and those after it move back.</summary>
    public void Remove(int start, int length)
    {
        foreach (Position position in _positions.Where(p => !p.IsDeleted))
        {
            if (position.Offset >= start && position.Offset < start + length)
            {
                position.IsDeleted = true;
            }
            else if (position.Offset >= start + length)
            {
                position.Offset -= length;
            }
        }
    }

    private sealed class Position(int offset) : ITrackedOffset
    {
        public int Offset { get; set; } = offset;

        public bool IsDeleted { get; set; }
    }
}
