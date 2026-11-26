namespace Mdedit.Tui;

/// <summary>
/// The tracker of a view whose text is not edited: its positions stay where they were created. The view uses it when it is given no
/// tracker, so that a view over a fixed document needs no editor.
/// </summary>
internal sealed class FixedOffsetTracker : IOffsetTracker
{
    /// <summary>The shared instance.</summary>
    public static FixedOffsetTracker Instance { get; } = new();

    /// <summary>Returns a position that stays at the offset.</summary>
    /// <param name="offset">The offset.</param>
    public ITrackedOffset Track(int offset) => new FixedOffset(offset);
}
