namespace Mdedit.Tui;

/// <summary>
/// A place in a file that Alt+Left returns to: the file's full path, and the source offsets of the caret and of the top line's first cell when
/// the window left the file by following a link.
/// </summary>
/// <param name="Path">The full path of the file.</param>
/// <param name="CaretOffset">The caret's absolute source offset.</param>
/// <param name="TopOffset">The absolute source offset of the top line's first mapped cell.</param>
internal sealed record BackEntry(string Path, int CaretOffset, int TopOffset);
