namespace Logvu.Core.Detail;

/// <summary>
/// The style of a span of a detail line. The view maps each style to terminal attributes. Text outside every span is plain.
/// </summary>
public enum SpanStyle
{
    /// <summary>Plain text. Named so that a style can be spelled out; the document never emits a span of this style.</summary>
    None,

    /// <summary>The name of a text event's field, or the <c>Message</c> and <c>Error</c> labels of a summary.</summary>
    FieldName,

    /// <summary>Structure: the colon after a name, braces, brackets, parentheses and expansion markers.</summary>
    Punctuation,

    /// <summary>The exception type of a .NET exception header.</summary>
    ExceptionType,

    /// <summary>The message of a .NET exception header.</summary>
    ExceptionMessage,

    /// <summary>The method of a stack frame.</summary>
    Method,

    /// <summary>The source file name and line of a stack frame.</summary>
    FileLocation,

    /// <summary>The directory part of a stack frame's source path. Dimmed.</summary>
    Directory,

    /// <summary>A framework frame: the whole line. Dimmed.</summary>
    Framework,

    /// <summary>A JSON member name or array index.</summary>
    JsonKey,

    /// <summary>A JSON string value.</summary>
    JsonString,

    /// <summary>A JSON number.</summary>
    JsonNumber,

    /// <summary>A JSON <c>true</c>, <c>false</c> or <c>null</c>.</summary>
    JsonLiteral,

    /// <summary>Text matched by a highlight rule. The rule's style name is in <see cref="StyledSpan.HighlightStyle"/>.</summary>
    Highlight,

    /// <summary>A fold marker: <c>… N framework frames</c>.</summary>
    FoldMarker,

    /// <summary>Secondary text: placeholders, counts, and notices.</summary>
    Muted,
}

/// <summary>A styled range of a detail line's text.</summary>
/// <param name="Start">The first character of the range, in the line's text.</param>
/// <param name="Length">The number of characters in the range. Always positive.</param>
/// <param name="Style">The style of the range.</param>
/// <param name="HighlightStyle">
/// The configured style name when <paramref name="Style"/> is <see cref="SpanStyle.Highlight"/>; otherwise <c>null</c>.
/// </param>
public readonly record struct StyledSpan(int Start, int Length, SpanStyle Style, string? HighlightStyle = null);
