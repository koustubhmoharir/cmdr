namespace Cfgedit.Core.Model;

/// <summary>
/// The insert that makes a node shaped like a sibling, with its values emptied. This is the one implementation behind
/// <see cref="ConfigDocument.ShapeLike"/> and <see cref="TableModel.AddRow"/>, so a table row and a node added with the editor's
/// keys have the same shape.
/// </summary>
/// <remarks>
/// The shape is the sibling's own kind and names, one level deep: a JSON object's members (keys, with empty values) and an XML
/// element's attributes (names, with empty values) and its child elements (names, with no attributes or text). A JSON scalar's
/// value is empty, and a number's is "0". A JSON array is copied empty.
/// </remarks>
internal static class SiblingShape
{
    /// <summary>
    /// Builds the insert of a node shaped like <paramref name="sibling"/> at <paramref name="index"/> among the children of
    /// <paramref name="parent"/>.
    /// </summary>
    /// <param name="document">The document both paths name.</param>
    /// <param name="sibling">The node to copy the shape of.</param>
    /// <param name="parent">The node the new node goes into.</param>
    /// <param name="index">The position among the parent's children, comments included.</param>
    /// <param name="keepElement">
    /// Decides which child elements of an XML sibling are copied, or null to copy them all. A table keeps only its columns.
    /// </param>
    /// <exception cref="ArgumentException">A path names no node, or the sibling has no shape to copy in that parent.</exception>
    public static ShapedInsert Build(ConfigDocument document, NodePath sibling, NodePath parent, int index, Func<DocNode, bool>? keepElement)
    {
        DocNode source = document.Resolve(sibling) ?? throw new ArgumentException($"There is no node at {sibling}.", nameof(sibling));
        DocNode container = document.Resolve(parent) ?? throw new ArgumentException($"There is no node at {parent}.", nameof(parent));
        var steps = new List<EditOp>();
        NodePath created;
        if (source.Kind is NodeKind.JsonScalar or NodeKind.JsonObject or NodeKind.JsonArray)
        {
            if (container.Kind != NodeKind.JsonArray)
            {
                throw new ArgumentException("A JSON member has a key, so its shape cannot be copied into this place.", nameof(sibling));
            }

            (JsonValueKind kind, string? text) = EmptyValue(source.JsonKind!.Value);
            steps.Add(new InsertNode(parent, index, new NewJsonValue(null, kind, text)));
            created = parent.Append(new ItemStep(CountBefore(container, index, child => child.Path.Steps[^1] is ItemStep)));
            if (kind == JsonValueKind.Object)
            {
                AddMembers(steps, created, source);
            }
        }
        else if (source.Kind == NodeKind.XmlElement)
        {
            string name = ElementName(source);
            List<(string Name, string Value)> attributes = source.Parts
                .Where(part => part.Kind == PartKind.AttributeName)
                .Select(part => (Name: part.Value, Value: string.Empty))
                .ToList();
            string? text = TextPart(source) is not null ? string.Empty : null;
            steps.Add(new InsertNode(parent, index, new NewXmlElement(name, attributes, text)));
            created = parent.Append(new ElementStep(
                name, CountBefore(container, index, child => child.Kind == NodeKind.XmlElement && ElementName(child) == name)));
            int position = 0;
            foreach (DocNode child in source.Children)
            {
                if (child.Kind == NodeKind.XmlElement && (keepElement is null || keepElement(child)))
                {
                    steps.Add(new InsertNode(created, position++, new NewXmlElement(ElementName(child), [], string.Empty)));
                }
            }
        }
        else
        {
            throw new ArgumentException($"The node at {sibling} has no shape to copy.", nameof(sibling));
        }

        return new ShapedInsert(new CompositeEdit(steps), created);
    }

    /// <summary>The empty value of a JSON kind: "" for a string, "0" for a number, and no text for the others.</summary>
    internal static (JsonValueKind Kind, string? Text) EmptyValue(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.String => (JsonValueKind.String, string.Empty),
            JsonValueKind.Number => (JsonValueKind.Number, "0"),
            JsonValueKind.True or JsonValueKind.False => (JsonValueKind.False, null),
            JsonValueKind.Null => (JsonValueKind.Null, null),
            JsonValueKind.Object => (JsonValueKind.Object, null),
            JsonValueKind.Array => (JsonValueKind.Array, null),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown JSON value kind."),
        };
    }

    /// <summary>The element's name, from its path.</summary>
    internal static string ElementName(DocNode element)
    {
        return ((ElementStep)element.Path.Steps[^1]).Name;
    }

    /// <summary>The node's text part, or null when it has none.</summary>
    internal static DocPart? TextPart(DocNode node)
    {
        return node.Parts.FirstOrDefault(part => part.Kind == PartKind.Text);
    }

    /// <summary>The number of children before <paramref name="index"/> that match the predicate.</summary>
    internal static int CountBefore(DocNode container, int index, Func<DocNode, bool> predicate)
    {
        int count = 0;
        for (int k = 0; k < index; k++)
        {
            if (predicate(container.Children[k]))
            {
                count++;
            }
        }

        return count;
    }

    // The members of an object sibling, copied with empty values. A key that repeats keeps its first member only, and comments
    // are skipped.
    private static void AddMembers(List<EditOp> steps, NodePath created, DocNode source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int position = 0;
        foreach (DocNode member in source.Children)
        {
            if (member.Path.Steps[^1] is not MemberStep step || !seen.Add(step.Key))
            {
                continue;
            }

            (JsonValueKind memberKind, string? memberText) = EmptyValue(member.JsonKind!.Value);
            steps.Add(new InsertNode(created, position++, new NewJsonValue(step.Key, memberKind, memberText)));
        }
    }
}
