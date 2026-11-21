namespace Cfgedit.Core.Model;

/// <summary>
/// Layout rules over the document tree and its source text, shared by the logical application and the JSON planner: which
/// comments belong to an entry, and where a JSON entry's comma is. They read only the tree and the text, never the CST.
/// </summary>
internal static class EditLayout
{
    /// <summary>True for a JSON or XML comment node.</summary>
    public static bool IsComment(DocNode node) => node.Kind is NodeKind.JsonComment or NodeKind.XmlComment;

    /// <summary>True for a node whose children are entries that can carry attached comments.</summary>
    public static bool HasEntries(DocNode node) => node.Kind is NodeKind.JsonObject or NodeKind.JsonArray or NodeKind.XmlElement;

    /// <summary>True for a JSON object or array.</summary>
    public static bool IsJsonContainer(DocNode node) => node.Kind is NodeKind.JsonObject or NodeKind.JsonArray;

    /// <summary>
    /// The offset of the comma that follows a JSON entry, or null when it has none. Only whitespace and comments can stand
    /// between an entry and its comma.
    /// </summary>
    public static int? CommaAfter(DocNode parent, int index, string text)
    {
        var children = parent.Children;
        if (!IsJsonContainer(parent) || IsComment(children[index]))
        {
            return null;
        }

        int position = children[index].End;
        int next = index + 1;
        while (true)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            if (position < text.Length && text[position] == ',')
            {
                return position;
            }

            if (next < children.Count && IsComment(children[next]) && children[next].Start == position)
            {
                position = children[next].End;
                next++;
                continue;
            }

            return null;
        }
    }

    /// <summary>The end of a child's extent: its end, or the end of its comma when it has one.</summary>
    public static int Extent(DocNode parent, int index, string text)
    {
        int? comma = CommaAfter(parent, index, text);
        return comma is { } position ? position + 1 : parent.Children[index].End;
    }

    /// <summary>
    /// The sibling range [First, Last] that moves, copies or deletes with the child at <paramref name="index"/>: the comments
    /// directly above it (each on its own line, with no blank line between them or before the entry) and the comments that
    /// start on the line where it ends. A comment, or a child of a node that holds no entries, stands alone.
    /// </summary>
    public static (int First, int Last) Group(DocNode parent, int index, string text)
    {
        var children = parent.Children;
        if (IsComment(children[index]) || !HasEntries(parent))
        {
            return (index, index);
        }

        int first = index;
        for (int k = index - 1; k >= 0 && IsComment(children[k]); k--)
        {
            if (!IsLeadingComment(text, children[k], children[k + 1].Start))
            {
                break;
            }

            first = k;
        }

        int last = index;
        int anchor = Extent(parent, index, text);
        for (int k = index + 1; k < children.Count && IsComment(children[k]); k++)
        {
            if (HasLineBreak(text, anchor, children[k].Start))
            {
                break;
            }

            last = k;
        }

        return (first, last);
    }

    /// <summary>True when the text from the line start up to the offset holds only spaces and tabs.</summary>
    public static bool StartsLine(string text, int offset)
    {
        for (int i = offset - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c is '\n' or '\r')
            {
                return true;
            }

            if (c is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when only spaces and tabs stand between the offset and the end of its line.</summary>
    public static bool RestOfLineBlank(string text, int offset)
    {
        for (int i = offset; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\n' or '\r')
            {
                return true;
            }

            if (c is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The offset of the first character of the line that holds the offset.</summary>
    public static int LineStart(string text, int offset)
    {
        int i = offset;
        while (i > 0 && text[i - 1] is not ('\n' or '\r'))
        {
            i--;
        }

        return i;
    }

    /// <summary>The spaces and tabs that start the line that holds the offset.</summary>
    public static string LineIndent(string text, int offset)
    {
        int start = LineStart(text, offset);
        int end = start;
        while (end < text.Length && text[end] is ' ' or '\t')
        {
            end++;
        }

        return text[start..end];
    }

    /// <summary>The offset just after the line break that ends the line holding the offset, or the text length.</summary>
    public static int AfterLineBreak(string text, int offset)
    {
        int i = offset;
        while (i < text.Length && text[i] is not ('\n' or '\r'))
        {
            i++;
        }

        if (i >= text.Length)
        {
            return text.Length;
        }

        if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
        {
            return i + 2;
        }

        return i + 1;
    }

    /// <summary>True when a line break lies in [from, to).</summary>
    public static bool HasLineBreak(string text, int from, int to)
    {
        return text.AsSpan(from, to - from).IndexOfAny('\r', '\n') >= 0;
    }

    /// <summary>Counts the line breaks in [from, to). CRLF is one break.</summary>
    public static int CountLineBreaks(string text, int from, int to)
    {
        int count = 0;
        for (int i = from; i < to; i++)
        {
            if (text[i] == '\n')
            {
                count++;
            }
            else if (text[i] == '\r' && !(i + 1 < to && text[i + 1] == '\n'))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsLeadingComment(string text, DocNode comment, int nextStart)
    {
        if (!StartsLine(text, comment.Start) || comment.End > nextStart)
        {
            return false;
        }

        for (int i = comment.End; i < nextStart; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
        }

        return CountLineBreaks(text, comment.End, nextStart) == 1;
    }
}

/// <summary>
/// The independent logical application of an edit: the logical tree after the operation, computed from the tree before it.
/// The verification step compares it with the logical tree of the reparsed text. It maps a node path to logical child
/// indices through the document tree, and it knows nothing of formatting beyond the attachment rules in
/// <see cref="EditLayout"/>. An operation that cannot be applied to the tree throws <see cref="InvalidOperationException"/>.
/// </summary>
internal static class LogicalEdit
{
    /// <summary>Applies an operation to a logical tree.</summary>
    /// <param name="before">The document the operation applies to. Its tree gives the paths their meaning.</param>
    /// <param name="root">The logical tree of <paramref name="before"/>.</param>
    /// <param name="op">The operation. It is assumed valid.</param>
    public static LogicalNode Apply(ConfigDocument before, LogicalNode root, EditOp op)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(op);
        return op switch
        {
            SetPart set => SetPartAt(before, root, set),
            InsertNode insert => InsertAt(before, root, insert),
            InsertAttribute insertAttribute => ChangeAt(
                before, root, insertAttribute.Element, node => WithAttributes(node, list => list.Insert(insertAttribute.Index, new LogicalAttribute(insertAttribute.Name, insertAttribute.Value)))),
            DeleteNode delete => DeleteAt(before, root, delete.Node),
            DeleteAttribute deleteAttribute => ChangeAt(before, root, deleteAttribute.Element, node => WithAttributes(node, list => list.RemoveAt(deleteAttribute.Index))),
            MoveNode move => MoveAt(before, root, move.Node, move.NewIndex),
            MoveAttribute moveAttribute => ChangeAt(before, root, moveAttribute.Element, node => WithAttributes(node, list => MoveItem(list, moveAttribute.Index, moveAttribute.NewIndex))),
            ChangeJsonType change => ChangeAt(before, root, change.Node, node => ChangeType(node, change)),
            DuplicateNode duplicate => DuplicateAt(before, root, duplicate.Node),
            _ => throw new InvalidOperationException("Unknown edit operation: " + op.GetType().Name),
        };
    }

    private static LogicalNode SetPartAt(ConfigDocument before, LogicalNode root, SetPart set)
    {
        DocNode target = Locate(before, set.Node);
        return ChangeAt(before, root, set.Node, node => set.Part switch
        {
            PartKind.Key or PartKind.ElementName or PartKind.EndName or PartKind.PITarget => Renamed(node, set.Value),
            PartKind.AttributeName => WithAttributes(node, list => list[set.AttributeIndex] = new LogicalAttribute(set.Value, list[set.AttributeIndex].Value)),
            PartKind.AttributeValue => WithAttributes(node, list => list[set.AttributeIndex] = new LogicalAttribute(list[set.AttributeIndex].Name, set.Value)),
            PartKind.Text when target.Kind == NodeKind.XmlElement => WithText(node, set.Value),
            PartKind.Text or PartKind.Value or PartKind.Comment or PartKind.PIData => WithValue(node, set.Value),
            _ => throw new InvalidOperationException("The part " + set.Part + " cannot be set."),
        });
    }

    private static LogicalNode InsertAt(ConfigDocument before, LogicalNode root, InsertNode insert)
    {
        DocNode parent = Locate(before, insert.Parent);
        LogicalNode created = Create(insert.Node);
        return ChangeAt(before, root, insert.Parent, node =>
        {
            var children = node.Children.ToList();
            int position = insert.Index;
            if (parent.Kind == NodeKind.XmlElement && parent.Children.Count == 0 && children.Count > 0)
            {
                // A text-only element's text is its only child, and new children go after it.
                position = children.Count;
            }

            if (position < 0 || position > children.Count)
            {
                throw new InvalidOperationException($"Index {position} is outside the children of {insert.Parent}.");
            }

            children.Insert(position, created);
            return WithChildren(node, children);
        });
    }

    private static LogicalNode DeleteAt(ConfigDocument before, LogicalNode root, NodePath path)
    {
        DocNode node = Locate(before, path);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be deleted.");
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), before.Source.Text);
        return ChangeAt(before, root, parent.Path, container =>
        {
            var children = container.Children.ToList();
            children.RemoveRange(first, last - first + 1);
            return WithChildren(container, children);
        });
    }

    private static LogicalNode MoveAt(ConfigDocument before, LogicalNode root, NodePath path, int newIndex)
    {
        DocNode node = Locate(before, path);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be moved.");
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), before.Source.Text);
        return ChangeAt(before, root, parent.Path, container =>
        {
            var children = container.Children.ToList();
            List<LogicalNode> moved = children.GetRange(first, last - first + 1);
            children.RemoveRange(first, moved.Count);
            int position = Math.Clamp(newIndex, 0, children.Count);
            children.InsertRange(position, moved);
            return WithChildren(container, children);
        });
    }

    private static LogicalNode DuplicateAt(ConfigDocument before, LogicalNode root, NodePath path)
    {
        DocNode node = Locate(before, path);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be duplicated.");
        int index = IndexOf(parent, node);
        (int first, int last) = EditLayout.Group(parent, index, before.Source.Text);
        return ChangeAt(before, root, parent.Path, container =>
        {
            var children = container.Children.ToList();
            var copies = children.GetRange(first, last - first + 1);
            if (!EditLayout.IsComment(node) && copies.Count > 0 && node.Parent!.Kind == NodeKind.JsonObject)
            {
                int copyOfEntry = index - first;
                string key = FreshKey(children, copies[copyOfEntry].Name!);
                copies[copyOfEntry] = Renamed(copies[copyOfEntry], key);
            }

            children.InsertRange(last + 1, copies);
            return WithChildren(container, children);
        });
    }

    private static LogicalNode ChangeAt(ConfigDocument before, LogicalNode root, NodePath path, Func<LogicalNode, LogicalNode> change)
    {
        return Replace(root, Indices(before, path), 0, change);
    }

    private static LogicalNode Replace(LogicalNode node, int[] indices, int depth, Func<LogicalNode, LogicalNode> change)
    {
        if (depth == indices.Length)
        {
            return change(node);
        }

        var children = node.Children.ToList();
        int k = indices[depth];
        children[k] = Replace(children[k], indices, depth + 1, change);
        return new LogicalNode(node.Kind, node.Name, node.ScalarKind, node.Value, node.Attributes, children);
    }

    /// <summary>The logical child indices that lead from the document node to a path.</summary>
    private static int[] Indices(ConfigDocument document, NodePath path)
    {
        var indices = new int[path.Count];
        DocNode node = document.Root;
        for (int k = 0; k < path.Count; k++)
        {
            int found = -1;
            for (int c = 0; c < node.Children.Count; c++)
            {
                if (node.Children[c].Path.Steps[^1] == path.Steps[k])
                {
                    found = c;
                    break;
                }
            }

            if (found < 0)
            {
                throw new InvalidOperationException($"There is no node at {path}.");
            }

            indices[k] = found;
            node = node.Children[found];
        }

        return indices;
    }

    private static DocNode Locate(ConfigDocument document, NodePath path)
    {
        return document.Resolve(path) ?? throw new InvalidOperationException($"There is no node at {path}.");
    }

    private static int IndexOf(DocNode parent, DocNode child)
    {
        for (int c = 0; c < parent.Children.Count; c++)
        {
            if (ReferenceEquals(parent.Children[c], child))
            {
                return c;
            }
        }

        throw new InvalidOperationException("The node is not a child of its parent.");
    }

    private static LogicalNode Create(NewNode node)
    {
        switch (node)
        {
            case NewJsonValue value:
                return ValueNode(value.Key, value.Kind, value.Value);
            case NewJsonComment comment:
                return new LogicalNode(LogicalKind.Comment, null, null, comment.Text, [], []);
            case NewXmlElement element:
            {
                var attributes = element.Attributes.Select(a => new LogicalAttribute(a.Name, a.Value)).ToList();
                var children = new List<LogicalNode>();
                if (!string.IsNullOrEmpty(element.Text))
                {
                    children.Add(new LogicalNode(LogicalKind.Text, null, null, element.Text, [], []));
                }

                return new LogicalNode(LogicalKind.Element, element.Name, null, null, attributes, children);
            }
            case NewXmlText text:
                return new LogicalNode(text.CData ? LogicalKind.CData : LogicalKind.Text, null, null, text.Text, [], []);
            case NewXmlComment comment:
                return new LogicalNode(LogicalKind.Comment, null, null, comment.Text, [], []);
            default:
                throw new InvalidOperationException("Unknown new node: " + node.GetType().Name);
        }
    }

    private static LogicalNode ValueNode(string? name, JsonValueKind kind, string? value)
    {
        return kind switch
        {
            JsonValueKind.Object => new LogicalNode(LogicalKind.Object, name, null, null, [], []),
            JsonValueKind.Array => new LogicalNode(LogicalKind.Array, name, null, null, [], []),
            JsonValueKind.String => new LogicalNode(LogicalKind.Scalar, name, JsonValueKind.String, value ?? string.Empty, [], []),
            JsonValueKind.Number => new LogicalNode(LogicalKind.Scalar, name, JsonValueKind.Number, value ?? throw new InvalidOperationException("A number needs a value."), [], []),
            JsonValueKind.True => new LogicalNode(LogicalKind.Scalar, name, JsonValueKind.True, "true", [], []),
            JsonValueKind.False => new LogicalNode(LogicalKind.Scalar, name, JsonValueKind.False, "false", [], []),
            JsonValueKind.Null => new LogicalNode(LogicalKind.Scalar, name, JsonValueKind.Null, "null", [], []),
            _ => throw new InvalidOperationException("Unknown JSON kind: " + kind),
        };
    }

    private static LogicalNode ChangeType(LogicalNode node, ChangeJsonType change)
    {
        if (change.Kind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return ValueNode(node.Name, change.Kind, null);
        }

        string? value = change.Kind switch
        {
            JsonValueKind.String => change.Value ?? string.Empty,
            JsonValueKind.Number => change.Value,
            _ => null,
        };
        return ValueNode(node.Name, change.Kind, value);
    }

    private static LogicalNode Renamed(LogicalNode node, string name)
    {
        return new LogicalNode(node.Kind, name, node.ScalarKind, node.Value, node.Attributes, node.Children);
    }

    private static LogicalNode WithValue(LogicalNode node, string value)
    {
        return new LogicalNode(node.Kind, node.Name, node.ScalarKind, value, node.Attributes, node.Children);
    }

    private static LogicalNode WithChildren(LogicalNode node, List<LogicalNode> children)
    {
        return new LogicalNode(node.Kind, node.Name, node.ScalarKind, node.Value, node.Attributes, children);
    }

    private static LogicalNode WithAttributes(LogicalNode node, Action<List<LogicalAttribute>> change)
    {
        var attributes = node.Attributes.ToList();
        change(attributes);
        return new LogicalNode(node.Kind, node.Name, node.ScalarKind, node.Value, attributes, node.Children);
    }

    private static LogicalNode WithText(LogicalNode element, string text)
    {
        var children = element.Children.ToList();
        bool hasText = children.Count == 1 && children[0].Kind == LogicalKind.Text;
        if (text.Length == 0)
        {
            if (hasText)
            {
                children.RemoveAt(0);
            }
        }
        else if (hasText)
        {
            children[0] = WithValue(children[0], text);
        }
        else
        {
            children.Insert(0, new LogicalNode(LogicalKind.Text, null, null, text, [], []));
        }

        return WithChildren(element, children);
    }

    private static void MoveItem(List<LogicalAttribute> list, int index, int newIndex)
    {
        LogicalAttribute item = list[index];
        list.RemoveAt(index);
        list.Insert(Math.Clamp(newIndex, 0, list.Count), item);
    }

    /// <summary>The key for a copy: <c>name-copy</c>, then <c>name-copy2</c> and so on, unique among the siblings.</summary>
    private static string FreshKey(IReadOnlyList<LogicalNode> siblings, string name)
    {
        string candidate = name + "-copy";
        for (int n = 2; siblings.Any(s => s.Name == candidate); n++)
        {
            candidate = name + "-copy" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return candidate;
    }
}
