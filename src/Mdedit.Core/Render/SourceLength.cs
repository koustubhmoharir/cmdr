using System.Globalization;
using System.Text.RegularExpressions;

namespace Mdedit.Core.Render;

/// <summary>
/// The length in the source of one cell of a run: the whole reference of an HTML entity when no other cell of the run lies inside it,
/// else the grapheme cluster at the cell's offset. The markup around a cell (emphasis delimiters, the backslash of an escape) is not part
/// of the length. A search match's end and the source a deletion removes both use this rule.
/// </summary>
internal static class SourceLength
{
    /// <summary>An HTML entity in the source: a named, decimal or hexadecimal reference, with its semicolon.</summary>
    private static readonly Regex EntityPattern = new(@"\G&(?:#[0-9]+|#[xX][0-9A-Fa-f]+|[A-Za-z][A-Za-z0-9]*);", RegexOptions.CultureInvariant);

    /// <summary>The source length of the cell at <paramref name="index"/> of <paramref name="run"/>, which belongs to <paramref name="block"/>.</summary>
    /// <param name="doc">The document whose source the offsets refer to.</param>
    /// <param name="block">The block that owns the run.</param>
    /// <param name="run">The run.</param>
    /// <param name="index">The index of the cell in the run.</param>
    public static int Of(RenderDocument doc, RenderBlock block, Run run, int index) => Of(doc.Text, block.Start, block.End, run, index);

    /// <summary>
    /// The source length of the cell at <paramref name="index"/> of <paramref name="run"/>, for a block whose source is given by its text and bounds
    /// (the lowering has no document yet).
    /// </summary>
    /// <param name="text">The source text the offsets refer to.</param>
    /// <param name="blockStart">The absolute offset of the block's start.</param>
    /// <param name="blockEnd">The absolute offset of the block's end, exclusive.</param>
    /// <param name="run">The run.</param>
    /// <param name="index">The index of the cell in the run.</param>
    public static int Of(string text, int blockStart, int blockEnd, Run run, int index)
    {
        Cell cell = run.Cells[index];
        int absolute = blockStart + cell.Offset;
        int limit = Math.Min(text.Length, blockEnd);
        if (absolute >= limit)
        {
            return 1;
        }

        if (text[absolute] == '&')
        {
            Match entity = EntityPattern.Match(text, absolute);
            int length = entity.Success ? Math.Min(entity.Length, limit - absolute) : 0;
            bool otherCellInside = run.Cells.Any(other => other.IsMapped && other.Offset > cell.Offset && other.Offset < cell.Offset + length);
            if (length > 0 && !otherCellInside)
            {
                return length;
            }
        }

        return StringInfo.GetNextTextElementLength(text.AsSpan(absolute, limit - absolute));
    }
}
