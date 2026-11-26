namespace Mdedit.Core.Render;

/// <summary>
/// The inline styles a cell sits in, as flags: a cell inside a strong link inside a code span carries all the flags of
/// its ancestors. Delimiters are never cells, so these flags are the only record of the markup that was removed.
/// </summary>
[Flags]
public enum InlineStyle
{
    /// <summary>No inline style: plain text.</summary>
    None = 0,

    /// <summary>Inside <c>*x*</c> or <c>_x_</c>.</summary>
    Emphasis = 1,

    /// <summary>Inside <c>**x**</c> or <c>__x__</c>.</summary>
    Strong = 2,

    /// <summary>Inside <c>~~x~~</c>.</summary>
    Strikethrough = 4,

    /// <summary>Inside a code span.</summary>
    Code = 8,

    /// <summary>Inside the text of a link (also a bare or angle-bracket autolink).</summary>
    Link = 16,

    /// <summary>Inside the alt text of an image.</summary>
    Image = 32,
}
