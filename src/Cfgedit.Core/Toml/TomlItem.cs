namespace Cfgedit.Core.Toml;

/// <summary>
/// One line of a TOML document, in source order. <see cref="Start"/> is the start of the line and <see cref="End"/> is just
/// after its line break (or the end of the text for a last line without one). The parts of a line are, in order:
/// <see cref="Indent"/>, the content (a key and value, or a header, or a comment), <see cref="TrailingSpace"/>,
/// <see cref="Comment"/> and <see cref="LineBreak"/>. Those spans tile the line.
/// </summary>
public abstract class TomlItem : TomlNode
{
    internal TomlItem(int start, int end, TomlSpan indent, TomlSpan trailingSpace, TomlComment? comment, TomlSpan lineBreak)
        : base(start, end)
    {
        Indent = indent;
        TrailingSpace = trailingSpace;
        Comment = comment;
        LineBreak = lineBreak;
    }

    /// <summary>The spaces and tabs at the start of the line. It may be empty.</summary>
    public TomlSpan Indent { get; }

    /// <summary>
    /// The spaces and tabs after the content of the line and before its comment, or before its line break when there is no
    /// comment. It is empty on a blank line or a comment line.
    /// </summary>
    public TomlSpan TrailingSpace { get; }

    /// <summary>The comment at the end of the line, or null when there is none.</summary>
    public TomlComment? Comment { get; }

    /// <summary>The line break that ends the line, LF or CRLF. Empty for a last line without one.</summary>
    public TomlSpan LineBreak { get; }
}

/// <summary>A line with nothing on it but spaces and tabs (possibly none).</summary>
public sealed class TomlBlankItem : TomlItem
{
    internal TomlBlankItem(int start, int end, TomlSpan indent, TomlSpan lineBreak)
        : base(start, end, indent, new TomlSpan(indent.End, indent.End), null, lineBreak)
    {
    }
}

/// <summary>A line that holds only a comment, after its optional indent.</summary>
public sealed class TomlCommentItem : TomlItem
{
    internal TomlCommentItem(int start, int end, TomlSpan indent, TomlComment comment, TomlSpan lineBreak)
        : base(start, end, indent, new TomlSpan(comment.Start, comment.Start), comment, lineBreak)
    {
    }
}

/// <summary>A key/value pair. Its key, the assignment and its value are on the same line, except that a value may span lines.</summary>
public sealed class TomlKeyValueItem : TomlItem
{
    internal TomlKeyValueItem(
        int start,
        int end,
        TomlSpan indent,
        TomlKey key,
        TomlSpan assignment,
        TomlValue value,
        TomlSpan trailingSpace,
        TomlComment? comment,
        TomlSpan lineBreak)
        : base(start, end, indent, trailingSpace, comment, lineBreak)
    {
        Key = key;
        Assignment = assignment;
        Value = value;
    }

    /// <summary>The key.</summary>
    public TomlKey Key { get; }

    /// <summary>
    /// The spaces, the equals sign and the spaces after the key, from the end of the key to the start of the value.
    /// </summary>
    public TomlSpan Assignment { get; }

    /// <summary>The value.</summary>
    public TomlValue Value { get; }
}

/// <summary>
/// A table header: <c>[key]</c> or <c>[[key]]</c>. <see cref="OpenSpan"/> is the opening bracket or pair of brackets, and
/// <see cref="CloseSpan"/> the closing one. The spaces inside the brackets are spans of their own.
/// </summary>
public abstract class TomlHeaderItem : TomlItem
{
    internal TomlHeaderItem(
        int start,
        int end,
        TomlSpan indent,
        TomlSpan openSpan,
        TomlSpan spaceAfterOpen,
        TomlKey key,
        TomlSpan spaceBeforeClose,
        TomlSpan closeSpan,
        TomlSpan trailingSpace,
        TomlComment? comment,
        TomlSpan lineBreak)
        : base(start, end, indent, trailingSpace, comment, lineBreak)
    {
        OpenSpan = openSpan;
        SpaceAfterOpen = spaceAfterOpen;
        Key = key;
        SpaceBeforeClose = spaceBeforeClose;
        CloseSpan = closeSpan;
    }

    /// <summary>The opening bracket, <c>[</c>, or the two of an array table, <c>[[</c>.</summary>
    public TomlSpan OpenSpan { get; }

    /// <summary>The spaces and tabs between the opening brackets and the key. Possibly empty.</summary>
    public TomlSpan SpaceAfterOpen { get; }

    /// <summary>The key of the table.</summary>
    public TomlKey Key { get; }

    /// <summary>The spaces and tabs between the key and the closing brackets. Possibly empty.</summary>
    public TomlSpan SpaceBeforeClose { get; }

    /// <summary>The closing bracket, <c>]</c>, or the two of an array table, <c>]]</c>.</summary>
    public TomlSpan CloseSpan { get; }
}

/// <summary>A standard table header, <c>[key]</c>.</summary>
public sealed class TomlTableItem : TomlHeaderItem
{
    internal TomlTableItem(
        int start,
        int end,
        TomlSpan indent,
        TomlSpan openSpan,
        TomlSpan spaceAfterOpen,
        TomlKey key,
        TomlSpan spaceBeforeClose,
        TomlSpan closeSpan,
        TomlSpan trailingSpace,
        TomlComment? comment,
        TomlSpan lineBreak)
        : base(start, end, indent, openSpan, spaceAfterOpen, key, spaceBeforeClose, closeSpan, trailingSpace, comment, lineBreak)
    {
    }
}

/// <summary>An array-of-tables header, <c>[[key]]</c>.</summary>
public sealed class TomlArrayTableItem : TomlHeaderItem
{
    internal TomlArrayTableItem(
        int start,
        int end,
        TomlSpan indent,
        TomlSpan openSpan,
        TomlSpan spaceAfterOpen,
        TomlKey key,
        TomlSpan spaceBeforeClose,
        TomlSpan closeSpan,
        TomlSpan trailingSpace,
        TomlComment? comment,
        TomlSpan lineBreak)
        : base(start, end, indent, openSpan, spaceAfterOpen, key, spaceBeforeClose, closeSpan, trailingSpace, comment, lineBreak)
    {
    }
}
