using Terminal.Gui.Editor.Document;

namespace Mdedit.Tui;

/// <summary>
/// The tracker of the window's rendered view: its positions are text anchors of the source buffer, so they move with every edit of
/// either mode and with undo and redo. An anchor in front of an insertion stays in front of the inserted text's end, so a fold at a
/// section's start stays at the section when text is inserted just above it.
/// </summary>
/// <param name="buffer">The buffer whose text the view shows.</param>
internal sealed class AnchorOffsetTracker(TextDocument buffer) : IOffsetTracker
{
    /// <summary>Creates a text anchor at the offset of the buffer.</summary>
    /// <param name="offset">The offset in the buffer's text.</param>
    public ITrackedOffset Track(int offset) => new AnchorOffset(buffer.CreateAnchor(offset));
}
