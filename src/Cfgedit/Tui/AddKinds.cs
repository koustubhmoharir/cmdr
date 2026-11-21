using Cfgedit.Core.Json;
using Cfgedit.Core.Model;

namespace Cfgedit.Tui;

/// <summary>What the Add dialog can add: a JSON value (as a member or an item), a JSON comment, or an XML node.</summary>
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
    };

    /// <summary>The label of the name field for a kind in a parent: "Key" for a JSON member, "Name" for an XML element, and null when the kind takes no name.</summary>
    public static string? NameLabel(DocNode parent, AddKind kind)
    {
        if (kind == AddKind.XmlElement)
        {
            return "Name";
        }

        return parent.Kind == NodeKind.JsonObject && IsJsonValue(kind) ? "Key" : null;
    }

    /// <summary>True when the kind is a comment, so that the dialog asks for its text.</summary>
    public static bool TakesText(AddKind kind) => kind is AddKind.LineComment or AddKind.BlockComment or AddKind.XmlComment;

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
}
