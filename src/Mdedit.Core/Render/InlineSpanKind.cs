namespace Mdedit.Core.Render;

/// <summary>The kinds of inline container that <see cref="InlineSpan"/> records: the markup a copy may need to re-add.</summary>
public enum InlineSpanKind
{
    /// <summary>Emphasis written with one <c>*</c> or <c>_</c>.</summary>
    Emphasis,

    /// <summary>Strong emphasis written with two <c>*</c> or <c>_</c>.</summary>
    Strong,

    /// <summary>Strikethrough written with <c>~~</c>.</summary>
    Strikethrough,

    /// <summary>A code span, delimited by one or more backticks.</summary>
    Code,

    /// <summary>A link, <c>[text](url)</c> or a reference link. Bare and angle-bracket autolinks are not recorded.</summary>
    Link,

    /// <summary>An image, <c>![alt](url)</c>.</summary>
    Image,
}
