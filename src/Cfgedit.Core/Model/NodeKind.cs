namespace Cfgedit.Core.Model;

/// <summary>The kinds of node in a document tree.</summary>
public enum NodeKind
{
    /// <summary>The root of every document. It has no parts.</summary>
    Document,

    /// <summary>A JSON object, as a member or as a root value.</summary>
    JsonObject,

    /// <summary>A JSON array, as a member or as an item.</summary>
    JsonArray,

    /// <summary>A JSON scalar, as a member, an item or the root value.</summary>
    JsonScalar,

    /// <summary>A JSON comment.</summary>
    JsonComment,

    /// <summary>An XML element.</summary>
    XmlElement,

    /// <summary>XML character data, with its non-whitespace text.</summary>
    XmlText,

    /// <summary>An XML CDATA section.</summary>
    XmlCData,

    /// <summary>An XML comment.</summary>
    XmlComment,

    /// <summary>An XML processing instruction.</summary>
    XmlProcessingInstruction,

    /// <summary>The XML declaration. Shown read-only.</summary>
    XmlDeclaration,

    /// <summary>The XML document type declaration. Shown read-only.</summary>
    XmlDoctype,
}
