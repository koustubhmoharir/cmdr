using System.Collections.Immutable;
using System.Text;

namespace Mdedit.Core.Render;

/// <summary>
/// The GitHub-style anchors of a document's headings, which a <c>#fragment</c> link names. The anchors are computed from the
/// outline, so the heading text is the same as the outline's. Pure and immutable.
/// </summary>
public static class HeadingAnchors
{
    /// <summary>
    /// The anchor of every heading, in document order, with the heading's start. A heading's anchor is its text lower-cased, with
    /// every character that is not a letter, a digit, a space, <c>-</c> or <c>_</c> removed and each space replaced by <c>-</c>. A
    /// repeated anchor gets <c>-1</c>, <c>-2</c>, … in document order, so the second <c>Intro</c> is <c>intro-1</c>.
    /// </summary>
    /// <param name="doc">The document.</param>
    public static ImmutableArray<HeadingAnchor> Of(RenderDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var used = new HashSet<string>(StringComparer.Ordinal);
        var anchors = ImmutableArray.CreateBuilder<HeadingAnchor>();
        foreach (OutlineEntry entry in Outline.Of(doc))
        {
            string slug = Slug(entry.Text);
            string anchor = slug;
            for (int n = 1; !used.Add(anchor); n++)
            {
                anchor = $"{slug}-{n}";
            }

            anchors.Add(new HeadingAnchor(anchor, entry.Start));
        }

        return anchors.ToImmutable();
    }

    /// <summary>
    /// The start of the heading whose anchor is <paramref name="fragment"/>, or null when there is none. The fragment is percent-decoded
    /// and compared case-insensitively, so <c>#caf%C3%A9</c> finds the heading <c>Café</c>.
    /// </summary>
    /// <param name="anchors">The anchors from <see cref="Of"/>.</param>
    /// <param name="fragment">The fragment of a link, without the <c>#</c>, as written.</param>
    public static int? Find(ImmutableArray<HeadingAnchor> anchors, string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        string decoded = Uri.UnescapeDataString(fragment);
        foreach (HeadingAnchor anchor in anchors)
        {
            if (string.Equals(anchor.Anchor, decoded, StringComparison.OrdinalIgnoreCase))
            {
                return anchor.Start;
            }
        }

        return null;
    }

    /// <summary>The anchor of a heading's text before duplicates are numbered: lower-cased, punctuation removed, spaces as hyphens.</summary>
    private static string Slug(string text)
    {
        var slug = new StringBuilder(text.Length);
        foreach (Rune rune in text.ToLowerInvariant().EnumerateRunes())
        {
            if (rune.Value == ' ')
            {
                slug.Append('-');
            }
            else if (Rune.IsLetterOrDigit(rune) || rune.Value is '-' or '_')
            {
                slug.Append(rune.ToString());
            }
        }

        return slug.ToString();
    }
}

/// <summary>One heading's anchor.</summary>
/// <param name="Anchor">The anchor, as a <c>#fragment</c> names it: lower-cased, with a <c>-n</c> suffix for a repeat.</param>
/// <param name="Start">The absolute source offset of the heading's first character.</param>
public sealed record HeadingAnchor(string Anchor, int Start);
