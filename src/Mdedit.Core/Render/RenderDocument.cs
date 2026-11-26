using System.Collections.Immutable;
using Markdig.Syntax;

namespace Mdedit.Core.Render;

/// <summary>
/// The render model of a whole document: its source text and its leaf blocks in document order. Built once from the
/// Markdig AST by <see cref="Lower"/> (or <see cref="Parse"/>); immutable.
/// </summary>
/// <param name="Text">The source text the offsets refer to.</param>
/// <param name="Blocks">The leaf blocks, in document order.</param>
public sealed record RenderDocument(string Text, ImmutableArray<RenderBlock> Blocks)
{
    /// <summary>Lowers a parsed document to its leaf blocks.</summary>
    /// <param name="text">The source text <paramref name="ast"/> was parsed from. Its offsets are indexes into this string.</param>
    /// <param name="ast">The document, from <c>MdPipeline.Parse(text)</c>.</param>
    public static RenderDocument Lower(string text, MarkdownDocument ast)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(ast);

        return new RenderDocument(text, new Lowerer(text).Lower(ast));
    }

    /// <summary>Parses <paramref name="text"/> with the mdedit pipeline and lowers it.</summary>
    /// <param name="text">The Markdown source.</param>
    public static RenderDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Lower(text, MdPipeline.Parse(text));
    }
}
