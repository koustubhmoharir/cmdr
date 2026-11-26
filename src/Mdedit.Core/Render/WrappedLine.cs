using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// One line of wrapped content, before its prefix and anchor are added. <see cref="LastPlaced"/> indexes the block's flat cell
/// list (the cells of its runs in order), so the anchor can be found from the last cell the line shows.
/// </summary>
/// <param name="Cells">The cells the line shows, in order, without its prefix.</param>
/// <param name="LastPlaced">The flat index of the last source cell the line shows, or -1 when it shows none.</param>
/// <param name="HasMapped">
/// Whether the line shows a mapped cell of the block's runs, from which its anchor is found. A code header's label may map the
/// block's start without setting this: the header keeps the fallback anchor, which is that start.
/// </param>
/// <param name="IsContinuation">Whether the line continues the line before it by wrapping.</param>
internal sealed record WrappedLine(ImmutableArray<Cell> Cells, int LastPlaced, bool HasMapped, bool IsContinuation);
