using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// One row of a <see cref="TableModel"/>: its source line and its cells. The delimiter row is not a row of the model.
/// </summary>
/// <param name="IsHeader">Whether the row is a header row; its cells are drawn in bold.</param>
/// <param name="Start">
/// The source offset, relative to the start of the block, of the row's source line: where the row's lines anchor. The first
/// row of a block starts at the block's start.
/// </param>
/// <param name="Cells">
/// One run per column, in column order. A cell is lowered like a paragraph's inline content, with offsets relative to the
/// block. Cells past the last column are dropped; a row with fewer cells gets empty runs. Each run's start is <paramref name="Start"/>.
/// </param>
/// <param name="Positions">
/// The source position of each column's content, relative to the block, or <see cref="Cell.Synthetic"/> for a column the row does not
/// have. For an empty cell this is its place between its pipes, where the layout maps a blank so that the caret can rest on the cell
/// (see <see cref="TableLayouter"/>). Empty when the model is built without positions.
/// </param>
public sealed record TableRowModel(bool IsHeader, int Start, ImmutableArray<Run> Cells, ImmutableArray<int> Positions = default);
