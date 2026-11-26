namespace Mdedit.Tui;

/// <summary>A tracked position that does not move: the position of a text that is not edited (see <see cref="FixedOffsetTracker"/>).</summary>
/// <param name="Offset">The offset the position stays at.</param>
internal sealed record FixedOffset(int Offset) : ITrackedOffset
{
    /// <summary>Always false: a fixed position is never deleted.</summary>
    public bool IsDeleted => false;
}
