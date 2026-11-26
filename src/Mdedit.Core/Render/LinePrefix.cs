using System.Collections.Immutable;
using Shared.Core;

namespace Mdedit.Core.Render;

/// <summary>
/// The prefix of a display line, built from the gutter and then the block's containers outermost first: a bar per quote or alert,
/// and the bullet or the number of a list item on the first line that the marker belongs to. The gutter is the first two columns of
/// every line that has cells, at screen column 0 whatever the nesting: the fold glyph and a space on the first line of a heading or
/// a code block, blanks on every other line. Lines without the marker get blanks of the same width, so that the content of every
/// line starts in the same column.
/// </summary>
internal static class LinePrefix
{
    private const string Bullets = "•◦▪";

    /// <summary>
    /// The gutter of a line: the fold glyph and a space when <paramref name="glyph"/> is set (<c>▾ </c> unfolded, <c>▸ </c> folded), else
    /// two blanks.
    /// </summary>
    /// <param name="glyph">Whether the line is the first line of a heading or a code block, which shows the fold glyph.</param>
    /// <param name="folded">Whether that head is folded, which turns its glyph to the folded one.</param>
    public static ImmutableArray<Cell> Gutter(bool glyph = false, bool folded = false) =>
        glyph
            ? SyntheticCell.Text(folded ? "▸ " : "▾ ", StyleRole.FoldGlyph)
            : SyntheticCell.Text("  ", StyleRole.Body);

    /// <summary>
    /// The prefix of a content line of a block: the gutter, then its containers (see <see cref="Containers"/>). The fold glyph goes on
    /// the first line of a heading or a code block (its header line), when <paramref name="fold"/> is set.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="showMarker">Whether this is the block's first line, which gets the list markers of its first-leaf items.</param>
    /// <param name="fold">Whether this is the block's first content line, which gets the fold glyph when the block is a heading or a code block.</param>
    /// <param name="folded">Whether the block is a folded head, which turns its glyph to the folded one.</param>
    public static ImmutableArray<Cell> Content(RenderBlock block, bool showMarker, bool fold, bool folded = false)
    {
        bool glyph = fold && (block.Kind is BlockKind.Heading or BlockKind.Code);
        return Gutter(glyph, folded).Concat(Containers(block, showMarker)).ToImmutableArray();
    }

    /// <summary>
    /// The container part of the prefix of a content line: a bar per quote or alert, and the marker or the blanks of each list
    /// item. These cells are the prefix that a horizontal scroll keeps in place; the fold glyph of a heading is not among them.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="showMarker">Whether this is the block's first line, which gets the list markers of its first-leaf items.</param>
    public static ImmutableArray<Cell> Containers(RenderBlock block, bool showMarker)
    {
        var cells = ImmutableArray.CreateBuilder<Cell>();
        foreach (Container entry in block.Containers)
        {
            AddContainer(cells, entry, showMarker && entry.IsFirstLeaf);
        }

        return cells.ToImmutable();
    }

    /// <summary>
    /// The prefix of the title line of an alert: the gutter, then the containers of the title. The title line is the block's first
    /// line, so the markers that would be on the first content line go here: the markers of the list items that the title's alert
    /// contains, or that sit outside every alert before it. Quotes nested inside the alert are not part of the title line.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="titles">The chain indexes of the alerts that have a title line (see <see cref="TitleOwner"/>).</param>
    /// <param name="index">Which of <paramref name="titles"/> this title is.</param>
    public static ImmutableArray<Cell> Title(RenderBlock block, ImmutableArray<int> titles, int index)
    {
        ImmutableArray<Cell>.Builder cells = Gutter().ToBuilder();
        int alert = titles[index];
        for (int i = 0; i < block.Containers.Length; i++)
        {
            Container entry = block.Containers[i];
            if (entry.Kind is ContainerKind.Quote or ContainerKind.Alert)
            {
                if (i <= alert)
                {
                    AddContainer(cells, entry, showMarker: false);
                }

                continue;
            }

            if (TitleOwner(titles, i) == index)
            {
                AddItem(cells, entry, entry.IsFirstLeaf);
            }
            else if (i <= alert)
            {
                AddItem(cells, entry, showMarker: false);
            }
        }

        return cells.ToImmutable();
    }

    /// <summary>
    /// The title line that a container's list marker goes on: the last title alert before it, or the first title for the
    /// containers before every title.
    /// </summary>
    /// <param name="titles">The chain indexes of the alerts that have a title line, in order.</param>
    /// <param name="containerIndex">The chain index of the container.</param>
    public static int TitleOwner(ImmutableArray<int> titles, int containerIndex)
    {
        int before = 0;
        foreach (int title in titles)
        {
            if (title < containerIndex)
            {
                before++;
            }
        }

        return Math.Max(0, before - 1);
    }

    /// <summary>
    /// The shared prefix of the blank separator line between two blocks: a bar for each quote or alert that both blocks are in,
    /// behind the gutter. Blanks stand for the list items shared before a bar, so that the bar stays in its column; none are kept
    /// after the last bar. Empty when no bar is shared, so that the separator is an empty line.
    /// </summary>
    /// <param name="previous">The block before the separator.</param>
    /// <param name="next">The block after it.</param>
    public static ImmutableArray<Cell> Separator(RenderBlock previous, RenderBlock next)
    {
        var cells = ImmutableArray.CreateBuilder<Cell>();
        int keep = 0;
        int common = Math.Min(previous.Containers.Length, next.Containers.Length);
        for (int i = 0; i < common; i++)
        {
            Container a = previous.Containers[i];
            Container b = next.Containers[i];
            if (a.Kind != b.Kind || a.Alert != b.Alert || a.BulletDepth != b.BulletDepth)
            {
                break;
            }

            AddContainer(cells, a, showMarker: false);
            if (a.Kind is ContainerKind.Quote or ContainerKind.Alert)
            {
                keep = cells.Count;
            }
        }

        // Blanks after the last bar are dropped, so that a separator between list items stays an empty line. A separator with a bar
        // has the gutter in front of it like every line with cells.
        ImmutableArray<Cell> bars = cells.Take(keep).ToImmutableArray();
        return bars.IsEmpty ? bars : Gutter().Concat(bars).ToImmutableArray();
    }

    /// <summary>The display width of a prefix.</summary>
    public static int Width(ImmutableArray<Cell> prefix)
    {
        int width = 0;
        foreach (Cell cell in prefix)
        {
            width += cell.Width;
        }

        return width;
    }

    private static void AddContainer(ImmutableArray<Cell>.Builder cells, Container entry, bool showMarker)
    {
        if (entry.Kind is ContainerKind.Quote or ContainerKind.Alert)
        {
            cells.AddRange(SyntheticCell.Text("│ ", StyleRole.QuoteBar));
        }
        else
        {
            AddItem(cells, entry, showMarker);
        }
    }

    /// <summary>
    /// The marker of a list item, or blanks of its width. A bullet is its glyph by depth and a space. An ordered item is its
    /// number as written, mapped to the source, and a space.
    /// </summary>
    private static void AddItem(ImmutableArray<Cell>.Builder cells, Container entry, bool showMarker)
    {
        if (entry.Kind == ContainerKind.BulletItem)
        {
            string glyph = Bullets[entry.BulletDepth % Bullets.Length].ToString();
            cells.AddRange(showMarker ? SyntheticCell.Text(glyph + " ", StyleRole.ListMarker) : SyntheticCell.Text("  ", StyleRole.Body));
            return;
        }

        string marker = entry.Marker ?? string.Empty;
        if (!showMarker)
        {
            cells.AddRange(SyntheticCell.Text(new string(' ', DisplayWidth.Of(marker) + 1), StyleRole.Body));
            return;
        }

        for (int i = 0; i < marker.Length; i++)
        {
            string digit = marker.Substring(i, 1);
            cells.Add(new Cell(digit, DisplayWidth.OfGrapheme(digit.AsSpan()), new CellStyle(StyleRole.ListMarker), entry.MarkerOffset + i, -1));
        }

        cells.AddRange(SyntheticCell.Text(" ", StyleRole.ListMarker));
    }
}
