namespace Cfgedit.Core.Env;

/// <summary>A span of the source text: from <see cref="Start"/> up to, not including, <see cref="End"/>.</summary>
/// <param name="Start">The offset of the first character.</param>
/// <param name="End">The offset just after the last character.</param>
public readonly record struct EnvSpan(int Start, int End)
{
    /// <summary>The number of UTF-16 units in the span.</summary>
    public int Length => End - Start;
}

/// <summary>
/// A comment: the <c>#</c> that starts it, and the text after it. The span runs from the <c>#</c> to the end of the line, not
/// including the line break.
/// </summary>
/// <param name="Start">The offset of the <c>#</c>.</param>
/// <param name="End">The offset just after the last character of the comment, before its line break.</param>
/// <param name="Content">The text after the <c>#</c>, without the line break. It keeps any leading or trailing spaces.</param>
public readonly record struct EnvComment(int Start, int End, string Content);

/// <summary>The quote style of a value.</summary>
public enum EnvQuote
{
    /// <summary>The value is not quoted. It has no escapes and ends at a trailing comment or the end of the line.</summary>
    None,

    /// <summary>The value is in single quotes. It is literal: there are no escapes, and it may span lines.</summary>
    Single,

    /// <summary>
    /// The value is in double quotes. The escapes <c>\n</c>, <c>\r</c>, <c>\t</c>, <c>\"</c> and <c>\\</c> are decoded, and it
    /// may span lines.
    /// </summary>
    Double,
}

/// <summary>
/// One item of a .env file, in source order. <see cref="Start"/> and <see cref="End"/> cover the whole item, including the line
/// break that ends its last line. A final line without a line break ends at the end of the text. The items' spans tile the
/// text: each character belongs to exactly one item.
/// </summary>
public abstract class EnvItem
{
    internal EnvItem(int start, int end)
    {
        Start = start;
        End = end;
    }

    /// <summary>The offset of the first character of the item, which is the start of its first line.</summary>
    public int Start { get; }

    /// <summary>The offset just after the item's last line break, or the end of the text.</summary>
    public int End { get; }
}

/// <summary>A line with nothing on it but spaces and tabs (possibly none).</summary>
public sealed class EnvBlankLine : EnvItem
{
    internal EnvBlankLine(int start, int end)
        : base(start, end)
    {
    }
}

/// <summary>A line whose first character, after optional spaces and tabs, is <c>#</c>.</summary>
public sealed class EnvCommentLine : EnvItem
{
    internal EnvCommentLine(int start, int end, EnvComment comment)
        : base(start, end)
    {
        Comment = comment;
    }

    /// <summary>The comment on the line.</summary>
    public EnvComment Comment { get; }
}

/// <summary>
/// An entry: a key, an equals sign and a value, with an optional <c>export</c> keyword before the key and an optional trailing
/// comment. A quoted value can make the entry span several lines. Every offset is into <see cref="EnvDocument.Source"/>.
/// </summary>
public sealed class EnvEntry : EnvItem
{
    internal EnvEntry(
        int start,
        int end,
        EnvSpan? export,
        string key,
        EnvSpan keySpan,
        int equalsStart,
        EnvSpan valueSpan,
        string value,
        EnvQuote quote,
        EnvComment? comment)
        : base(start, end)
    {
        Export = export;
        Key = key;
        KeySpan = keySpan;
        EqualsStart = equalsStart;
        ValueSpan = valueSpan;
        Value = value;
        Quote = quote;
        Comment = comment;
    }

    /// <summary>The span of the <c>export</c> keyword, or null when the line has none. The spaces after it are not included.</summary>
    public EnvSpan? Export { get; }

    /// <summary>The key, as written. Keys are compared as they are: dotenv is case-sensitive.</summary>
    public string Key { get; }

    /// <summary>The span of the key.</summary>
    public EnvSpan KeySpan { get; }

    /// <summary>The offset of the <c>=</c>.</summary>
    public int EqualsStart { get; }

    /// <summary>
    /// The span of the value token. A quoted value includes its quotes. An unquoted value excludes its trailing spaces and tabs
    /// and its trailing comment. An empty unquoted value has an empty span at the first character after the spaces that follow
    /// the <c>=</c>.
    /// </summary>
    public EnvSpan ValueSpan { get; }

    /// <summary>The decoded value. Quotes are removed, escapes are decoded and every line break inside quotes is <c>\n</c>.</summary>
    public string Value { get; }

    /// <summary>The quote style of the value.</summary>
    public EnvQuote Quote { get; }

    /// <summary>The trailing comment, or null when the line has none.</summary>
    public EnvComment? Comment { get; }
}
