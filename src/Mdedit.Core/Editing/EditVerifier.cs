using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// Checks an edit by its rendering, as the table verifier does: the edited text is reparsed, and the document must have the same blocks, of the
/// same kinds in the same containers, whose plain text (<see cref="TextExtraction.PlainText"/>) is unchanged, except the edited block. The plain
/// text of the edited block must be the old one with the removed text gone and the inserted text in its place, at the place the edit was made.
/// </summary>
internal static class EditVerifier
{
    /// <summary>
    /// Whether the edited document keeps the structure and shows the intended text.
    /// </summary>
    /// <param name="before">The document before the edit.</param>
    /// <param name="after">The document parsed from the text after the edit.</param>
    /// <param name="blockIndex">The index of the edited block; its index is the same in both documents when the edit kept the structure.</param>
    /// <param name="removed">The plain text the edit removes from the edited block (empty for an insertion).</param>
    /// <param name="inserted">The plain texts that may appear in its place: the typed text, or the text of a pending space that shows or does not.</param>
    /// <param name="end">The offset in the edited text where the edit's text ends: the end of the inserted source, or the removal's start.</param>
    public static bool Holds(RenderDocument before, RenderDocument after, int blockIndex, string removed, IEnumerable<string> inserted, int end)
    {
        if (before.Blocks.Length != after.Blocks.Length)
        {
            return false;
        }

        bool textOk = false;
        for (int index = 0; index < before.Blocks.Length; index++)
        {
            RenderBlock old = before.Blocks[index];
            RenderBlock now = after.Blocks[index];
            if (old.Kind != now.Kind || !old.Containers.SequenceEqual(now.Containers))
            {
                return false;
            }

            string oldPlain = PlainOf(before, old);
            string newPlain = PlainOf(after, now);
            if (index != blockIndex)
            {
                if (oldPlain != newPlain)
                {
                    return false;
                }
            }
            else
            {
                // The plain text up to the edit's end holds the inserted text, so its length gives the place of the edit in the plain text.
                int placeEnd = TextExtraction.PlainText(after, now.Start, end).Length;
                textOk = inserted.Any(text => Matches(oldPlain, newPlain, placeEnd, removed, text));
            }
        }

        return textOk;
    }

    /// <summary>The plain text of a whole block.</summary>
    private static string PlainOf(RenderDocument doc, RenderBlock block) => TextExtraction.PlainText(doc, block.Start, block.End);

    /// <summary>
    /// Whether <paramref name="newPlain"/> is <paramref name="oldPlain"/> with <paramref name="removed"/> taken out and <paramref name="inserted"/>
    /// put in its place, the place being the one that ends at <paramref name="placeEnd"/> in the new text.
    /// </summary>
    private static bool Matches(string oldPlain, string newPlain, int placeEnd, string removed, string inserted)
    {
        if (newPlain.Length != oldPlain.Length - removed.Length + inserted.Length)
        {
            return false;
        }

        int place = placeEnd - inserted.Length;
        if (place < 0 || place > oldPlain.Length - removed.Length)
        {
            return false;
        }

        return newPlain.AsSpan(place, inserted.Length).SequenceEqual(inserted)
            && oldPlain.AsSpan(place, removed.Length).SequenceEqual(removed)
            && newPlain.AsSpan(0, place).SequenceEqual(oldPlain.AsSpan(0, place))
            && newPlain.AsSpan(place + inserted.Length).SequenceEqual(oldPlain.AsSpan(place + removed.Length));
    }
}
