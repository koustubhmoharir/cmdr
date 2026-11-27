using System.Collections.Immutable;
using System.Text;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// The source a structure edit writes: the separators between blocks, the container prefixes of new lines, and the markers of new items. The
/// display text of a pending block is built from the same containers by the layout's prefix code.
/// </summary>
internal static class EditSource
{
    /// <summary>
    /// The prefix of a continuation line of a block in <paramref name="chain"/>: a quote bar, or the blanks of a list marker's width, per container.
    /// </summary>
    /// <param name="chain">The containers, outermost first.</param>
    public static string Continuation(IEnumerable<Container> chain)
    {
        var text = new StringBuilder();
        foreach (Container entry in chain)
        {
            text.Append(entry.Kind switch
            {
                ContainerKind.Quote or ContainerKind.Alert => "> ",
                ContainerKind.BulletItem => "  ",
                _ => new string(' ', (entry.Marker?.Length ?? 0) + 1),
            });
        }

        return text.ToString();
    }

    /// <summary>
    /// The prefix of a blank line between two blocks that share <paramref name="chain"/>: a quote bar for each quote, and no trailing blanks, so
    /// that a blank line inside a quote stays a quote line.
    /// </summary>
    /// <param name="chain">The containers the two blocks share, outermost first.</param>
    public static string Blank(IEnumerable<Container> chain)
    {
        var text = new StringBuilder();
        foreach (Container entry in chain)
        {
            text.Append(entry.Kind is ContainerKind.Quote or ContainerKind.Alert ? ">" : " ");
        }

        return text.ToString().TrimEnd(' ');
    }

    /// <summary>
    /// The text between two blocks: a line break, and for a loose list or any paragraph, a blank line carrying the shared quote bars and then
    /// another line break. Between items of a tight list it is a single line break.
    /// </summary>
    /// <param name="newline">The line ending.</param>
    /// <param name="shared">The containers the blocks share.</param>
    /// <param name="tightItems">Whether the blocks are items of a tight list, so that they take one line break.</param>
    public static string Separator(string newline, IEnumerable<Container> shared, bool tightItems) =>
        tightItems ? newline : newline + Blank(shared) + newline;

    /// <summary>The next number of an ordered marker, with its delimiter: <c>3.</c> becomes <c>4.</c>, <c>3)</c> becomes <c>4)</c>.</summary>
    /// <param name="marker">The marker as written.</param>
    public static string NextMarker(string marker) =>
        int.Parse(marker[..^1]) + 1 + marker[^1..];

    /// <summary>The display text of a pending line: the layout's prefix for the containers, the task box of an item shown as its source.</summary>
    /// <param name="chain">The containers of the pending block, outermost first; the innermost item carries its marker.</param>
    /// <param name="showMarker">Whether the innermost item shows its marker.</param>
    /// <param name="task">Whether the pending item is a task item, whose box is shown after the marker.</param>
    public static string Display(IEnumerable<Container> chain, bool showMarker, bool task)
    {
        var block = new RenderBlock(BlockKind.Paragraph, 0, 0, chain.ToImmutableArray(), ImmutableArray<Run>.Empty);
        string text = string.Concat(LinePrefix.Containers(block, showMarker).Select(cell => cell.Text));
        return task ? text + "[ ] " : text;
    }
}
