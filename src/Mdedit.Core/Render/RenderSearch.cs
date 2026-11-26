using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Shared.Core;

namespace Mdedit.Core.Render;

/// <summary>
/// Finds a pattern in the rendered text of a document and maps each match to its source range. Each run of a block is searched
/// on its own, so a match never crosses a hard break, a block or a table cell; a soft break is a space and can be matched. Prefixes,
/// bullets, fold glyphs and rules are not in the text, and the synthetic cells that are (the image marker, the space after a task
/// box) are searched but are trimmed from a match's ends.
/// </summary>
public static class RenderSearch
{
    /// <summary>An HTML entity in the source: a named, decimal or hexadecimal reference, with its semicolon.</summary>
    private static readonly Regex EntityPattern = new(@"\G&(?:#[0-9]+|#[xX][0-9A-Fa-f]+|[A-Za-z][A-Za-z0-9]*);", RegexOptions.CultureInvariant);

    /// <summary>
    /// The matches of a pattern in the rendered text of the document, in document order, as absolute source ranges. A match
    /// covering only synthetic cells is dropped. A match whose first or last cell is synthetic is trimmed to its mapped cells.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="pattern">The pattern, from <see cref="SearchPattern.Create"/>.</param>
    public static RenderSearchResult Find(RenderDocument doc, SearchPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(pattern);

        var matches = ImmutableArray.CreateBuilder<SourceRange>();
        foreach (RenderBlock block in doc.Blocks)
        {
            foreach (Run run in block.Runs)
            {
                if (run.Cells.IsEmpty)
                {
                    continue;
                }

                AddMatches(doc, block, run, pattern, matches);
            }
        }

        return new RenderSearchResult(matches.ToImmutable(), pattern.TimedOut);
    }

    /// <summary>Searches one run's text and adds the mapped matches of its cells.</summary>
    private static void AddMatches(RenderDocument doc, RenderBlock block, Run run, SearchPattern pattern, ImmutableArray<SourceRange>.Builder matches)
    {
        var text = new StringBuilder();
        var cellOfChar = new List<int>();
        for (int index = 0; index < run.Cells.Length; index++)
        {
            string cellText = run.Cells[index].Text;
            text.Append(cellText);
            for (int k = 0; k < cellText.Length; k++)
            {
                cellOfChar.Add(index);
            }
        }

        foreach (SearchMatch found in pattern.Matches(text.ToString()))
        {
            int first = cellOfChar[found.Start];
            int last = cellOfChar[found.End - 1];
            while (first <= last && !run.Cells[first].IsMapped)
            {
                first++;
            }

            while (last >= first && !run.Cells[last].IsMapped)
            {
                last--;
            }

            if (first > last)
            {
                continue;
            }

            int start = block.Start + run.Cells[first].Offset;
            int end = block.Start + run.Cells[last].Offset + SourceLength(doc, block, run, last);
            matches.Add(new SourceRange(start, end));
        }
    }

    /// <summary>
    /// The length in the source of the cell at <paramref name="index"/>: an entity is its whole reference (when no other cell of the
    /// run is inside it, so that an escaped ampersand followed by text is not one), and any other cell is the grapheme of the source
    /// at its offset. The markup around a cell (emphasis delimiters, the backslash of an escape) is not part of the cell's length.
    /// </summary>
    private static int SourceLength(RenderDocument doc, RenderBlock block, Run run, int index)
    {
        Cell cell = run.Cells[index];
        int absolute = block.Start + cell.Offset;
        int limit = Math.Min(doc.Text.Length, block.End);
        if (absolute >= limit)
        {
            return 1;
        }

        if (doc.Text[absolute] == '&')
        {
            Match entity = EntityPattern.Match(doc.Text, absolute);
            int length = entity.Success ? Math.Min(entity.Length, limit - absolute) : 0;
            bool otherCellInside = run.Cells.Any(other => other.IsMapped && other.Offset > cell.Offset && other.Offset < cell.Offset + length);
            if (length > 0 && !otherCellInside)
            {
                return length;
            }
        }

        return StringInfo.GetNextTextElementLength(doc.Text.AsSpan(absolute, limit - absolute));
    }
}
