namespace Mdedit.Tui;

/// <summary>
/// Creates positions in the source text that move with the edits made to it. The rendered view keeps its fold state and its table scroll
/// offsets as such positions, so that they follow the text when it changes. The window implements it over the editor's buffer; tests use
/// an implementation that moves its positions by hand.
/// </summary>
internal interface IOffsetTracker
{
    /// <summary>Starts tracking an offset of the current text. The position moves with the edits that happen before it.</summary>
    /// <param name="offset">The offset, in the current text.</param>
    /// <returns>The tracked position.</returns>
    ITrackedOffset Track(int offset);
}
