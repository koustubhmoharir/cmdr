namespace Cfgedit.Core.Model;

/// <summary>
/// An edit operation on a document. Operations are validated and planned by the format; <see cref="ConfigDocument.Apply"/>
/// carries them out. Paths name the nodes as they are in the document the operation is applied to.
/// </summary>
public abstract record EditOp;

/// <summary>
/// Sets one editable part of a node: a JSON key, value or comment, or an XML name, attribute value, text, comment or PI part.
/// <see cref="AttributeIndex"/> is used by the attribute parts only; pass -1 otherwise.
/// </summary>
public sealed record SetPart(NodePath Node, PartKind Part, int AttributeIndex, string Value) : EditOp;

/// <summary>
/// Inserts a node as a child of <see cref="Parent"/>. <see cref="Index"/> is the position among the parent's
/// <c>DocNode</c> children, comments included. The new node takes that position. An index equal to the child count appends.
/// Inserting into a JSON object needs <see cref="NewJsonValue.Key"/>; inserting into a JSON array needs it null.
/// </summary>
public sealed record InsertNode(NodePath Parent, int Index, NewNode Node) : EditOp;

/// <summary>Inserts an attribute into an element at a position among its attributes. The name must not already exist.</summary>
public sealed record InsertAttribute(NodePath Element, int Index, string Name, string Value) : EditOp;

/// <summary>
/// Deletes a node with its attached comments (the comments directly above it and those on its last line). A comment node is
/// deleted alone. The root value cannot be deleted.
/// </summary>
public sealed record DeleteNode(NodePath Node) : EditOp;

/// <summary>Deletes the attribute at a position among an element's attributes.</summary>
public sealed record DeleteAttribute(NodePath Element, int Index) : EditOp;

/// <summary>
/// Moves a node, with its attached comments, to a new position among its parent's children. <see cref="NewIndex"/> counts
/// positions in the children list after the moved node and its attached comments are removed: the node ends up at that
/// index. It is clamped to the list's range. A comment node moves alone. The root value cannot be moved.
/// </summary>
public sealed record MoveNode(NodePath Node, int NewIndex) : EditOp;

/// <summary>
/// Moves an attribute to a new position among its element's attributes. <see cref="NewIndex"/> counts positions in the list
/// after the attribute is removed, and is clamped to the list's range.
/// </summary>
public sealed record MoveAttribute(NodePath Element, int Index, int NewIndex) : EditOp;

/// <summary>
/// Changes the JSON type of a value. <see cref="Value"/> is the new scalar's value: the string for String (null means
/// empty), the number text for Number (required), ignored for True, False and Null, and ignored for Object and Array,
/// which become empty. The contents of a container that changes to anything else are dropped.
/// </summary>
public sealed record ChangeJsonType(NodePath Node, JsonValueKind Kind, string? Value) : EditOp;

/// <summary>
/// Changes the TOML type of a value: a scalar, an inline table or an inline array. <see cref="Value"/> is the new scalar's text:
/// required for DateTime, optional for the others (null means the kind's default, and for the same kind it means no change).
/// The contents of a container that changes to anything else are dropped. A table defined by headers or dotted keys, and an
/// array of tables, cannot change type.
/// </summary>
public sealed record ChangeTomlType(NodePath Node, TomlNewKind Kind, string? Value) : EditOp;

/// <summary>
/// Changes the YAML type of a value: a scalar, or a block or flow mapping or sequence. <see cref="Value"/> is the new scalar's text: the
/// string for String (null means empty), the number text for Number (null means <c>0</c>), <c>true</c> or <c>false</c> for Boolean (null
/// means <c>false</c>); it is ignored for Null, Mapping and Sequence. Null is written <c>null</c>, Mapping <c>{}</c> and Sequence <c>[]</c>.
/// A container's contents are dropped. An anchor stays; a tag stays only when it still fits the new type. An alias cannot change type.
/// </summary>
public sealed record ChangeYamlType(NodePath Node, YamlNewKind Kind, string? Value) : EditOp;

/// <summary>The YAML types that a value can change to.</summary>
public enum YamlNewKind
{
    /// <summary>A string.</summary>
    String,

    /// <summary>An integer or a float, by the text given.</summary>
    Number,

    /// <summary>A boolean.</summary>
    Boolean,

    /// <summary>null.</summary>
    Null,

    /// <summary>A mapping, written <c>{}</c>.</summary>
    Mapping,

    /// <summary>A sequence, written <c>[]</c>.</summary>
    Sequence,
}

/// <summary>
/// Inserts a copy of a node right after the node and its attached comments. A JSON member's copy is renamed
/// <c>&lt;key&gt;-copy</c>, or <c>&lt;key&gt;-copy2</c>, <c>-copy3</c> and so on, until its key is unique among its siblings.
/// </summary>
public sealed record DuplicateNode(NodePath Node) : EditOp;

/// <summary>
/// Several operations applied in order, each to the document the one before it made. The result is one edit, so
/// <see cref="EditHistory"/> records it as one undo step. Its focus is the last step's focus. A step that fails throws that
/// step's exception and the document is left as it was. Composites do not nest, and an empty composite changes nothing.
/// </summary>
public sealed record CompositeEdit(IReadOnlyList<EditOp> Steps) : EditOp;

/// <summary>The node inserted by an <see cref="InsertNode"/> operation.</summary>
public abstract record NewNode;

/// <summary>
/// A JSON member or item. <see cref="Key"/> is required in an object and must be null in an array. <see cref="Value"/> is
/// the string for String (null means empty), the number text for Number (required), and ignored for True, False, Null,
/// Object and Array.
/// </summary>
public sealed record NewJsonValue(string? Key, JsonValueKind Kind, string? Value) : NewNode;

/// <summary>
/// A JSON comment. <see cref="Text"/> is the content between the markers. A line comment must not contain a line break,
/// a block comment must not contain <c>*/</c>, and neither is allowed in strict JSON.
/// </summary>
public sealed record NewJsonComment(string Text, bool Block) : NewNode;

/// <summary>
/// A .env entry. <see cref="Key"/> is relative to the parent group and may contain <c>__</c>, which creates nested groups. The
/// value is written in the plainest form that reads back as the same value.
/// </summary>
public sealed record NewEnvEntry(string Key, string Value) : NewNode;

/// <summary>
/// A .env comment line. The line is <c>#</c> followed by <see cref="Text"/> exactly, so the text includes any leading space the
/// user wants. It must not contain a line break.
/// </summary>
public sealed record NewEnvComment(string Text) : NewNode;

/// <summary>
/// A TOML key/value, table, array or array item. <see cref="Key"/> is required in a table and in an inline table, and must be null
/// in an array. An array of tables takes only <see cref="TomlNewKind.Table"/>. <see cref="Value"/> is the text of a scalar: the string
/// for String (null means empty), the number or date-time text for the others (null means the kind's default, and a date-time
/// needs it), and "true" or "false" for Boolean. It is ignored for Table and Array.
/// </summary>
public sealed record NewTomlValue(string? Key, TomlNewKind Kind, string? Value) : NewNode;

/// <summary>
/// A TOML comment line. <see cref="Text"/> is the text after the <c>#</c>, so it includes any leading space the user wants. It must
/// not contain a line break.
/// </summary>
public sealed record NewTomlComment(string Text) : NewNode;

/// <summary>
/// A YAML mapping entry or sequence item. <see cref="Key"/> is required in a mapping and must be null in a sequence. <see cref="Value"/> is
/// the text of a String (null means empty), of a Number (null means 0) or of a Boolean (null means false). It is ignored for Null, Mapping
/// and Sequence.
/// </summary>
public sealed record NewYamlValue(string? Key, YamlNewKind Kind, string? Value) : NewNode;

/// <summary>
/// A YAML comment line. <see cref="Text"/> is the text after the <c>#</c>, so it includes any leading space the user wants. It must not
/// contain a line break.
/// </summary>
public sealed record NewYamlComment(string Text) : NewNode;

/// <summary>An XML element. <see cref="Text"/> is its text content when it is not null and not empty.</summary>
public sealed record NewXmlElement(string Name, IReadOnlyList<(string Name, string Value)> Attributes, string? Text) : NewNode;

/// <summary>An XML character data or CDATA node.</summary>
public sealed record NewXmlText(string Text, bool CData) : NewNode;

/// <summary>An XML comment.</summary>
public sealed record NewXmlComment(string Text) : NewNode;
