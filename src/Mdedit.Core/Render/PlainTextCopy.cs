using System.Text;

namespace Mdedit.Core.Render;

/// <summary>
/// The plain text of a source range: the selected cells of each block, block by block. Within a block the runs are lines (a
/// hard break ends one), table rows are lines of cells joined by tabs, and code lines are lines without their fence or header.
/// </summary>
internal static class PlainTextCopy
{
    /// <summary>The plain text of <c>[start, end)</c>, which is already clamped to the document.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="start">The first offset of the range.</param>
    /// <param name="end">The offset after the range.</param>
    public static string Of(RenderDocument doc, int start, int end)
    {
        var text = new StringBuilder();
        RenderBlock? previous = null;
        foreach (RenderBlock block in doc.Blocks)
        {
            if (block.Start >= end || block.End < start)
            {
                continue;
            }

            string? blockText = BlockText(block, start, end);
            if (blockText is null)
            {
                continue;
            }

            if (previous is not null)
            {
                text.Append(previous.Tight && block.Tight ? "\n" : "\n\n");
            }

            text.Append(blockText);
            previous = block;
        }

        return text.ToString();
    }

    /// <summary>The text of one block in the range, or null when the range selects nothing of it.</summary>
    private static string? BlockText(RenderBlock block, int start, int end)
    {
        var lines = new List<string>();
        if (block.Table is { } table)
        {
            foreach (TableRowModel row in table.Rows)
            {
                var cells = new List<string>();
                foreach (Run cell in row.Cells)
                {
                    (string text, bool selected) = SelectedText(cell, block.Start, start, end);
                    if (selected)
                    {
                        cells.Add(text);
                    }
                }

                if (cells.Count > 0)
                {
                    lines.Add(string.Join('\t', cells));
                }
            }
        }
        else
        {
            bool lineBased = block.Kind is BlockKind.Code or BlockKind.Html or BlockKind.Raw;
            foreach (Run run in block.Runs)
            {
                (string text, bool selected) = SelectedText(run, block.Start, start, end);
                bool emptyLineInRange = lineBased && run.Cells.IsEmpty && InRange(block.Start + run.Start, start, end);
                if (selected || emptyLineInRange)
                {
                    lines.Add(text);
                }
            }
        }

        if (lines.Count == 0)
        {
            return null;
        }

        return Prefix(block, start) + string.Join('\n', lines);
    }

    /// <summary>
    /// The list prefix of a block, when the range starts at or before its first mapped character: for each list item it is in,
    /// outermost first, the item's marker (<c>- </c>, <c>3) </c>) when the block is the item's first leaf, else spaces as wide as
    /// that marker, so that nested items and later paragraphs of an item are indented as they are on screen.
    /// </summary>
    private static string Prefix(RenderBlock block, int start)
    {
        int firstMapped = FirstMappedOffset(block);
        if (start > firstMapped)
        {
            return string.Empty;
        }

        var prefix = new StringBuilder();
        foreach (Container container in block.Containers)
        {
            string? marker = container.Kind switch
            {
                ContainerKind.BulletItem => "- ",
                ContainerKind.OrderedItem => container.Marker + " ",
                _ => null,
            };
            if (marker is not null)
            {
                prefix.Append(container.IsFirstLeaf ? marker : new string(' ', marker.Length));
            }
        }

        return prefix.ToString();
    }

    /// <summary>The absolute offset of the block's first mapped cell, or the block's start when it has none.</summary>
    private static int FirstMappedOffset(RenderBlock block)
    {
        foreach (Run run in block.Runs)
        {
            foreach (Cell cell in run.Cells)
            {
                if (cell.IsMapped)
                {
                    return block.Start + cell.Offset;
                }
            }
        }

        return block.Start;
    }

    /// <summary>
    /// The text of the selected cells of a run, and whether any mapped cell of it is selected. A mapped cell is selected when its
    /// offset is in the range; a synthetic cell is selected when the next mapped cell after it is, so that the image marker goes
    /// with the alt text it precedes and not with the cells after it.
    /// </summary>
    private static (string Text, bool Selected) SelectedText(Run run, int blockStart, int start, int end)
    {
        var selected = new bool[run.Cells.Length];
        bool nextMapped = false;
        bool any = false;
        for (int index = run.Cells.Length - 1; index >= 0; index--)
        {
            Cell cell = run.Cells[index];
            if (cell.IsMapped)
            {
                nextMapped = InRange(blockStart + cell.Offset, start, end);
                any |= nextMapped;
            }

            selected[index] = nextMapped;
        }

        var text = new StringBuilder();
        for (int index = 0; index < run.Cells.Length; index++)
        {
            if (selected[index])
            {
                text.Append(run.Cells[index].Text);
            }
        }

        return (text.ToString(), any);
    }

    /// <summary>Whether an absolute offset is in <c>[start, end)</c>.</summary>
    private static bool InRange(int offset, int start, int end) => offset >= start && offset < end;
}
