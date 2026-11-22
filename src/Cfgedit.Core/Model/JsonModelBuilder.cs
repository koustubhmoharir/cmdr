using System.Globalization;
using Cfgedit.Core.Json;

namespace Cfgedit.Core.Model;

/// <summary>Builds the document tree and the logical tree of a JSON-family concrete syntax tree.</summary>
internal static class JsonModelBuilder
{
    public static DocNode BuildTree(JsonDocument document)
    {
        var root = new DocNode(NodeKind.Document, NodePath.Empty, null, [], [], 0, document.Source.Text.Length, null);
        int comments = 0;
        foreach (JsonComment comment in document.Leading)
        {
            root.AddChild(CommentNode(root, comment, comments++));
        }

        JsonValue value = document.Root;
        root.AddChild(ValueNode(root, NodePath.Empty.Append(new ItemStep(0)), value, null, null, value.Start, value.End, isRoot: true));

        foreach (JsonComment comment in document.Trailing)
        {
            root.AddChild(CommentNode(root, comment, comments++));
        }

        return root;
    }

    /// <summary>Builds the node of a JSON value, with its parts and, for a container, its entries.</summary>
    /// <param name="isRoot">True for the document's root value, whose row starts with the zero-width <see cref="PartKind.Root"/> marker.</param>
    private static DocNode ValueNode(
        DocNode parent, NodePath path, JsonValue value, JsonKey? key, int? index, int start, int end, bool isRoot = false)
    {
        var parts = new List<DocPart>();
        if (isRoot)
        {
            parts.Add(new DocPart(PartKind.Root, "$", false, false, start, start));
        }

        if (key is not null)
        {
            parts.Add(new DocPart(PartKind.Key, key.Name, true, false, key.Start, key.End));
        }

        if (index is { } position)
        {
            parts.Add(new DocPart(PartKind.Index, position.ToString(CultureInfo.InvariantCulture), false, false, start, start));
        }

        NodeKind kind;
        JsonValueKind jsonKind;
        IReadOnlyList<JsonEntry>? entries = null;
        switch (value)
        {
            case JsonScalar scalar:
                kind = NodeKind.JsonScalar;
                jsonKind = ScalarKind(scalar.Kind);
                string text = scalar.Kind == JsonScalarKind.String ? scalar.StringValue! : scalar.Raw;
                parts.Add(new DocPart(PartKind.Value, text, true, false, scalar.Start, scalar.End));
                break;
            case JsonObject obj:
                kind = NodeKind.JsonObject;
                jsonKind = JsonValueKind.Object;
                entries = obj.Entries;
                parts.Add(SummaryPart(obj.Start, entries));
                break;
            case JsonArray array:
                kind = NodeKind.JsonArray;
                jsonKind = JsonValueKind.Array;
                entries = array.Entries;
                parts.Add(SummaryPart(array.Start, entries));
                break;
            default:
                throw new InvalidOperationException("Unknown JSON value type: " + value.GetType().Name);
        }

        var node = new DocNode(kind, path, parent, parts, [], start, end, jsonKind);
        if (entries is not null)
        {
            AddEntries(node, entries);
        }

        return node;
    }

    private static DocPart SummaryPart(int openingBracket, IReadOnlyList<JsonEntry> entries)
    {
        int count = entries.Count(entry => entry is not JsonComment);
        return new DocPart(PartKind.Summary, count.ToString(CultureInfo.InvariantCulture), false, false, openingBracket, openingBracket + 1);
    }

    private static void AddEntries(DocNode node, IReadOnlyList<JsonEntry> entries)
    {
        int comments = 0;
        int items = 0;
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonEntry entry in entries)
        {
            switch (entry)
            {
                case JsonComment comment:
                    node.AddChild(CommentNode(node, comment, comments++));
                    break;
                case JsonMember member:
                {
                    string name = member.Key.Name;
                    int ordinal = ordinals.GetValueOrDefault(name);
                    ordinals[name] = ordinal + 1;
                    NodePath path = node.Path.Append(new MemberStep(name, ordinal));
                    node.AddChild(ValueNode(node, path, member.Value, member.Key, null, member.Key.Start, member.Value.End));
                    break;
                }
                case JsonItem item:
                {
                    int index = items++;
                    NodePath path = node.Path.Append(new ItemStep(index));
                    node.AddChild(ValueNode(node, path, item.Value, null, index, item.Value.Start, item.Value.End));
                    break;
                }
                default:
                    throw new InvalidOperationException("Unknown JSON entry type: " + entry.GetType().Name);
            }
        }
    }

    private static DocNode CommentNode(DocNode parent, JsonComment comment, int index)
    {
        // The content sits between the two-character opener and the closer (none for a line comment).
        int contentEnd = comment.IsBlock ? comment.End - 2 : comment.End;
        var part = new DocPart(PartKind.Comment, comment.Content, true, false, comment.Start + 2, contentEnd);
        return new DocNode(
            NodeKind.JsonComment,
            parent.Path.Append(new CommentStep(index)),
            parent,
            [part],
            [],
            comment.Start,
            comment.End,
            null);
    }

    private static JsonValueKind ScalarKind(JsonScalarKind kind)
    {
        return kind switch
        {
            JsonScalarKind.String => JsonValueKind.String,
            JsonScalarKind.Number => JsonValueKind.Number,
            JsonScalarKind.True => JsonValueKind.True,
            JsonScalarKind.False => JsonValueKind.False,
            JsonScalarKind.Null => JsonValueKind.Null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown scalar kind."),
        };
    }

    // ----- Logical tree -----

    public static LogicalNode BuildLogical(JsonDocument document)
    {
        var children = new List<LogicalNode>();
        foreach (JsonComment comment in document.Leading)
        {
            children.Add(LogicalComment(comment));
        }

        children.Add(LogicalValue(document.Root, null));
        foreach (JsonComment comment in document.Trailing)
        {
            children.Add(LogicalComment(comment));
        }

        return new LogicalNode(LogicalKind.Document, null, null, null, [], children);
    }

    private static LogicalNode LogicalComment(JsonComment comment)
    {
        return new LogicalNode(LogicalKind.Comment, null, null, comment.Content, [], []);
    }

    private static LogicalNode LogicalValue(JsonValue value, string? name)
    {
        switch (value)
        {
            case JsonScalar scalar:
            {
                string text = scalar.Kind == JsonScalarKind.String ? scalar.StringValue! : scalar.Raw;
                return new LogicalNode(LogicalKind.Scalar, name, ScalarKind(scalar.Kind), text, [], []);
            }
            case JsonObject obj:
                return new LogicalNode(LogicalKind.Object, name, null, null, [], LogicalEntries(obj.Entries));
            case JsonArray array:
                return new LogicalNode(LogicalKind.Array, name, null, null, [], LogicalEntries(array.Entries));
            default:
                throw new InvalidOperationException("Unknown JSON value type: " + value.GetType().Name);
        }
    }

    private static List<LogicalNode> LogicalEntries(IReadOnlyList<JsonEntry> entries)
    {
        var children = new List<LogicalNode>();
        foreach (JsonEntry entry in entries)
        {
            switch (entry)
            {
                case JsonComment comment:
                    children.Add(LogicalComment(comment));
                    break;
                case JsonMember member:
                    children.Add(LogicalValue(member.Value, member.Key.Name));
                    break;
                case JsonItem item:
                    children.Add(LogicalValue(item.Value, null));
                    break;
                default:
                    throw new InvalidOperationException("Unknown JSON entry type: " + entry.GetType().Name);
            }
        }

        return children;
    }
}
