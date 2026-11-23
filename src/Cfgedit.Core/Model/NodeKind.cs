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

    /// <summary>
    /// A .env group: the root of the file, or a virtual node for a key prefix (<c>Logging</c> in <c>Logging__Level</c>). It has no
    /// line of its own. Its span runs from its first line to its last line.
    /// </summary>
    EnvGroup,

    /// <summary>A .env entry: one key, with its value. Its span is its line, or its lines when the value is quoted over lines.</summary>
    EnvEntry,

    /// <summary>A .env comment line.</summary>
    EnvComment,

    /// <summary>
    /// A TOML table: the document's root, a member table (<c>[a]</c>, a dotted-key table or an implicit parent), an inline table, or
    /// an item of an array of tables. Its children are its members and comments.
    /// </summary>
    TomlTable,

    /// <summary>A TOML array: a static array (<c>[...]</c>) or an array of tables (<c>[[x]]</c>). Its children are its items and comments.</summary>
    TomlArray,

    /// <summary>A TOML scalar: a string, number, boolean or date-time, as a member or an array item. Its kind is <see cref="DocNode.TomlKind"/>.</summary>
    TomlScalar,

    /// <summary>A TOML comment.</summary>
    TomlComment,

    /// <summary>
    /// A YAML document, a row under the root only when the file has several documents. Its child is that document's root node. It has
    /// one part, its index.
    /// </summary>
    YamlDocument,

    /// <summary>A YAML block or flow mapping, as a member or as a root value. Its children are its members and comments.</summary>
    YamlMapping,

    /// <summary>A YAML block or flow sequence, as a member or as an item. Its children are its items and comments.</summary>
    YamlSequence,

    /// <summary>
    /// A YAML scalar, as a member, an item or a root value. Its kind is <see cref="DocNode.YamlKind"/>. A missing value (<c>key:</c>) is a
    /// null scalar with an empty value part.
    /// </summary>
    YamlScalar,

    /// <summary>A YAML alias, <c>*name</c>. It is shown as written, not expanded, and has no children.</summary>
    YamlAlias,

    /// <summary>A YAML comment.</summary>
    YamlComment,
}
