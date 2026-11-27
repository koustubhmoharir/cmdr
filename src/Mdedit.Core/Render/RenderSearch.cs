using System.Collections.Immutable;
using System.Text;
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
            int end = block.Start + run.Cells[last].Offset + SourceLength.Of(doc, block, run, last);
            matches.Add(new SourceRange(start, end));
        }
    }
}
