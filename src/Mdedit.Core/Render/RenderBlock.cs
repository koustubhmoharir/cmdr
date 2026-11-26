using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// A leaf block of the rendered view with its source span, the containers it sits in and its content. Container blocks
/// (quotes, lists) are not blocks of their own: each leaf records its chain in <see cref="Containers"/>.
/// </summary>
/// <param name="Kind">What the block is.</param>
/// <param name="Start">The source offset of the block's first character, in the document.</param>
/// <param name="End">The source offset after the block's last character (exclusive).</param>
/// <param name="Containers">The containers around the block, outermost first.</param>
/// <param name="Runs">The content, with offsets relative to <paramref name="Start"/>.</param>
/// <param name="HeadingLevel">The level of a heading, 1 to 6; 0 for other blocks.</param>
/// <param name="Language">
/// The language of a fenced code block: the first word of its info string as written, or null when there is none (and for an
/// indented code block).
/// </param>
/// <param name="LanguageId">
/// The canonical id of <paramref name="Language"/> from <see cref="CodeLanguages.Normalize"/>, which a colorizer is given; null
/// when <paramref name="Language"/> is null.
/// </param>
/// <param name="Tight">Whether the block sits in a tight list item. The layout puts no blank line between blocks of a tight list.</param>
/// <param name="IsFirstInItem">Whether the block is the first leaf of its innermost list item, which gets the item's marker.</param>
/// <param name="Table">
/// The structure of a table block: its column alignments and rows. Null for every other kind. The cells of its rows are also
/// <paramref name="Runs"/>, in row order, so that the source map and the lowering checks see them like any other content.
/// </param>
public sealed record RenderBlock(
    BlockKind Kind,
    int Start,
    int End,
    ImmutableArray<Container> Containers,
    ImmutableArray<Run> Runs,
    int HeadingLevel = 0,
    string? Language = null,
    string? LanguageId = null,
    bool Tight = false,
    bool IsFirstInItem = false,
    TableModel? Table = null)
{
    /// <summary>
    /// The inline containers of the block (emphasis, strong, strikethrough, code spans, links and images), outermost first in
    /// document order, with offsets relative to <see cref="Start"/>. For a table, the spans of all its cells, in row order. Empty for
    /// blocks without inline content. Autolinks and task boxes are not recorded.
    /// </summary>
    public ImmutableArray<InlineSpan> InlineSpans { get; init; } = ImmutableArray<InlineSpan>.Empty;
}
