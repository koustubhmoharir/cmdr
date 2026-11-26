namespace Mdedit.Tui;

/// <summary>A position in the source text that moves with the edits made before it (see <see cref="IOffsetTracker"/>).</summary>
internal interface ITrackedOffset
{
    /// <summary>The position's current offset in the text. It is meaningless once <see cref="IsDeleted"/> is true.</summary>
    int Offset { get; }

    /// <summary>Whether the text around the position was deleted, so that the position is gone.</summary>
    bool IsDeleted { get; }
}
