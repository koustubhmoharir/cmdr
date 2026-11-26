using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// Finds the fold units of a document: a section for each container-free heading, and a code unit for each code block, in
/// document order.
/// </summary>
internal static class FoldUnitFinder
{
    /// <summary>The fold units of a document, in order of their head blocks.</summary>
    /// <param name="doc">The document.</param>
    public static ImmutableArray<FoldUnit> Of(RenderDocument doc)
    {
        var lines = new LineIndex(doc.Text);
        var units = ImmutableArray.CreateBuilder<FoldUnit>();
        for (int head = 0; head < doc.Blocks.Length; head++)
        {
            RenderBlock block = doc.Blocks[head];
            if (block.Kind == BlockKind.Code)
            {
                units.Add(new FoldUnit(FoldUnitKind.Code, block.Start, head, head + 1, block.Runs.Length));
            }
            else if (IsSectionHead(block))
            {
                int end = head + 1;
                while (end < doc.Blocks.Length && !EndsSection(doc.Blocks[end], block.HeadingLevel))
                {
                    end++;
                }

                RenderBlock last = doc.Blocks[end - 1];
                int hidden = lines.LineOf(LastOffset(last)) - lines.LineOf(LastOffset(block));
                units.Add(new FoldUnit(FoldUnitKind.Section, block.Start, head, end, hidden));
            }
        }

        return units.ToImmutable();
    }

    private static bool IsSectionHead(RenderBlock block) => block.Kind == BlockKind.Heading && block.Containers.IsEmpty;

    private static bool EndsSection(RenderBlock block, int level) => IsSectionHead(block) && block.HeadingLevel <= level;

    /// <summary>The offset of a block's last character, or its start for an empty block.</summary>
    private static int LastOffset(RenderBlock block) => Math.Max(block.Start, block.End - 1);
}
