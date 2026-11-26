using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// One screen row of the rendered view: its cells in order and where the cursor goes on it. Offsets are relative to the
/// start of the block that owns the row; <see cref="DocumentLayout"/> adds the base. Immutable.
/// </summary>
/// <param name="Cells">The cells of the row. Synthetic cells (prefixes, markers, rules) have the offset <see cref="Cell.Synthetic"/>.</param>
/// <param name="Anchor">
/// The offset, relative to the base, that the cursor goes to on this row when it has no mapped cell or is after its last one:
/// the offset of the first mapped cell after the row in the block, or the block's length when there is none.
/// </param>
/// <param name="IsContinuation">Whether the row is a line produced by wrapping, which continues the row before it.</param>
/// <param name="PrefixCells">
/// The number of leading cells that are the prefix of the line: the gutter (fold glyph or blanks), then the container prefix (quote
/// bars, list markers and their indentation). The cells after them are content, which is what a horizontal scroll moves. Code
/// wrap markers are content.
/// </param>
public sealed record DisplayLine(ImmutableArray<Cell> Cells, int Anchor, bool IsContinuation, int PrefixCells);
