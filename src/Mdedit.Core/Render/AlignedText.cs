namespace Mdedit.Core.Render;

/// <summary>
/// Text that Markdig produced, with the source index of each of its characters, as <see cref="Alignment.Align"/> returns it.
/// </summary>
/// <param name="Text">The text the cells are made from. It equals the produced text, except that a character Markdig substituted
/// (U+FFFD for a NUL) is the source character again, so that every cell shows what the source has.</param>
/// <param name="Offsets">The absolute source index of each character of <paramref name="Text"/>.</param>
internal readonly record struct AlignedText(string Text, int[] Offsets);
