namespace Mdedit.Core.Render;

/// <summary>One heading of the outline of a document.</summary>
/// <param name="Level">The heading level, 1 to 6.</param>
/// <param name="Text">The rendered text of the heading, trimmed, with its inline markup removed and no fold glyph.</param>
/// <param name="Start">The absolute source offset of the heading's first character.</param>
/// <param name="InContainer">Whether the heading sits in a container, such as a block quote or a list item.</param>
public sealed record OutlineEntry(int Level, string Text, int Start, bool InContainer);
