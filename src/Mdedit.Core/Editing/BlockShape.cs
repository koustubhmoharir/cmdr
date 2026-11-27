using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// One container of a block as the verifier compares it: its kind and the details that make it what it is, with the identity of the container
/// instance it belongs to. Two blocks share an instance when they are in the same quote or item.
/// </summary>
/// <param name="Kind">The kind of container.</param>
/// <param name="Alert">The alert kind, for an alert.</param>
/// <param name="BulletDepth">The bullet depth, for a bullet item.</param>
/// <param name="Marker">The marker as written, for an ordered item.</param>
/// <param name="Identity">The instance the container is, canonical once <see cref="BlockShape.Canonical"/> has renumbered it.</param>
internal readonly record struct ShapeContainer(ContainerKind Kind, AlertKind Alert, int BulletDepth, string? Marker, int Identity);

/// <summary>
/// A leaf block as the structure edits expect it: its kind, its heading level, its containers with their instance identities and its rendered
/// text (see <see cref="BlockText"/>). The verifier compares the whole block list of an edited document with the list an edit expects.
/// </summary>
/// <param name="Kind">The block's kind.</param>
/// <param name="HeadingLevel">The heading level, 0 for other kinds.</param>
/// <param name="Containers">The containers, outermost first.</param>
/// <param name="Text">The rendered text of the block (see <see cref="BlockText.Of"/>).</param>
internal sealed record BlockShape(BlockKind Kind, int HeadingLevel, ImmutableArray<ShapeContainer> Containers, string Text)
{
    /// <summary>Whether two shapes describe the same block: the same kind, level, containers (by value) and text.</summary>
    /// <param name="other">The shape to compare with.</param>
    public bool SameAs(BlockShape other) =>
        Kind == other.Kind && HeadingLevel == other.HeadingLevel && Text == other.Text && Containers.SequenceEqual(other.Containers);

    /// <summary>
    /// The shapes of a document's blocks. A container starts a new instance where its leaf is its first leaf; otherwise it continues the
    /// instance of the block before, at the same depth.
    /// </summary>
    /// <param name="doc">The document.</param>
    public static ImmutableArray<BlockShape> Of(RenderDocument doc)
    {
        var shapes = ImmutableArray.CreateBuilder<BlockShape>(doc.Blocks.Length);
        int next = 0;
        ImmutableArray<ShapeContainer> previous = ImmutableArray<ShapeContainer>.Empty;
        foreach (RenderBlock block in doc.Blocks)
        {
            var chain = ImmutableArray.CreateBuilder<ShapeContainer>(block.Containers.Length);
            for (int depth = 0; depth < block.Containers.Length; depth++)
            {
                Container entry = block.Containers[depth];
                int identity = !entry.IsFirstLeaf && depth < previous.Length ? previous[depth].Identity : next++;
                chain.Add(new ShapeContainer(entry.Kind, entry.Alert, entry.BulletDepth, entry.Marker, identity));
            }

            previous = chain.ToImmutable();
            shapes.Add(new BlockShape(block.Kind, block.HeadingLevel, previous, BlockText.Of(block)));
        }

        return shapes.ToImmutable();
    }

    /// <summary>
    /// The same shapes with their instance identities renumbered in order of first appearance, so that two lists compare equal when they
    /// describe the same grouping of blocks, whatever the numbers were.
    /// </summary>
    /// <param name="shapes">The shapes.</param>
    public static ImmutableArray<BlockShape> Canonical(IEnumerable<BlockShape> shapes)
    {
        var numbers = new Dictionary<int, int>();
        var result = ImmutableArray.CreateBuilder<BlockShape>();
        foreach (BlockShape shape in shapes)
        {
            var chain = ImmutableArray.CreateBuilder<ShapeContainer>(shape.Containers.Length);
            foreach (ShapeContainer entry in shape.Containers)
            {
                if (!numbers.TryGetValue(entry.Identity, out int number))
                {
                    number = numbers.Count;
                    numbers[entry.Identity] = number;
                }

                chain.Add(entry with { Identity = number });
            }

            result.Add(shape with { Containers = chain.ToImmutable() });
        }

        return result.ToImmutable();
    }
}

/// <summary>
/// The rendered text of a block, the text the verifier and the edits compare: the text of each run's cells that are source text (not
/// synthetic, and not the box of a task item), runs joined by line feeds.
/// </summary>
internal static class BlockText
{
    /// <summary>The rendered text of a whole block.</summary>
    /// <param name="block">The block.</param>
    public static string Of(RenderBlock block) => string.Join('\n', block.Runs.Select(RunText));

    /// <summary>
    /// The rendered text of a block on either side of an absolute source offset: the text before it and the text from it on. Runs are joined by
    /// line feeds as <see cref="Of"/> joins them.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="at">The absolute source offset that splits the block.</param>
    public static (string Before, string After) Split(RenderBlock block, int at)
    {
        int run = block.Runs.Length - 1;
        for (int index = 0; index < block.Runs.Length; index++)
        {
            if (block.Runs[index].Cells.Any(cell => Mapped(cell) && block.Start + cell.Offset >= at))
            {
                run = index;
                break;
            }
        }

        var before = new List<string>();
        var after = new List<string>();
        for (int index = 0; index < block.Runs.Length; index++)
        {
            Run line = block.Runs[index];
            if (index < run)
            {
                before.Add(RunText(line));
            }
            else if (index > run)
            {
                after.Add(RunText(line));
            }
            else
            {
                before.Add(string.Concat(line.Cells.Where(cell => Mapped(cell) && block.Start + cell.Offset < at).Select(cell => cell.Text)));
                after.Add(string.Concat(line.Cells.Where(cell => Mapped(cell) && block.Start + cell.Offset >= at).Select(cell => cell.Text)));
            }
        }

        return (string.Join('\n', before), string.Join('\n', after));
    }

    /// <summary>The rendered text of each run of a block, in order.</summary>
    /// <param name="block">The block.</param>
    public static IReadOnlyList<string> Lines(RenderBlock block) => block.Runs.Select(RunText).ToList();

    /// <summary>
    /// The rendered text of a block with the cells in <c>[from, to)</c> replaced by <paramref name="typed"/> (empty to remove them), where the
    /// selection crosses runs: the runs it covers are gone, and the text on either side of it joins into one line.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="from">The absolute offset where the selection starts.</param>
    /// <param name="to">The absolute offset where it ends.</param>
    /// <param name="typed">The text that takes the selection's place.</param>
    public static string Without(RenderBlock block, int from, int to, string typed)
    {
        var cells = block.Runs.Select((run, index) => (run, index)).ToList();
        int first = cells.First(r => r.run.Cells.Any(c => Mapped(c) && block.Start + c.Offset >= from && block.Start + c.Offset < to)).index;
        int last = cells.Last(r => r.run.Cells.Any(c => Mapped(c) && block.Start + c.Offset >= from && block.Start + c.Offset < to)).index;
        string left = string.Concat(block.Runs[first].Cells.Where(c => Mapped(c) && block.Start + c.Offset < from).Select(c => c.Text));
        string right = string.Concat(block.Runs[last].Cells.Where(c => Mapped(c) && block.Start + c.Offset >= to).Select(c => c.Text));
        var pieces = new List<string>();
        pieces.AddRange(Lines(block).Take(first));
        pieces.Add(left + typed + right);
        pieces.AddRange(Lines(block).Skip(last + 1));
        return string.Join('\n', pieces);
    }

    /// <summary>The source text of one run: its mapped cells except the task box.</summary>
    private static string RunText(Run run) => string.Concat(run.Cells.Where(cell => Mapped(cell)).Select(cell => cell.Text));

    /// <summary>Whether a cell is source text the rendered text shows: mapped, and not a task box.</summary>
    private static bool Mapped(Cell cell) => cell.IsMapped && cell.Style.Role != StyleRole.TaskBox;
}
