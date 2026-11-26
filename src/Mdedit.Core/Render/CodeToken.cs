namespace Mdedit.Core.Render;

/// <summary>
/// The syntax token a code cell belongs to, for colouring. Every code cell has one; <see cref="None"/> is the plain code
/// colour, which is what a cell has when no colorizer gives it a span.
/// </summary>
public enum CodeToken
{
    /// <summary>No token: the cell is drawn in the plain code colour.</summary>
    None,

    /// <summary>A keyword of the language.</summary>
    Keyword,

    /// <summary>A string literal, delimiters included.</summary>
    String,

    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>A comment.</summary>
    Comment,

    /// <summary>A type name.</summary>
    Type,

    /// <summary>The name of a function or method.</summary>
    Function,

    /// <summary>An operator.</summary>
    Operator,

    /// <summary>Punctuation: brackets, separators and delimiters.</summary>
    Punctuation,

    /// <summary>A preprocessor directive or a compiler directive.</summary>
    Preprocessor,

    /// <summary>A named constant or a literal such as <c>true</c> or <c>null</c>.</summary>
    Constant,

    /// <summary>An attribute or annotation.</summary>
    Attribute,

    /// <summary>A markup tag or element name.</summary>
    Tag,

    /// <summary>An identifier that no other token kind describes.</summary>
    Identifier,
}
