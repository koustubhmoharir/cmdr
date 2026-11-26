namespace Mdedit.Core.Render;

/// <summary>
/// One inline container of a block (emphasis, strong, strikethrough, code span, link or image) with its source ranges and the
/// markup that delimits it as written. A copy of a part of the block re-adds the markup of the containers the part is cut from.
/// </summary>
/// <param name="Kind">What the container is.</param>
/// <param name="OuterStart">The offset of the whole construct in the source, relative to the start of the block.</param>
/// <param name="OuterEnd">The offset after the whole construct (exclusive), relative to the start of the block.</param>
/// <param name="InnerStart">The offset of the content, after the opener, relative to the start of the block.</param>
/// <param name="InnerEnd">The offset of the content's end, where the closer begins, relative to the start of the block.</param>
/// <param name="Opener">
/// The source text before the content: the delimiters (<c>**</c>, <c>`</c>), or <c>[</c> and <c>![</c> for a link and an image.
/// </param>
/// <param name="Closer">
/// The source text after the content: the delimiters, or for a link or an image the text from <c>]</c> to the end, such as
/// <c>](url "title")</c>, as written.
/// </param>
public sealed record InlineSpan(
    InlineSpanKind Kind,
    int OuterStart,
    int OuterEnd,
    int InnerStart,
    int InnerEnd,
    string Opener,
    string Closer);
