namespace Cfgedit.Tui;

/// <summary>
/// The styles a segment of a row can have. The tree's colours map them to Terminal.Gui attributes; this layer only names them.
/// </summary>
public enum StyleKind
{
    /// <summary>A JSON member's key.</summary>
    Key,

    /// <summary>A JSON item's index.</summary>
    Index,

    /// <summary>A JSON string.</summary>
    String,

    /// <summary>A JSON number.</summary>
    Number,

    /// <summary>A JSON <c>true</c> or <c>false</c>.</summary>
    Bool,

    /// <summary>A JSON <c>null</c>.</summary>
    Null,

    /// <summary>A container's member or item count, such as <c>(3)</c>.</summary>
    Summary,

    /// <summary>Punctuation, such as <c>&lt;</c>, <c>=</c>, quotes and the markers of comments. Dimmed.</summary>
    Punctuation,

    /// <summary>The content of a comment. Dimmed.</summary>
    Comment,

    /// <summary>An XML element name.</summary>
    ElementName,

    /// <summary>An XML attribute name.</summary>
    AttributeName,

    /// <summary>An XML attribute value.</summary>
    AttributeValue,

    /// <summary>XML character data, or the text of a text-only element.</summary>
    Text,

    /// <summary>The content of an XML CDATA section.</summary>
    CData,

    /// <summary>Source text that is shown but cannot be edited: a declaration or a doctype. Dimmed.</summary>
    ReadOnly,

    /// <summary>
    /// A visible control character (<c>&#x23CE;</c>, <c>&#x2192;</c>, <c>&#x240D;</c>, <c>&#x00B7;</c>), or the ellipsis of a cut
    /// line or a collapsed element.
    /// </summary>
    Glyph,

    /// <summary>The dimmed <c>raw</c> marker after a part that is edited as source text.</summary>
    RawMarker,

    /// <summary>The fold glyph (<c>&#x25BE;</c>, <c>&#x25B8;</c>) or the two spaces in its place.</summary>
    Fold,
}
