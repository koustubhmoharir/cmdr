using System.Text.RegularExpressions;

namespace Mdedit.Core.Render;

/// <summary>Classifies the destination of a link, as written in the source, into a <see cref="LinkTarget"/>. Pure.</summary>
public static partial class LinkTargets
{
    /// <summary>
    /// Classifies a URL: an <see cref="LinkTarget.Anchor"/> when it starts with <c>#</c>; an <see cref="LinkTarget.External"/> when
    /// it has a URI scheme of two characters or more (so that a Windows drive letter <c>C:</c> is not one) or starts with <c>//</c>;
    /// otherwise a <see cref="LinkTarget.File"/>. The path of a file is the part before the first <c>?</c> or <c>#</c>, percent-decoded,
    /// and the fragment is the part after the first <c>#</c>. An empty URL is an external target with an empty URL.
    /// </summary>
    /// <param name="url">The destination as Markdig gives it.</param>
    public static LinkTarget Classify(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (url.Length == 0)
        {
            return new LinkTarget.External(string.Empty);
        }

        if (url[0] == '#')
        {
            return new LinkTarget.Anchor(url[1..]);
        }

        if (SchemeRegex().IsMatch(url) || url.StartsWith("//", StringComparison.Ordinal))
        {
            return new LinkTarget.External(url);
        }

        int hash = url.IndexOf('#');
        string? fragment = hash < 0 ? null : url[(hash + 1)..];
        int pathEnd = url.IndexOfAny(['?', '#']);
        string path = Uri.UnescapeDataString(pathEnd < 0 ? url : url[..pathEnd]);
        return new LinkTarget.File(path, fragment);
    }

    /// <summary>A URI scheme: a letter, then two or more letters, digits, <c>+</c>, <c>.</c> or <c>-</c>, then a colon.</summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]+:")]
    private static partial Regex SchemeRegex();
}
