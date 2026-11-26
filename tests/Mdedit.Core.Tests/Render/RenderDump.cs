using System.Text;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>
/// Compact renderings of blocks and runs for exact assertions. A cell is <c>text@offset</c>, with the offset absolute in the
/// document (the block's start plus the relative offset) or <c>-</c> when synthetic, then <c>/Role</c> when the role is not
/// <see cref="StyleRole.Body"/>, <c>+flags</c>, <c>#level</c> in a heading and <c>^link</c> in a link. Cells are separated by
/// <c>|</c>.
/// </summary>
internal static class RenderDump
{
    /// <summary>The cells of one run, as described above.</summary>
    public static string Cells(RenderBlock block, int run) =>
        string.Join("|", block.Runs[run].Cells.Select(cell => Describe(cell, block.Start)));

    /// <summary>The text of one run: its cell texts concatenated.</summary>
    public static string Text(RenderBlock block, int run) =>
        string.Concat(block.Runs[run].Cells.Select(cell => cell.Text));

    /// <summary>The text of every run, separated by <c>/</c>, so that the run boundaries show.</summary>
    public static string Texts(RenderBlock block) =>
        string.Join("/", Enumerable.Range(0, block.Runs.Length).Select(run => Text(block, run)));

    /// <summary>The absolute source offsets of a run's cells, comma separated; <c>-</c> for a synthetic cell.</summary>
    public static string Offsets(RenderBlock block, int run) =>
        string.Join(",", block.Runs[run].Cells.Select(cell => cell.Offset < 0 ? "-" : (block.Start + cell.Offset).ToString()));

    /// <summary>The kind, span and flags of a block, for asserting the block structure.</summary>
    public static string Shape(RenderBlock block) =>
        $"{block.Kind} {block.Start}..{block.End} level={block.HeadingLevel} lang={block.Language ?? "-"} " +
        $"tight={Flag(block.Tight)} first={Flag(block.IsFirstInItem)} containers=[{Containers(block)}]";

    /// <summary>The containers of a block, outermost first, separated by spaces: <c>quote</c>, <c>alert:note</c>,
    /// <c>bullet[depth]</c>, <c>ordered:marker@offset</c>, with <c>*</c> after the ones the leaf is the first leaf of.</summary>
    public static string Containers(RenderBlock block) =>
        block.Containers.Length == 0
            ? "-"
            : string.Join(" ", block.Containers.Select(entry => ContainerText(entry, block.Start)));

    private static string ContainerText(Container entry, int blockStart)
    {
        string text = entry.Kind switch
        {
            ContainerKind.Quote => "quote",
            ContainerKind.Alert => "alert:" + entry.Alert.ToString().ToLowerInvariant(),
            ContainerKind.BulletItem => $"bullet[{entry.BulletDepth}]",
            ContainerKind.OrderedItem => $"ordered:{entry.Marker}@{blockStart + entry.MarkerOffset}",
            _ => entry.Kind.ToString(),
        };
        return entry.IsFirstLeaf ? text + "*" : text;
    }

    private static string Describe(Cell cell, int blockStart)
    {
        var text = new StringBuilder(cell.Text);
        text.Append('@').Append(cell.Offset < 0 ? "-" : (blockStart + cell.Offset).ToString());
        if (cell.Style.Role != StyleRole.Body)
        {
            text.Append('/').Append(cell.Style.Role);
        }

        if (cell.Style.Inline != InlineStyle.None)
        {
            text.Append('+').Append(string.Join("+", FlagNames(cell.Style.Inline)));
        }

        if (cell.Style.HeadingLevel != 0)
        {
            text.Append('#').Append(cell.Style.HeadingLevel);
        }

        if (cell.LinkId >= 0)
        {
            text.Append('^').Append(cell.LinkId);
        }

        return text.ToString();
    }

    private static IEnumerable<string> FlagNames(InlineStyle style)
    {
        if (style.HasFlag(InlineStyle.Emphasis)) yield return "em";
        if (style.HasFlag(InlineStyle.Strong)) yield return "strong";
        if (style.HasFlag(InlineStyle.Strikethrough)) yield return "del";
        if (style.HasFlag(InlineStyle.Code)) yield return "code";
        if (style.HasFlag(InlineStyle.Link)) yield return "link";
        if (style.HasFlag(InlineStyle.Image)) yield return "img";
    }

    private static string Flag(bool value) => value ? "yes" : "no";
}
