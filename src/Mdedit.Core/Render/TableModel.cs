using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// The structure of a pipe table: the alignment of each column and its rows, in source order. The cells are the content of a
/// table block; the layout draws the grid from them.
/// </summary>
/// <param name="Alignments">The alignment of each column. Its length is the number of columns.</param>
/// <param name="Rows">The header row, then the body rows, in source order.</param>
public sealed record TableModel(ImmutableArray<ColumnAlignment> Alignments, ImmutableArray<TableRowModel> Rows);
