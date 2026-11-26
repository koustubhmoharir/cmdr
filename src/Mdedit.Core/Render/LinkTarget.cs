namespace Mdedit.Core.Render;

/// <summary>
/// Where a link goes, as <see cref="LinkTargets.Classify"/> reads its URL. A closed set: an in-document anchor, an external
/// URL (a scheme or a network path) or a file path with an optional fragment.
/// </summary>
public abstract record LinkTarget
{
    // Closed hierarchy: only the nested records below derive from it.
    private LinkTarget()
    {
    }

    /// <summary>A link to a heading of the same document: <c>#fragment</c>.</summary>
    /// <param name="Fragment">The text after the <c>#</c>, as written (not percent-decoded; <see cref="HeadingAnchors.Find"/> decodes it).</param>
    public sealed record Anchor(string Fragment) : LinkTarget;

    /// <summary>A URL with a scheme (<c>https:</c>, <c>mailto:</c>, <c>file:</c>, …) or a network path (<c>//host/x</c>), as written.</summary>
    /// <param name="Url">The URL as written. An empty link is an external target with an empty URL.</param>
    public sealed record External(string Url) : LinkTarget;

    /// <summary>A relative or absolute file path, with an optional fragment.</summary>
    /// <param name="Path">The path before any <c>?</c> or <c>#</c>, percent-decoded, with the separators as written.</param>
    /// <param name="Fragment">The text after the first <c>#</c>, as written, or null when there is none.</param>
    public sealed record File(string Path, string? Fragment) : LinkTarget;
}
