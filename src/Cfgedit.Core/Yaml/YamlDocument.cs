using Shared.Core;

namespace Cfgedit.Core.Yaml;

/// <summary>
/// A parsed YAML stream: the concrete syntax tree of a source text, as an ordered list of items. An item is a document or a run
/// of comments and blank lines between documents. Every character of the source belongs to exactly one leaf of the tree, so the
/// leaves printed in order reproduce the text. The tree holds the syntax of the supported subset only; see
/// <see cref="Parse"/> for what is refused.
/// </summary>
public sealed class YamlDocument
{
    internal YamlDocument(SourceText source, IReadOnlyList<YamlNode> items)
    {
        Source = source;
        Items = items;
        Documents = items.OfType<YamlDocumentNode>().ToList();
    }

    /// <summary>The source text the tree was parsed from.</summary>
    public SourceText Source { get; }

    /// <summary>The items of the stream, in source order: documents, and the comments and blank lines between them.</summary>
    public IReadOnlyList<YamlNode> Items { get; }

    /// <summary>The documents, in source order. A stream with no content has none.</summary>
    public IReadOnlyList<YamlDocumentNode> Documents { get; }

    /// <summary>
    /// Parses a source text as a YAML 1.2 stream, within the subset cfgedit supports. Line breaks are LF, CRLF or a lone CR, and
    /// all three count as line breaks. The text must be UTF-8; a byte order mark is kept, as the source keeps it.
    /// </summary>
    /// <remarks>
    /// Refused, with a message that names the construct: complex keys (<c>?</c>), a collection, an alias or a block scalar as a
    /// key, a key that spans lines, an empty key, a single-pair mapping inside a flow sequence, a YAML version other than 1.2,
    /// and an unknown directive. A mapping with two keys that are the same string, or with more than one key that is not a
    /// string, is refused as a duplicate key (YAML requires keys to be unique). Nesting deeper than
    /// <see cref="Json.JsonDocument.MaxDepth"/> is refused. Duplicate anchors are allowed, and an alias refers to the latest
    /// anchor before it.
    /// </remarks>
    /// <exception cref="CfgeditException">
    /// The text is not UTF-8, it is not valid YAML, or it uses a construct outside the subset. Line and column say where.
    /// </exception>
    public static YamlDocument Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Encoding.CodePage != 65001)
        {
            throw new CfgeditException("a YAML file must be UTF-8");
        }

        return YamlParser.Parse(source);
    }
}
