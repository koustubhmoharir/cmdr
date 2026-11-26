using System.Globalization;
using System.Text.RegularExpressions;
using Mdedit.Core.Render;

namespace Mdedit.Tui;

/// <summary>
/// The length in the source of the rendered cell that starts at an offset: the whole reference of an HTML entity, else the grapheme
/// cluster of the source. A selection's end and a word's end are the offset of their last cell plus this length, the same rule the
/// search uses for its matches (see <see cref="RenderSearch"/>).
/// </summary>
internal static class SourceCells
{
    /// <summary>An HTML entity in the source: a named, decimal or hexadecimal reference, with its semicolon.</summary>
    private static readonly Regex EntityPattern = new(@"\G&(?:#[0-9]+|#[xX][0-9A-Fa-f]+|[A-Za-z][A-Za-z0-9]*);", RegexOptions.CultureInvariant);

    /// <summary>
    /// The number of source characters of the cell at an offset: the entity's whole reference when one starts there, else the grapheme
    /// cluster. Zero at the end of the text.
    /// </summary>
    /// <param name="document">The document whose source the offset refers to.</param>
    /// <param name="offset">The absolute source offset of the cell's first character.</param>
    public static int Length(RenderDocument document, int offset)
    {
        ArgumentNullException.ThrowIfNull(document);

        string text = document.Text;
        if (offset < 0 || offset >= text.Length)
        {
            return 0;
        }

        if (text[offset] == '&')
        {
            Match entity = EntityPattern.Match(text, offset);
            if (entity.Success)
            {
                return entity.Length;
            }
        }

        return StringInfo.GetNextTextElementLength(text.AsSpan(offset));
    }
}
