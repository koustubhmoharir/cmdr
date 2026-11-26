using Terminal.Gui.Editor.Document;

namespace Mdedit.Tui;

/// <summary>A tracked position held by a text anchor of the editor's buffer: it moves with the insertions and deletions of the buffer.</summary>
/// <param name="Anchor">The text anchor that holds the position.</param>
internal sealed record AnchorOffset(TextAnchor Anchor) : ITrackedOffset
{
    /// <summary>The anchor's current offset in the buffer.</summary>
    public int Offset => Anchor.Offset;

    /// <summary>Whether the anchor's text was deleted.</summary>
    public bool IsDeleted => Anchor.IsDeleted;
}
