using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>The outline of a document: its headings in document order.</summary>
public static class Outline
{
    /// <summary>
    /// The headings of a document, ATX and setext, in document order, including those in containers and in folded sections. A
    /// heading's text is its cells' text, so emphasis and code spans appear without their markup; a heading with a line break
    /// (a setext heading over several lines) joins its lines with a space.
    /// </summary>
    /// <param name="doc">The document.</param>
    public static ImmutableArray<OutlineEntry> Of(RenderDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var entries = ImmutableArray.CreateBuilder<OutlineEntry>();
        foreach (RenderBlock block in doc.Blocks)
        {
            if (block.Kind != BlockKind.Heading)
            {
                continue;
            }

            string text = string.Join(" ", block.Runs.Select(run => string.Concat(run.Cells.Select(cell => cell.Text))));
            entries.Add(new OutlineEntry(block.HeadingLevel, text.Trim(), block.Start, block.Containers.Length > 0));
        }

        return entries.ToImmutable();
    }
}
