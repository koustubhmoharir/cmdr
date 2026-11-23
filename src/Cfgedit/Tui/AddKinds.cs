using Cfgedit.Core.Json;
using Cfgedit.Core.Model;

namespace Cfgedit.Tui;

/// <summary>What the Add dialog can add: a JSON value (as a member or an item), a JSON comment, an XML node, or a .env entry or comment.</summary>
internal enum AddKind
{
    /// <summary>A JSON string, with an empty value.</summary>
    String,

    /// <summary>A JSON number, with the value 0.</summary>
    Number,

    /// <summary>A JSON true.</summary>
    True,

    /// <summary>A JSON false.</summary>
    False,

    /// <summary>A JSON null.</summary>
    Null,

    /// <summary>An empty JSON object.</summary>
    Object,

    /// <summary>An empty JSON array.</summary>
    Array,

    /// <summary>A JSON line comment, with the text the dialog gives.</summary>
    LineComment,

    /// <summary>A JSON block comment, with the text the dialog gives.</summary>
    BlockComment,

    /// <summary>An XML element with the name the dialog gives and no content.</summary>
    XmlElement,

    /// <summary>An XML text node, empty, so that its text is written in the tree.</summary>
    XmlText,

    /// <summary>An XML comment with the text the dialog gives.</summary>
    XmlComment,

    /// <summary>A .env entry with the key the dialog gives (relative to the group) and an empty value.</summary>
    EnvEntry,

    /// <summary>A .env comment line with the text the dialog gives.</summary>
    EnvComment,

    /// <summary>A TOML string, with an empty value.</summary>
    TomlString,

    /// <summary>A TOML integer, with the value 0.</summary>
    TomlInteger,

    /// <summary>A TOML float, with the value 0.0.</summary>
    TomlFloat,

    /// <summary>A TOML boolean, with the value false.</summary>
    TomlBoolean,

    /// <summary>A TOML date-time, with the text the dialog gives.</summary>
    TomlDateTime,

    /// <summary>An empty TOML table, <c>{}</c> inline or a table in a header, with the key the dialog gives.</summary>
    TomlTable,

    /// <summary>An empty TOML array, <c>[]</c>, with the key the dialog gives.</summary>
    TomlArray,

    /// <summary>A TOML comment line with the text the dialog gives.</summary>
    TomlComment,

    /// <summary>A YAML string, with an empty value.</summary>
    YamlString,

    /// <summary>A YAML number, with the value 0.</summary>
    YamlNumber,

    /// <summary>A YAML boolean, with the value false.</summary>
    YamlBoolean,

    /// <summary>A YAML null, written <c>null</c>.</summary>
    YamlNull,

    /// <summary>An empty YAML mapping, written <c>{}</c>, with the key the dialog gives.</summary>
    YamlMapping,

    /// <summary>An empty YAML sequence, written <c>[]</c>, with the key the dialog gives.</summary>
    YamlSequence,

    /// <summary>A YAML comment line with the text the dialog gives.</summary>
    YamlComment,
}

/// <summary>What the Add dialog asked for: the kind, and the key or name and the text when the kind takes them (the others are ignored).</summary>
/// <param name="Kind">The kind chosen.</param>
/// <param name="Name">The key of a JSON member, or the name of an XML element.</param>
/// <param name="Text">The text of a comment.</param>
internal sealed record AddRequest(AddKind Kind, string Name, string Text);

/// <summary>
/// The kinds an add may take in a parent, and the edit each one makes. Pure: no Terminal.Gui types, so the rules can be tested without a
/// terminal.
/// </summary>
internal static class AddKinds
{
    /// <summary>The kinds a parent takes, in the order the dialog lists them. Empty when nothing can be added there.</summary>
    /// <param name="document">The document the parent belongs to.</param>
    /// <param name="parent">The node the new node goes into.</param>
    public static IReadOnlyList<AddKind> For(ConfigDocument document, DocNode parent)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(parent);
        switch (parent.Kind)
        {
            case NodeKind.JsonObject or NodeKind.JsonArray:
            {
                var kinds = new List<AddKind> { AddKind.String, AddKind.Number, AddKind.True, AddKind.False, AddKind.Null, AddKind.Object, AddKind.Array };
                if (CommentsAllowed(document))
                {
                    kinds.Add(AddKind.LineComment);
                    kinds.Add(AddKind.BlockComment);
                }

                return kinds;
            }
            case NodeKind.XmlElement:
                return [AddKind.XmlElement, AddKind.XmlText, AddKind.XmlComment];
            case NodeKind.EnvGroup:
                return [AddKind.EnvEntry, AddKind.EnvComment];
            case NodeKind.TomlArray when TomlFormat.IsArrayOfTables(document, parent):
                // An array of tables ([[x]]) holds tables only.
                return [AddKind.TomlTable];
            case NodeKind.TomlTable or NodeKind.TomlArray:
                return [AddKind.TomlString, AddKind.TomlInteger, AddKind.TomlFloat, AddKind.TomlBoolean, AddKind.TomlDateTime, AddKind.TomlTable, AddKind.TomlArray, AddKind.TomlComment];
            case NodeKind.YamlMapping or NodeKind.YamlSequence:
                // A YAML document row takes nothing (the Core refuses adding documents), so it falls to the default.
                return [AddKind.YamlString, AddKind.YamlNumber, AddKind.YamlBoolean, AddKind.YamlNull, AddKind.YamlMapping, AddKind.YamlSequence, AddKind.YamlComment];
            case NodeKind.Document when document.Format is XmlFormat:
                return [AddKind.XmlComment];
            case NodeKind.Document when CommentsAllowed(document):
                return [AddKind.LineComment, AddKind.BlockComment];
            default:
                return [];
        }
    }

    /// <summary>The name of a kind as the dialog shows it.</summary>
    public static string Label(AddKind kind) => kind switch
    {
        AddKind.String => "string",
        AddKind.Number => "number",
        AddKind.True => "true",
        AddKind.False => "false",
        AddKind.Null => "null",
        AddKind.Object => "object",
        AddKind.Array => "array",
        AddKind.LineComment => "line comment",
        AddKind.BlockComment => "block comment",
        AddKind.XmlElement => "element",
        AddKind.XmlText => "text",
        AddKind.XmlComment => "comment",
        AddKind.EnvEntry => "entry",
        AddKind.EnvComment => "comment",
        AddKind.TomlString => "string",
        AddKind.TomlInteger => "integer",
        AddKind.TomlFloat => "float",
        AddKind.TomlBoolean => "boolean",
        AddKind.TomlDateTime => "date-time",
        AddKind.TomlTable => "table",
        AddKind.TomlArray => "array",
        AddKind.TomlComment => "comment",
        AddKind.YamlString => "string",
        AddKind.YamlNumber => "number",
        AddKind.YamlBoolean => "boolean",
        AddKind.YamlNull => "null",
        AddKind.YamlMapping => "mapping",
        AddKind.YamlSequence => "sequence",
        AddKind.YamlComment => "comment",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
    };

    /// <summary>The label of the text field for a kind that takes text: "Value" for a TOML date-time, else "Text".</summary>
    public static string TextLabel(AddKind kind) => kind == AddKind.TomlDateTime ? "Value" : "Text";

    /// <summary>
    /// The label of the name field for a kind in a parent: "Key" for a JSON member or a .env entry, "Name" for an XML element, and null when
    /// the kind takes no name.
    /// </summary>
    public static string? NameLabel(DocNode parent, AddKind kind)
    {
        if (kind == AddKind.XmlElement)
        {
            return "Name";
        }

        if (kind == AddKind.EnvEntry)
        {
            return "Key";
        }

        if (parent.Kind == NodeKind.TomlTable && IsTomlValue(kind))
        {
            return "Key";
        }

        if (parent.Kind == NodeKind.YamlMapping && IsYamlValue(kind))
        {
            return "Key";
        }

        return parent.Kind == NodeKind.JsonObject && IsJsonValue(kind) ? "Key" : null;
    }

    /// <summary>True when the kind takes text: a comment, or a TOML date-time, whose text is its value.</summary>
    public static bool TakesText(AddKind kind) =>
        kind is AddKind.LineComment or AddKind.BlockComment or AddKind.XmlComment or AddKind.EnvComment or AddKind.TomlComment or AddKind.TomlDateTime
            or AddKind.YamlComment;

    /// <summary>
    /// The insert that adds the node at <paramref name="index"/> among the parent's children. A JSON member's key is the request's name
    /// (an object takes a key, an array none). A comment takes the request's text.
    /// </summary>
    public static EditOp Build(DocNode parent, int index, AddRequest request)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(request);
        switch (request.Kind)
        {
            case AddKind.String:
                return Member(parent, index, request, JsonValueKind.String, string.Empty);
            case AddKind.Number:
                return Member(parent, index, request, JsonValueKind.Number, "0");
            case AddKind.True:
                return Member(parent, index, request, JsonValueKind.True, null);
            case AddKind.False:
                return Member(parent, index, request, JsonValueKind.False, null);
            case AddKind.Null:
                return Member(parent, index, request, JsonValueKind.Null, null);
            case AddKind.Object:
                return Member(parent, index, request, JsonValueKind.Object, null);
            case AddKind.Array:
                return Member(parent, index, request, JsonValueKind.Array, null);
            case AddKind.LineComment:
                return new InsertNode(parent.Path, index, new NewJsonComment(request.Text, Block: false));
            case AddKind.BlockComment:
                return new InsertNode(parent.Path, index, new NewJsonComment(request.Text, Block: true));
            case AddKind.XmlElement:
                return new InsertNode(parent.Path, index, new NewXmlElement(request.Name, [], null));
            case AddKind.XmlText:
                return new InsertNode(parent.Path, index, new NewXmlText(string.Empty, CData: false));
            case AddKind.XmlComment:
                return new InsertNode(parent.Path, index, new NewXmlComment(request.Text));
            case AddKind.EnvEntry:
                return new InsertNode(parent.Path, index, new NewEnvEntry(request.Name, string.Empty));
            case AddKind.EnvComment:
                return new InsertNode(parent.Path, index, new NewEnvComment(request.Text));
            case AddKind.TomlString:
                return TomlMember(parent, index, request, TomlNewKind.String, string.Empty);
            case AddKind.TomlInteger:
                return TomlMember(parent, index, request, TomlNewKind.Integer, "0");
            case AddKind.TomlFloat:
                return TomlMember(parent, index, request, TomlNewKind.Float, "0.0");
            case AddKind.TomlBoolean:
                return TomlMember(parent, index, request, TomlNewKind.Boolean, "false");
            case AddKind.TomlDateTime:
                return TomlMember(parent, index, request, TomlNewKind.DateTime, request.Text);
            case AddKind.TomlTable:
                return TomlMember(parent, index, request, TomlNewKind.Table, null);
            case AddKind.TomlArray:
                return TomlMember(parent, index, request, TomlNewKind.Array, null);
            case AddKind.TomlComment:
                return new InsertNode(parent.Path, index, new NewTomlComment(request.Text));
            case AddKind.YamlString:
                return YamlMember(parent, index, request, YamlNewKind.String, string.Empty);
            case AddKind.YamlNumber:
                return YamlMember(parent, index, request, YamlNewKind.Number, "0");
            case AddKind.YamlBoolean:
                return YamlMember(parent, index, request, YamlNewKind.Boolean, "false");
            case AddKind.YamlNull:
                return YamlMember(parent, index, request, YamlNewKind.Null, null);
            case AddKind.YamlMapping:
                return YamlMember(parent, index, request, YamlNewKind.Mapping, null);
            case AddKind.YamlSequence:
                return YamlMember(parent, index, request, YamlNewKind.Sequence, null);
            case AddKind.YamlComment:
                return new InsertNode(parent.Path, index, new NewYamlComment(request.Text));
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown kind.");
        }
    }

    /// <summary>True when the dialect allows comments: every JSON-family dialect except strict JSON.</summary>
    public static bool CommentsAllowed(ConfigDocument document) =>
        document.Format is JsonFormat json && json.Dialect != JsonDialect.Json;

    private static bool IsJsonValue(AddKind kind) => kind is AddKind.String or AddKind.Number or AddKind.True or AddKind.False or AddKind.Null
        or AddKind.Object or AddKind.Array;

    private static EditOp Member(DocNode parent, int index, AddRequest request, JsonValueKind kind, string? value)
    {
        string? key = parent.Kind == NodeKind.JsonObject ? request.Name : null;
        return new InsertNode(parent.Path, index, new NewJsonValue(key, kind, value));
    }

    /// <summary>A TOML value. The key is the request's name in a table (a header table, an inline table or the root), and null in an array.</summary>
    private static EditOp TomlMember(DocNode parent, int index, AddRequest request, TomlNewKind kind, string? value)
    {
        string? key = parent.Kind == NodeKind.TomlTable ? request.Name : null;
        return new InsertNode(parent.Path, index, new NewTomlValue(key, kind, value));
    }

    private static bool IsTomlValue(AddKind kind) => kind is AddKind.TomlString or AddKind.TomlInteger or AddKind.TomlFloat or AddKind.TomlBoolean
        or AddKind.TomlDateTime or AddKind.TomlTable or AddKind.TomlArray;

    /// <summary>A YAML value. The key is the request's name in a mapping, and null in a sequence.</summary>
    private static EditOp YamlMember(DocNode parent, int index, AddRequest request, YamlNewKind kind, string? value)
    {
        string? key = parent.Kind == NodeKind.YamlMapping ? request.Name : null;
        return new InsertNode(parent.Path, index, new NewYamlValue(key, kind, value));
    }

    private static bool IsYamlValue(AddKind kind) => kind is AddKind.YamlString or AddKind.YamlNumber or AddKind.YamlBoolean or AddKind.YamlNull
        or AddKind.YamlMapping or AddKind.YamlSequence;
}
