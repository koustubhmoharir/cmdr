using System.Collections.Immutable;
using Markdig.Syntax;

namespace Mdedit.Core.Render;

/// <summary>
/// The render model of a whole document: its source text and its leaf blocks in document order. Built once from the
/// Markdig AST by <see cref="Lower"/> (or <see cref="Parse"/>); immutable.
/// </summary>
/// <param name="Text">The source text the offsets refer to.</param>
/// <param name="Blocks">The leaf blocks, in document order.</param>
/// <param name="ReferenceKey">
/// The normalized labels of the document's link reference definitions, sorted and joined with line feeds (empty when there are
/// none). A block's lowering can depend on them, so a layout cache keys on it as well as on the block's text.
/// </param>
public sealed record RenderDocument(string Text, ImmutableArray<RenderBlock> Blocks, string ReferenceKey = "")
{
    /// <summary>Lowers a parsed document to its leaf blocks.</summary>
    /// <param name="text">The source text <paramref name="ast"/> was parsed from. Its offsets are indexes into this string.</param>
    /// <param name="ast">The document, from <c>MdPipeline.Parse(text)</c>.</param>
    public static RenderDocument Lower(string text, MarkdownDocument ast)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(ast);

        var lowerer = new Lowerer(text);
        ImmutableArray<RenderBlock> blocks = lowerer.Lower(ast);
        return new RenderDocument(text, blocks, lowerer.ReferenceKey);
    }

    /// <summary>Parses <paramref name="text"/> with the mdedit pipeline and lowers it.</summary>
    /// <param name="text">The Markdown source.</param>
    public static RenderDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Lower(text, MdPipeline.Parse(text));
    }
}
