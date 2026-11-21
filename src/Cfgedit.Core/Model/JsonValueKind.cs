namespace Cfgedit.Core.Model;

/// <summary>The kinds of JSON value. This is Cfgedit's own enum, not System.Text.Json's.</summary>
public enum JsonValueKind
{
    /// <summary>A string.</summary>
    String,

    /// <summary>A number, in the spelling it was written in.</summary>
    Number,

    /// <summary><c>true</c>.</summary>
    True,

    /// <summary><c>false</c>.</summary>
    False,

    /// <summary><c>null</c>.</summary>
    Null,

    /// <summary>An object.</summary>
    Object,

    /// <summary>An array.</summary>
    Array,
}
