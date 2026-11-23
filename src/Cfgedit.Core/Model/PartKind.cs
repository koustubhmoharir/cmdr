namespace Cfgedit.Core.Model;

/// <summary>The parts a node's row is made of.</summary>
public enum PartKind
{
    /// <summary>
    /// The JSON root value's marker, shown as <c>$</c>. It is the first part of the root row, has no width in the source, and is not
    /// editable. The keys a and i act on it as on the document, so that a comment can go before or after the root value.
    /// </summary>
    Root,

    /// <summary>A JSON member's key.</summary>
    Key,

    /// <summary>A JSON item's position in its array. Not editable.</summary>
    Index,

    /// <summary>A JSON scalar's value.</summary>
    Value,

    /// <summary>A JSON container's count of members or items, shown as <c>{3}</c> or <c>[5]</c>. Not editable.</summary>
    Summary,

    /// <summary>An XML element's start-tag name.</summary>
    ElementName,

    /// <summary>An XML element's end-tag name.</summary>
    EndName,

    /// <summary>An XML attribute's name.</summary>
    AttributeName,

    /// <summary>An XML attribute's value.</summary>
    AttributeValue,

    /// <summary>The text of an XML text-only element, or of a text or CDATA node.</summary>
    Text,

    /// <summary>The content of a JSON or XML comment.</summary>
    Comment,

    /// <summary>An XML processing instruction's target.</summary>
    PITarget,

    /// <summary>An XML processing instruction's data.</summary>
    PIData,

    /// <summary>A part that is shown but cannot be edited, such as a declaration or a container's summary.</summary>
    ReadOnly,

    /// <summary>A YAML anchor, shown as <c>&amp;name</c>. Not editable.</summary>
    Anchor,

    /// <summary>A YAML tag, shown as written, such as <c>!!str</c>. Not editable.</summary>
    Tag,

    /// <summary>A YAML alias, shown as <c>*name</c>. Not editable.</summary>
    Alias,
}
