namespace Cfgedit.Core.Yaml;

/// <summary>Which documents of a stream count, and how the comments of a node are found.</summary>
internal static class YamlDocuments
{
    /// <summary>
    /// The documents of a stream that count. A document with no <c>---</c> marker, no directive and no content is not one: it is only
    /// what a bare <c>...</c> leaves behind, after any comments, as the YAML test suite and YamlDotNet both read it. Its comments belong to
    /// the stream.
    /// </summary>
    internal static IReadOnlyList<YamlDocumentNode> Counted(YamlDocument syntax)
    {
        return syntax.Documents.Where(IsCounted).ToList();
    }

    /// <summary>True for a document that counts: see <see cref="Counted"/>.</summary>
    internal static bool IsCounted(YamlDocumentNode document)
    {
        return document.StartMarker is not null || document.Root is not null || document.Directives.Count > 0;
    }

    /// <summary>Every comment inside a node, including the node itself when it is a comment, in source order.</summary>
    internal static IEnumerable<YamlComment> CommentsIn(YamlNode node)
    {
        if (node is YamlComment comment)
        {
            yield return comment;
            yield break;
        }

        foreach (YamlNode part in node.Parts)
        {
            foreach (YamlComment inner in CommentsIn(part))
            {
                yield return inner;
            }
        }
    }
}
