using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// Checks an edit by its rendering: the edited text is reparsed, and the block list must equal the one the edit expects, block by block, in
/// kind, heading level, containers (with the identity of their instances) and rendered text (see <see cref="BlockShape"/>). Container identity
/// is checked as grouping only, so a renumbered instance is the same instance.
/// </summary>
/// <remarks>
/// Tightness is not compared: the separators an edit writes keep a tight list tight and a loose list loose, which the exact source of the tests
/// shows, while a change of tightness is a layout property the verifier does not judge.
/// </remarks>
internal static class EditVerifier
{
    /// <summary>Whether the reparsed document has the expected block list.</summary>
    /// <param name="expected">The shapes the edit expects, in document order.</param>
    /// <param name="after">The document parsed from the edited text.</param>
    public static bool Holds(IEnumerable<BlockShape> expected, RenderDocument after) =>
        Same(BlockShape.Canonical(BlockShape.Of(after)), BlockShape.Canonical(expected.ToImmutableArray()));

    /// <summary>Whether two lists of shapes are the same, block by block.</summary>
    private static bool Same(ImmutableArray<BlockShape> actual, ImmutableArray<BlockShape> wanted) =>
        actual.Length == wanted.Length && actual.Zip(wanted).All(pair => pair.First.SameAs(pair.Second));

    /// <summary>
    /// Whether the edited document keeps the structure of <paramref name="before"/> and shows the intended text in the edited block. The
    /// edited block's old text, with <paramref name="removed"/> taken out at the place the edit ends (<paramref name="end"/>) and one of
    /// <paramref name="inserted"/> put in its place, is the text it must show.
    /// </summary>
    /// <param name="before">The document before the edit.</param>
    /// <param name="after">The document parsed from the text after the edit.</param>
    /// <param name="blockIndex">The index of the edited block; its index is the same in both documents when the edit kept the structure.</param>
    /// <param name="removed">The rendered text the edit removes from the edited block (empty for an insertion).</param>
    /// <param name="inserted">The rendered texts that may appear in its place: the typed text, or the text of a pending space that shows or does not.</param>
    /// <param name="end">The offset in the edited text where the edit's text ends: the end of the inserted source, or the removal's start.</param>
    public static bool Holds(RenderDocument before, RenderDocument after, int blockIndex, string removed, IEnumerable<string> inserted, int end)
    {
        ImmutableArray<BlockShape> old = BlockShape.Of(before);
        if (old.Length != after.Blocks.Length)
        {
            return false;
        }

        string oldText = old[blockIndex].Text;
        int placeEnd = BlockText.Split(after.Blocks[blockIndex], end).Before.Length;
        foreach (string text in inserted)
        {
            int place = placeEnd - text.Length;
            if (place < 0 || place > oldText.Length - removed.Length || oldText.Substring(place, removed.Length) != removed)
            {
                continue;
            }

            string expected = oldText[..place] + text + oldText[(place + removed.Length)..];
            var shapes = old.ToArray();
            shapes[blockIndex] = shapes[blockIndex] with { Text = expected };
            if (Holds(shapes, after))
            {
                return true;
            }
        }

        return false;
    }
}
