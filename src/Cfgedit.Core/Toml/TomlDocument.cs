using Shared.Core;

namespace Cfgedit.Core.Toml;

/// <summary>
/// A parsed TOML 1.0 document: the concrete syntax tree of a source text, as an ordered list of lines. Every character of the
/// source belongs to exactly one span of the tree, so printing the spans in order reproduces the text. The tree holds the
/// syntax only: duplicate keys, redefined tables and other semantic rules are not checked here.
/// </summary>
public sealed class TomlDocument
{
    internal TomlDocument(SourceText source, IReadOnlyList<TomlItem> items)
    {
        Source = source;
        Items = items;
    }

    /// <summary>The source text the tree was parsed from.</summary>
    public SourceText Source { get; }

    /// <summary>The lines of the document, in source order. Their spans tile the text.</summary>
    public IReadOnlyList<TomlItem> Items { get; }

    /// <summary>
    /// Parses a source text as TOML 1.0. The text must be UTF-8, with or without a byte order mark (which is kept, as the
    /// source keeps it). Line breaks are LF or CRLF; a lone CR is an error.
    /// </summary>
    /// <exception cref="CfgeditException">
    /// The text is not valid TOML 1.0 syntax, or it is not UTF-8. Line and column say where, when the failure has a place.
    /// </exception>
    public static TomlDocument Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Encoding.CodePage != 65001)
        {
            throw new CfgeditException("a TOML file must be UTF-8");
        }

        return TomlParser.Parse(source);
    }
}
