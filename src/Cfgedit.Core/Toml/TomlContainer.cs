namespace Cfgedit.Core.Toml;

/// <summary>
/// An array. Its span runs from the opening bracket through the closing one. <see cref="Entries"/> hold, in source order,
/// <see cref="TomlWhitespace"/> runs, <see cref="TomlComment"/>s and <see cref="TomlArrayItem"/>s, so that the interior of the
/// array is tiled by them.
/// </summary>
public sealed class TomlArray : TomlValue
{
    internal TomlArray(int start, int end, IReadOnlyList<TomlNode> entries)
        : base(start, end)
    {
        Entries = entries;
    }

    /// <summary>The entries of the array, in source order: whitespace, comments and items.</summary>
    public IReadOnlyList<TomlNode> Entries { get; }
}

/// <summary>
/// An inline table, written on one line. Its span runs from the opening brace through the closing one. <see cref="Entries"/>
/// hold, in source order, <see cref="TomlWhitespace"/> runs and <see cref="TomlInlineMember"/>s.
/// </summary>
public sealed class TomlInlineTable : TomlValue
{
    internal TomlInlineTable(int start, int end, IReadOnlyList<TomlNode> entries)
        : base(start, end)
    {
        Entries = entries;
    }

    /// <summary>The entries of the table, in source order: whitespace and members.</summary>
    public IReadOnlyList<TomlNode> Entries { get; }
}

/// <summary>
/// An item of an array: a value, the whitespace and comments between it and its comma, and the comma. Its span runs from the
/// value to the comma, or to the end of the value when there is no comma. The line breaks and comments that TOML allows between
/// a value and its comma are in <see cref="BeforeComma"/>; those that come after the comma are entries of the array.
/// </summary>
public sealed class TomlArrayItem : TomlNode
{
    internal TomlArrayItem(int start, int end, TomlValue value, IReadOnlyList<TomlNode> beforeComma, TomlSpan? comma)
        : base(start, end)
    {
        Value = value;
        BeforeComma = beforeComma;
        Comma = comma;
    }

    /// <summary>The value.</summary>
    public TomlValue Value { get; }

    /// <summary>
    /// The whitespace runs and comments between the value and its comma, in source order. Empty when there is no comma, and
    /// when the comma follows the value directly.
    /// </summary>
    public IReadOnlyList<TomlNode> BeforeComma { get; }

    /// <summary>The comma after the value, or null for the last item of an array without a trailing comma.</summary>
    public TomlSpan? Comma { get; }
}

/// <summary>
/// A member of an inline table: a key, the assignment, a value, the spaces before its comma and the comma. Its span runs from
/// the key to the comma, or to the end of the value when there is no comma.
/// </summary>
public sealed class TomlInlineMember : TomlNode
{
    internal TomlInlineMember(
        int start,
        int end,
        TomlKey key,
        TomlSpan assignment,
        TomlValue value,
        TomlSpan spaceBeforeComma,
        TomlSpan? comma)
        : base(start, end)
    {
        Key = key;
        Assignment = assignment;
        Value = value;
        SpaceBeforeComma = spaceBeforeComma;
        Comma = comma;
    }

    /// <summary>The key. A dotted key is allowed.</summary>
    public TomlKey Key { get; }

    /// <summary>The spaces, the equals sign and the spaces between the key and the value.</summary>
    public TomlSpan Assignment { get; }

    /// <summary>The value.</summary>
    public TomlValue Value { get; }

    /// <summary>The spaces and tabs between the value and its comma. Empty when there is no comma.</summary>
    public TomlSpan SpaceBeforeComma { get; }

    /// <summary>The comma after the member, or null for the last member, which has none.</summary>
    public TomlSpan? Comma { get; }
}
