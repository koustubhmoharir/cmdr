namespace Mdedit.Core.Render;

/// <summary>
/// A part of a document that can be folded to one line. A section is a container-free heading and the blocks under it; a code
/// unit is one code block. The unit's key in the fold state is its <see cref="Start"/>.
/// </summary>
/// <param name="Kind">What the unit is.</param>
/// <param name="Start">The absolute source offset of the head block: the unit's key in a fold state.</param>
/// <param name="HeadBlock">The index of the head block in <see cref="RenderDocument.Blocks"/>.</param>
/// <param name="EndBlock">
/// The index after the last block of the unit (exclusive). For a code unit it is <paramref name="HeadBlock"/> plus one.
/// </param>
/// <param name="HiddenLines">
/// The source lines hidden when the unit is folded. For a section: the lines from the line after the head block's last line to
/// the last line of the unit's last block, blank lines between included and trailing blank lines after it not included; 0 when
/// the head is the last block. For a code unit: its number of code lines.
/// </param>
public sealed record FoldUnit(FoldUnitKind Kind, int Start, int HeadBlock, int EndBlock, int HiddenLines);
