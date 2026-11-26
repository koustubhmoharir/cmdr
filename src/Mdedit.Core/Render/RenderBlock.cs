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
/// The language of a fenced code block: the first word of its info string, or null when there is none (and for an
/// indented code block).
/// </param>
/// <param name="Tight">Whether the block sits in a tight list item. The layout puts no blank line between blocks of a tight list.</param>
/// <param name="IsFirstInItem">Whether the block is the first leaf of its innermost list item, which gets the item's marker.</param>
public sealed record RenderBlock(
    BlockKind Kind,
    int Start,
    int End,
    ImmutableArray<Container> Containers,
    ImmutableArray<Run> Runs,
    int HeadingLevel = 0,
    string? Language = null,
    bool Tight = false,
    bool IsFirstInItem = false);
