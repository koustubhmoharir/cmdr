namespace Mdedit.Core.Render;

/// <summary>
/// One mapped cell of a block's layout, as the source map's index holds it: its relative source offset, where it is (the line
/// within the block, the index of the cell within that line) and its screen column.
/// </summary>
/// <param name="Offset">The source offset relative to the block's start.</param>
/// <param name="Line">The line within the block.</param>
/// <param name="Cell">The index of the cell within its line.</param>
/// <param name="Column">The screen column of the cell.</param>
internal readonly record struct MappedCell(int Offset, int Line, int Cell, int Column);
