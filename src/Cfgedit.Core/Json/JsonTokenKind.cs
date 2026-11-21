namespace Cfgedit.Core.Json;

/// <summary>The kinds of token the JSON-family lexer produces.</summary>
public enum JsonTokenKind
{
    /// <summary>{</summary>
    LeftBrace,

    /// <summary>}</summary>
    RightBrace,

    /// <summary>[</summary>
    LeftBracket,

    /// <summary>]</summary>
    RightBracket,

    /// <summary>:</summary>
    Colon,

    /// <summary>,</summary>
    Comma,

    /// <summary>A string literal, quotes included. JSON5 allows single quotes.</summary>
    String,

    /// <summary>A number, including the JSON5 forms <c>Infinity</c> and <c>NaN</c>, with or without a sign.</summary>
    Number,

    /// <summary>The literal <c>true</c>.</summary>
    True,

    /// <summary>The literal <c>false</c>.</summary>
    False,

    /// <summary>The literal <c>null</c>.</summary>
    Null,

    /// <summary>A JSON5 identifier key, which may contain <c>\uXXXX</c> escapes. Reserved words are lexed as their own kinds.</summary>
    Identifier,

    /// <summary>A run of JSON whitespace (or JSON5 whitespace).</summary>
    Whitespace,

    /// <summary>A comment that starts with two slashes, up to but not including the line break.</summary>
    LineComment,

    /// <summary>A <c>/* */</c> comment, markers included.</summary>
    BlockComment,

    /// <summary>The end of the text. Its length is 0.</summary>
    EndOfFile,
}
