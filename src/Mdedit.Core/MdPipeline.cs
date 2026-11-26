using Markdig;
using Markdig.Syntax;

namespace Mdedit.Core;

/// <summary>
/// The Markdig pipeline that mdedit parses Markdown with. It enables the extensions mdedit renders (pipe tables, task
/// lists, strikethrough, autolinks, alert blocks) and precise source locations, which the source map needs. Everything
/// else is plain CommonMark: no smart punctuation and no grid tables, so that the text on screen is the source text.
/// </summary>
public static class MdPipeline
{
    /// <summary>The pipeline, built once. Markdig pipelines are immutable after <c>Build</c> and safe to share.</summary>
    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UseAutoLinks()
        .UseAlertBlocks()
        .UsePreciseSourceLocation()
        .Build();

    /// <summary>Parses <paramref name="text"/> into a document with the <see cref="Pipeline"/>.</summary>
    /// <param name="text">The Markdown source.</param>
    public static MarkdownDocument Parse(string text) => Markdown.Parse(text, Pipeline);
}
