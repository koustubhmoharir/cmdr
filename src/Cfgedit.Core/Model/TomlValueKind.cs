namespace Cfgedit.Core.Model;

/// <summary>The kinds of TOML scalar. The date-time kinds are the four TOML forms.</summary>
public enum TomlValueKind
{
    /// <summary>A string, in any of its four forms.</summary>
    String,

    /// <summary>An integer.</summary>
    Integer,

    /// <summary>A float, including <c>inf</c> and <c>nan</c>.</summary>
    Float,

    /// <summary>A boolean.</summary>
    Boolean,

    /// <summary>A date and time with an offset, <c>1979-05-27T07:32:00Z</c>.</summary>
    OffsetDateTime,

    /// <summary>A date and time without an offset, <c>1979-05-27T07:32:00</c>.</summary>
    LocalDateTime,

    /// <summary>A date, <c>1979-05-27</c>.</summary>
    LocalDate,

    /// <summary>A time, <c>07:32:00</c>.</summary>
    LocalTime,
}

/// <summary>The TOML types a value can be given: the scalar kinds (a date-time is any of the four forms), a table and an array.</summary>
public enum TomlNewKind
{
    /// <summary>A string.</summary>
    String,

    /// <summary>An integer.</summary>
    Integer,

    /// <summary>A float.</summary>
    Float,

    /// <summary>A boolean.</summary>
    Boolean,

    /// <summary>A date and time, of whichever of the four forms the text has.</summary>
    DateTime,

    /// <summary>An empty inline table, <c>{}</c>.</summary>
    Table,

    /// <summary>An empty inline array, <c>[]</c>.</summary>
    Array,
}
