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
        if (source.Kind is NodeKind.TomlScalar or NodeKind.TomlTable or NodeKind.TomlArray)
        {
            return TomlShape(document, source, sibling, container, parent, index);
        }

        if (source.Kind is NodeKind.YamlScalar or NodeKind.YamlMapping or NodeKind.YamlSequence)
        {
            return YamlShape(document, source, sibling, container, parent, index);
        }

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

    /// <summary>
    /// The TOML shape of an item of an array. An array-of-tables item becomes a new item (a section) whose scalars and arrays and inline
    /// tables keep their keys, with empty values; its subtables are not copied. An inline table in an inline array is the same, inline. A
    /// scalar or an array in an inline array becomes one empty value of its kind. A date-time keeps its value, as it has no empty form.
    /// </summary>
    private static ShapedInsert TomlShape(ConfigDocument document, DocNode source, NodePath sibling, DocNode container, NodePath parent, int index)
    {
        if (container.Kind != NodeKind.TomlArray)
        {
            throw new ArgumentException("A TOML key/value has a key, so its shape cannot be copied into this place.", nameof(sibling));
        }

        var model = (TomlModel)document.Syntax;
        TomlContainer shape = model.ShapeOf(container);
        var steps = new List<EditOp>();
        if (source.Kind == NodeKind.TomlTable)
        {
            TomlContainer itemShape = model.ShapeOf(source);
            bool tableItem = shape == TomlContainer.ArrayOfTables && itemShape == TomlContainer.Header;
            bool inlineItem = shape == TomlContainer.Array && itemShape == TomlContainer.Inline;
            if (!tableItem && !inlineItem)
            {
                throw new ArgumentException("The node at " + sibling + " cannot be copied into this place.", nameof(sibling));
            }

            steps.Add(new InsertNode(parent, index, new NewTomlValue(null, TomlNewKind.Table, null)));
            NodePath created = parent.Append(new ItemStep(CountBefore(container, index, child => child.Kind != NodeKind.TomlComment)));
            int position = 0;
            foreach (DocNode member in source.Children)
            {
                if (member.Path.Steps[^1] is not MemberStep step)
                {
                    continue;
                }

                switch (member.Kind)
                {
                    case NodeKind.TomlScalar:
                        (TomlNewKind kind, string? text) = EmptyTomlScalar(member);
                        steps.Add(new InsertNode(created, position++, new NewTomlValue(step.Key, kind, text)));
                        break;
                    case NodeKind.TomlArray when model.ShapeOf(member) == TomlContainer.Array:
                        steps.Add(new InsertNode(created, position++, new NewTomlValue(step.Key, TomlNewKind.Array, null)));
                        break;
                    case NodeKind.TomlTable when model.ShapeOf(member) == TomlContainer.Inline:
                        steps.Add(new InsertNode(created, position++, new NewTomlValue(step.Key, TomlNewKind.Table, null)));
                        break;
                }
            }

            return new ShapedInsert(new CompositeEdit(steps), created);
        }

        if (shape != TomlContainer.Array)
        {
            throw new ArgumentException("The node at " + sibling + " cannot be copied into this place.", nameof(sibling));
        }

        NodePath item = parent.Append(new ItemStep(CountBefore(container, index, child => child.Kind != NodeKind.TomlComment)));
        if (source.Kind == NodeKind.TomlScalar)
        {
            (TomlNewKind kind, string? text) = EmptyTomlScalar(source);
            steps.Add(new InsertNode(parent, index, new NewTomlValue(null, kind, text)));
        }
        else if (source.Kind == NodeKind.TomlArray && model.ShapeOf(source) == TomlContainer.Array)
        {
            steps.Add(new InsertNode(parent, index, new NewTomlValue(null, TomlNewKind.Array, null)));
        }
        else
        {
            throw new ArgumentException("The node at " + sibling + " cannot be copied into this place.", nameof(sibling));
        }

        return new ShapedInsert(new CompositeEdit(steps), item);
    }

    /// <summary>
    /// The YAML shape of an item of a sequence. A mapping item becomes a new mapping item whose scalar members keep their keys, with empty
    /// values of their kinds, and whose collection members become <c>{}</c> or <c>[]</c>. Aliases are not copied, and neither are anchors and
    /// tags. A scalar item becomes one empty value of its kind, and a sequence item becomes <c>[]</c>. The insert is a composite: the item, then
    /// its members, so the item is written by the planner's own rules (an empty mapping item becomes block style).
    /// </summary>
    private static ShapedInsert YamlShape(ConfigDocument document, DocNode source, NodePath sibling, DocNode container, NodePath parent, int index)
    {
        if (sibling.Steps[^1] is MemberStep)
        {
            throw new ArgumentException("A YAML mapping entry has a key, so its shape cannot be copied into this place.", nameof(sibling));
        }

        if (container.Kind != NodeKind.YamlSequence)
        {
            throw new ArgumentException("A YAML item has no key, so its shape cannot be copied into this place.", nameof(sibling));
        }

        var steps = new List<EditOp>();
        NodePath created = parent.Append(new ItemStep(CountBefore(container, index, child => !EditLayout.IsComment(child))));
        switch (source.Kind)
        {
            case NodeKind.YamlScalar:
                (YamlNewKind kind, string? text) = EmptyYaml(source);
                steps.Add(new InsertNode(parent, index, new NewYamlValue(null, kind, text)));
                break;
            case NodeKind.YamlSequence:
                steps.Add(new InsertNode(parent, index, new NewYamlValue(null, YamlNewKind.Sequence, null)));
                break;
            default:
                steps.Add(new InsertNode(parent, index, new NewYamlValue(null, YamlNewKind.Mapping, null)));
                int position = 0;
                foreach (DocNode member in source.Children)
                {
                    if (member.Path.Steps[^1] is not MemberStep step)
                    {
                        continue;
                    }

                    switch (member.Kind)
                    {
                        case NodeKind.YamlScalar:
                            (YamlNewKind memberKind, string? memberText) = EmptyYaml(member);
                            steps.Add(new InsertNode(created, position++, new NewYamlValue(step.Key, memberKind, memberText)));
                            break;
                        case NodeKind.YamlSequence:
                            steps.Add(new InsertNode(created, position++, new NewYamlValue(step.Key, YamlNewKind.Sequence, null)));
                            break;
                        case NodeKind.YamlMapping:
                            steps.Add(new InsertNode(created, position++, new NewYamlValue(step.Key, YamlNewKind.Mapping, null)));
                            break;
                    }
                }

                break;
        }

        return new ShapedInsert(new CompositeEdit(steps), created);
    }

    /// <summary>The empty value of a YAML scalar of its kind: <c>""</c> for a string, <c>0</c> or <c>0.0</c>, <c>false</c>, and <c>null</c>.</summary>
    private static (YamlNewKind Kind, string? Text) EmptyYaml(DocNode scalar)
    {
        return scalar.YamlKind switch
        {
            YamlValueKind.String => (YamlNewKind.String, string.Empty),
            YamlValueKind.Integer => (YamlNewKind.Number, "0"),
            YamlValueKind.Float => (YamlNewKind.Number, "0.0"),
            YamlValueKind.Boolean => (YamlNewKind.Boolean, "false"),
            _ => (YamlNewKind.Null, null),
        };
    }

    /// <summary>The empty value of a TOML scalar: "" for a string, "0", "0.0" and "false", and a date-time's own text.</summary>
    private static (TomlNewKind Kind, string? Text) EmptyTomlScalar(DocNode scalar)
    {
        return scalar.TomlKind switch
        {
            TomlValueKind.String => (TomlNewKind.String, string.Empty),
            TomlValueKind.Integer => (TomlNewKind.Integer, "0"),
            TomlValueKind.Float => (TomlNewKind.Float, "0.0"),
            TomlValueKind.Boolean => (TomlNewKind.Boolean, "false"),
            _ => (TomlNewKind.DateTime, scalar.Parts.First(part => part.Kind == PartKind.Value).Value),
        };
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
