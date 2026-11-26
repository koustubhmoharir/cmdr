using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// A sequence of cells with no line structure of its own. A paragraph or heading has one run per line that a hard break
/// ends; a code, HTML or table block has one run per source line. The layout decides where the display lines break.
/// </summary>
/// <param name="Cells">The cells, in source order. Synthetic cells are inside the run where the layout draws them.</param>
/// <param name="Start">
/// The source offset, relative to the start of the block, where the run's source begins: the first character of a source
/// line (after its container prefix and any indentation of the block), the hard break that starts a paragraph line, or 0
/// for the first run of a paragraph or heading. An empty run is anchored here.
/// </param>
public sealed record Run(ImmutableArray<Cell> Cells, int Start);
