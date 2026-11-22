namespace Cfgedit.Core.Toml;

/// <summary>
/// A value in a TOML document. <see cref="Start"/> and <see cref="End"/> are offsets into the source text, and
/// <see cref="End"/> is exclusive. Arrays and inline tables are in <see cref="TomlArray"/> and <see cref="TomlInlineTable"/>.
/// </summary>
public abstract class TomlValue : TomlNode
{
    internal TomlValue(int start, int end)
        : base(start, end)
    {
    }
}

/// <summary>The four forms a string can be written in.</summary>
public enum TomlStringForm
{
    /// <summary>A basic string, <c>"…"</c>, with escapes, on one line.</summary>
    Basic,

    /// <summary>A literal string, <c>'…'</c>, with no escapes, on one line.</summary>
    Literal,

    /// <summary>A multi-line basic string, <c>"""…"""</c>, with escapes.</summary>
    MultiLineBasic,

    /// <summary>A multi-line literal string, <c>'''…'''</c>, with no escapes.</summary>
    MultiLineLiteral,
}

/// <summary>A string. <see cref="Value"/> is the decoded text.</summary>
public sealed class TomlString : TomlValue
{
    internal TomlString(int start, int end, TomlStringForm form, string raw, string value)
        : base(start, end)
    {
        Form = form;
        Raw = raw;
        Value = value;
    }

    /// <summary>The form the string was written in.</summary>
    public TomlStringForm Form { get; }

    /// <summary>The exact source text, delimiters included.</summary>
    public string Raw { get; }

    /// <summary>
    /// The decoded text. Escapes are decoded. In a multi-line string a newline is read as LF whether the file has LF or CRLF,
    /// as TOML allows, and a newline right after the opening delimiter is not part of the value.
    /// </summary>
    public string Value { get; }
}

/// <summary>An integer. <see cref="Value"/> is the signed 64-bit value, and <see cref="Base"/> the radix it was written in.</summary>
public sealed class TomlInteger : TomlValue
{
    internal TomlInteger(int start, int end, string raw, long value, int radix)
        : base(start, end)
    {
        Raw = raw;
        Value = value;
        Base = radix;
    }

    /// <summary>The exact source text, sign, prefix and underscores included.</summary>
    public string Raw { get; }

    /// <summary>The value.</summary>
    public long Value { get; }

    /// <summary>The radix: 10, 16, 8 or 2.</summary>
    public int Base { get; }
}

/// <summary>A float, including <c>inf</c> and <c>nan</c>. <see cref="Value"/> is the IEEE 754 double.</summary>
public sealed class TomlFloat : TomlValue
{
    internal TomlFloat(int start, int end, string raw, double value)
        : base(start, end)
    {
        Raw = raw;
        Value = value;
    }

    /// <summary>The exact source text.</summary>
    public string Raw { get; }

    /// <summary>The value.</summary>
    public double Value { get; }
}

/// <summary>A boolean, <c>true</c> or <c>false</c>, lower case as TOML requires.</summary>
public sealed class TomlBoolean : TomlValue
{
    internal TomlBoolean(int start, int end, bool value)
        : base(start, end)
    {
        Value = value;
    }

    /// <summary>The value.</summary>
    public bool Value { get; }
}

/// <summary>The four kinds of date and time.</summary>
public enum TomlDateTimeKind
{
    /// <summary>A date and a time with an offset from UTC, <c>1979-05-27T07:32:00Z</c>.</summary>
    OffsetDateTime,

    /// <summary>A date and a time without an offset, <c>1979-05-27T07:32:00</c>.</summary>
    LocalDateTime,

    /// <summary>A date, <c>1979-05-27</c>.</summary>
    LocalDate,

    /// <summary>A time, <c>07:32:00</c>.</summary>
    LocalTime,
}

/// <summary>A date, a time, or a date and a time. <see cref="Raw"/> is the source text; it is not normalized.</summary>
public sealed class TomlDateTime : TomlValue
{
    internal TomlDateTime(int start, int end, string raw, TomlDateTimeKind kind)
        : base(start, end)
    {
        Raw = raw;
        Kind = kind;
    }

    /// <summary>The exact source text.</summary>
    public string Raw { get; }

    /// <summary>The kind of date and time.</summary>
    public TomlDateTimeKind Kind { get; }
}
