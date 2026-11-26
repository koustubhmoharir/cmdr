namespace Mdedit.Core.Render;

/// <summary>
/// The look of one cell: its <see cref="Role"/>, its inline styles, the heading level of a heading (0 when the cell is not
/// in a heading) and its code token. A readonly record struct, so equal styles compare equal and cells stay cheap to copy.
/// </summary>
/// <param name="Role">What the cell is for.</param>
/// <param name="Inline">The inline styles the cell sits in.</param>
/// <param name="HeadingLevel">The level of the heading the cell is in, 1 to 6, or 0 when it is not in a heading.</param>
/// <param name="Token">The code token of the cell, <see cref="CodeToken.None"/> until code colouring.</param>
public readonly record struct CellStyle(
    StyleRole Role,
    InlineStyle Inline = InlineStyle.None,
    int HeadingLevel = 0,
    CodeToken Token = CodeToken.None);
