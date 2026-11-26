using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// Lays out the lines of one block: the title line of each alert the block starts, then the content of its kind, every line
/// with its prefix and its anchor. Offsets stay relative to the block's start.
/// </summary>
internal static class BlockLayouter
{
    /// <summary>The display lines of a block at a width, with relative offsets.</summary>
    /// <param name="block">The block.</param>
    /// <param name="width">The full screen width.</param>
    public static ImmutableArray<DisplayLine> Layout(RenderBlock block, int width)
    {
        ImmutableArray<int> titles = TitleAlerts(block.Containers);
        int contentWidth = ContentWidth(block, width);
        var lines = ImmutableArray.CreateBuilder<DisplayLine>();

        for (int index = 0; index < titles.Length; index++)
        {
            ImmutableArray<Cell> title = SyntheticCell.Text(TitleText(block.Containers[titles[index]].Alert), StyleRole.AlertTitle);
            lines.Add(Line(LinePrefix.Title(block, titles, index), title, 0, false));
        }

        bool first = true;
        foreach ((ImmutableArray<Cell> content, int anchor, bool continuation) in ContentLines(block, contentWidth))
        {
            bool showMarker = first && titles.IsEmpty;
            ImmutableArray<Cell> prefix = LinePrefix.Content(block, showMarker, fold: first);
            int containers = LinePrefix.Containers(block, showMarker).Length;
            lines.Add(new DisplayLine(prefix.Concat(content).ToImmutableArray(), anchor, continuation, containers));
            first = false;
        }

        return lines.ToImmutable();
    }

    /// <summary>The width left for content after the prefix of a block's lines, at least 1.</summary>
    /// <param name="block">The block.</param>
    /// <param name="width">The full screen width.</param>
    public static int ContentWidth(RenderBlock block, int width) =>
        Math.Max(1, width - LinePrefix.Width(LinePrefix.Content(block, showMarker: false, fold: false)));

    /// <summary>
    /// The content lines of a block, without their prefix, each with its anchor and whether it continues the line before it. A
    /// table is drawn as a grid; the other kinds are wrapped runs.
    /// </summary>
    private static List<(ImmutableArray<Cell> Content, int Anchor, bool IsContinuation)> ContentLines(RenderBlock block, int width)
    {
        if (block.Kind == BlockKind.Table)
        {
            return TableLayouter.Lines(block, width);
        }

        ImmutableArray<Cell> flat = block.Runs.SelectMany(run => run.Cells).ToImmutableArray();
        int length = block.End - block.Start;
        var lines = new List<(ImmutableArray<Cell>, int, bool)>();
        foreach ((WrappedLine line, int fallback) in WrappedContent(block, width))
        {
            lines.Add((line.Cells, AnchorOf(line, fallback, flat, length), line.IsContinuation));
        }

        return lines;
    }

    /// <summary>
    /// The wrapped content lines of a paragraph, heading, code, thematic break, HTML or raw block, each with the anchor it falls
    /// back to when it shows no mapped cell (the start of its run, or 0 for a synthetic line).
    /// </summary>
    private static List<(WrappedLine Line, int Fallback)> WrappedContent(RenderBlock block, int width)
    {
        var lines = new List<(WrappedLine, int)>();
        int index = 0;
        switch (block.Kind)
        {
            case BlockKind.Paragraph:
            case BlockKind.Heading:
                foreach (Run run in block.Runs)
                {
                    AddRun(lines, TextWrap.Words(run.Cells, index, width), run.Start);
                    index += run.Cells.Length;
                }

                break;

            case BlockKind.Code:
                lines.Add((Synthetic(SyntheticCell.Text("▾ " + (block.Language ?? "code"), StyleRole.CodeHeader)), 0));
                foreach (Run run in block.Runs)
                {
                    AddRun(lines, TextWrap.Cells(run.Cells, index, width, marked: true), run.Start);
                    index += run.Cells.Length;
                }

                break;

            case BlockKind.ThematicBreak:
                lines.Add((Synthetic(SyntheticCell.Text(new string('─', width), StyleRole.ThematicBreak)), 0));
                break;

            default:
                // HTML and raw blocks: their source lines, wrapped at cell boundaries with no marker.
                foreach (Run run in block.Runs)
                {
                    AddRun(lines, TextWrap.Cells(run.Cells, index, width, marked: false), run.Start);
                    index += run.Cells.Length;
                }

                break;
        }

        return lines;
    }

    private static DisplayLine Line(ImmutableArray<Cell> prefix, ImmutableArray<Cell> content, int anchor, bool continuation) =>
        new(prefix.Concat(content).ToImmutableArray(), anchor, continuation, prefix.Length);

    private static void AddRun(List<(WrappedLine, int)> lines, List<WrappedLine> wrapped, int fallback)
    {
        foreach (WrappedLine line in wrapped)
        {
            lines.Add((line, fallback));
        }
    }

    private static WrappedLine Synthetic(ImmutableArray<Cell> cells) => new(cells, -1, false, false);

    /// <summary>
    /// The anchor of a line: the first mapped cell after the last source cell the line shows, which is the source text the
    /// cursor reaches at the end of the line. A line with no mapped cell uses its fallback.
    /// </summary>
    private static int AnchorOf(WrappedLine line, int fallback, ImmutableArray<Cell> flat, int length)
    {
        if (!line.HasMapped)
        {
            return fallback;
        }

        for (int index = line.LastPlaced + 1; index < flat.Length; index++)
        {
            if (flat[index].IsMapped)
            {
                return flat[index].Offset;
            }
        }

        return length;
    }

    /// <summary>The chain indexes of the alerts that start a block with their first leaf, outermost first.</summary>
    private static ImmutableArray<int> TitleAlerts(ImmutableArray<Container> containers)
    {
        var titles = ImmutableArray.CreateBuilder<int>();
        for (int index = 0; index < containers.Length; index++)
        {
            if (containers[index].Kind == ContainerKind.Alert && containers[index].IsFirstLeaf)
            {
                titles.Add(index);
            }
        }

        return titles.ToImmutable();
    }

    private static string TitleText(AlertKind kind) => kind switch
    {
        AlertKind.Note => "Note",
        AlertKind.Tip => "Tip",
        AlertKind.Important => "Important",
        AlertKind.Warning => "Warning",
        AlertKind.Caution => "Caution",
        _ => kind.ToString(),
    };
}
