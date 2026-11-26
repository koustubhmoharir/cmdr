using System.Collections.Immutable;
using System.Globalization;
using Shared.Core;

namespace Mdedit.Core.Render;

/// <summary>
/// Makes the cells the layout draws that stand for no source text: prefixes, titles and rules. Their offset is
/// <see cref="Cell.Synthetic"/>, so the source map skips them.
/// </summary>
internal static class SyntheticCell
{
    /// <summary>One synthetic cell holding <paramref name="grapheme"/>, which must be one grapheme cluster.</summary>
    /// <param name="grapheme">The text of the cell.</param>
    /// <param name="role">The role the cell is drawn in.</param>
    public static Cell Make(string grapheme, StyleRole role) =>
        new(grapheme, DisplayWidth.OfGrapheme(grapheme.AsSpan()), new CellStyle(role), Cell.Synthetic, -1);

    /// <summary>One synthetic cell per grapheme cluster of <paramref name="text"/>, all in <paramref name="role"/>.</summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="role">The role the cells are drawn in.</param>
    public static ImmutableArray<Cell> Text(string text, StyleRole role)
    {
        var cells = ImmutableArray.CreateBuilder<Cell>();
        for (int index = 0; index < text.Length;)
        {
            int length = StringInfo.GetNextTextElementLength(text.AsSpan(index));
            cells.Add(Make(text.Substring(index, length), role));
            index += length;
        }

        return cells.ToImmutable();
    }
}
