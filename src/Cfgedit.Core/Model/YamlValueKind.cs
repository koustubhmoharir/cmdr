namespace Cfgedit.Core.Model;

/// <summary>
/// The kinds of YAML scalar. A plain scalar with no tag has the kind of the YAML 1.2 core schema; a quoted or block scalar with no tag is
/// a string; a tag that names a core type gives that type; any other tag, and the non-specific tag <c>!</c>, gives a string.
/// </summary>
public enum YamlValueKind
{
    /// <summary>null: <c>~</c>, <c>null</c>, <c>Null</c>, <c>NULL</c>, or an empty plain scalar.</summary>
    Null,

    /// <summary>A boolean: <c>true</c> or <c>false</c> in one of their spellings.</summary>
    Boolean,

    /// <summary>An integer, in decimal, <c>0o</c> octal or <c>0x</c> hexadecimal.</summary>
    Integer,

    /// <summary>A floating-point number, including <c>.inf</c> and <c>.nan</c>.</summary>
    Float,

    /// <summary>A string.</summary>
    String,
}
