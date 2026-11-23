using Cfgedit.Core.Xml;

namespace Cfgedit.Core.Model;

/// <summary>Builds the document tree and the logical tree of an XML concrete syntax tree.</summary>
internal static class XmlModelBuilder
{
    /// <summary>
    /// The state for one parent's children: the source text, and the counters that give each child its path step. A new scope
    /// is made for each element's children, so that ordinals and indices count among siblings only.
    /// </summary>
    private sealed class Scope
    {
        private readonly Dictionary<string, int> _elements = new(StringComparer.Ordinal);
        private int _comments;
        private int _texts;
        private int _pis;

        public Scope(string source)
        {
            Source = source;
        }

        public string Source { get; }

        public int NextComment() => _comments++;

        public int NextText() => _texts++;

        public int NextPI() => _pis++;

        public int NextElement(string name)
        {
            int ordinal = _elements.GetValueOrDefault(name);
            _elements[name] = ordinal + 1;
            return ordinal;
        }
    }

    // ----- Document tree -----

    public static DocNode BuildTree(XmlDocumentSyntax document)
    {
        string source = document.Source.Text;
        var root = new DocNode(NodeKind.Document, NodePath.Empty, null, [], [], 0, source.Length, null);
        var scope = new Scope(source);
        if (document.Declaration is { } declaration)
        {
            root.AddChild(ReadOnlyNode(root, NodeKind.XmlDeclaration, new DeclarationStep(), declaration, source));
        }

        foreach (XmlNodeSyntax node in document.Prolog)
        {
            AddNode(root, node, scope);
        }

        AddElement(root, document.Root, scope);

        foreach (XmlNodeSyntax node in document.Epilog)
        {
            AddNode(root, node, scope);
        }

        return root;
    }

    private static void AddNode(DocNode parent, XmlNodeSyntax node, Scope scope)
    {
        switch (node)
        {
            case XmlTextSyntax text when text.IsWhitespace:
                // Whitespace between markup is formatting. It is not a node.
                return;
            case XmlTextSyntax text:
            {
                var part = new DocPart(PartKind.Text, text.Value, true, text.IsRaw, text.Start, text.End);
                AddChild(parent, NodeKind.XmlText, new TextStep(scope.NextText()), [part], [], text.Start, text.End);
                return;
            }
            case XmlCDataSyntax cdata:
            {
                // The content sits between the nine-character opener and the three-character closer.
                var part = new DocPart(PartKind.Text, cdata.Content, true, false, cdata.Start + 9, cdata.End - 3);
                AddChild(parent, NodeKind.XmlCData, new TextStep(scope.NextText()), [part], [], cdata.Start, cdata.End);
                return;
            }
            case XmlCommentSyntax comment:
            {
                // The content sits between the four-character opener and the three-character closer.
                var part = new DocPart(PartKind.Comment, comment.Content, true, false, comment.Start + 4, comment.End - 3);
                AddChild(parent, NodeKind.XmlComment, new CommentStep(scope.NextComment()), [part], [], comment.Start, comment.End);
                return;
            }
            case XmlProcessingInstructionSyntax pi:
            {
                // The target follows <?, and the data follows the whitespace after the target and runs up to ?>.
                int targetStart = pi.Start + 2;
                int targetEnd = targetStart + pi.Target.Length;
                int dataEnd = pi.End - 2;
                int dataStart = targetEnd;
                while (dataStart < dataEnd && IsXmlSpace(scope.Source[dataStart]))
                {
                    dataStart++;
                }

                var parts = new[]
                {
                    new DocPart(PartKind.PITarget, pi.Target, true, false, targetStart, targetEnd),
                    new DocPart(PartKind.PIData, pi.Data, true, false, dataStart, dataEnd),
                };
                AddChild(parent, NodeKind.XmlProcessingInstruction, new PIStep(scope.NextPI()), parts, [], pi.Start, pi.End);
                return;
            }
            case XmlElementSyntax element:
                AddElement(parent, element, scope);
                return;
            case XmlDoctypeSyntax doctype:
                parent.AddChild(ReadOnlyNode(parent, NodeKind.XmlDoctype, new DoctypeStep(), doctype, scope.Source));
                return;
            default:
                throw new InvalidOperationException("Unknown XML node type: " + node.GetType().Name);
        }
    }

    private static void AddElement(DocNode parent, XmlElementSyntax element, Scope scope)
    {
        NodePath path = parent.Path.Append(new ElementStep(element.Name, scope.NextElement(element.Name)));
        var parts = new List<DocPart>
        {
            new(PartKind.ElementName, element.Name, true, false, element.NameStart, element.NameStart + element.Name.Length),
        };
        for (int i = 0; i < element.Attributes.Count; i++)
        {
            XmlAttributeSyntax attribute = element.Attributes[i];
            parts.Add(new DocPart(
                PartKind.AttributeName, attribute.Name, true, false, attribute.Start, attribute.Start + attribute.Name.Length, i));
            parts.Add(new DocPart(
                PartKind.AttributeValue, attribute.Value, true, attribute.IsRaw, attribute.ValueStart + 1, attribute.End - 1, i));
        }

        bool textOnly = IsTextOnly(element);
        var endParts = new List<DocPart>();
        if (textOnly)
        {
            if (element.Children.Count == 1 && element.Children[0] is XmlTextSyntax text)
            {
                parts.Add(new DocPart(PartKind.Text, text.Value, true, text.IsRaw, text.Start, text.End));
            }
            else
            {
                // <a></a>: the text is empty, and it sits where the content would start.
                parts.Add(new DocPart(PartKind.Text, string.Empty, true, false, element.StartTagEnd, element.StartTagEnd));
            }
        }
        else if (!element.IsEmpty)
        {
            XmlEndTagSyntax endTag = element.EndTag ?? throw new InvalidOperationException("A non-empty element has no end tag.");
            endParts.Add(new DocPart(PartKind.EndName, endTag.Name, true, false, endTag.NameStart, endTag.NameStart + endTag.Name.Length));
        }

        if (IsEmptyElement(element))
        {
            // The summary is zero-width and not editable. A self-closing element has no content start, so it sits at the element's end.
            int at = element.IsEmpty ? element.End : element.StartTagEnd;
            parts.Add(new DocPart(PartKind.Summary, "0", false, false, at, at));
        }

        var node = new DocNode(NodeKind.XmlElement, path, parent, parts, endParts, element.Start, element.End, null);
        parent.AddChild(node);

        if (!textOnly && !element.IsEmpty)
        {
            var inner = new Scope(scope.Source);
            foreach (XmlNodeSyntax child in element.Children)
            {
                AddNode(node, child, inner);
            }
        }
    }

    private static void AddChild(
        DocNode parent, NodeKind kind, PathStep step, IReadOnlyList<DocPart> parts, IReadOnlyList<DocPart> endParts, int start, int end)
    {
        parent.AddChild(new DocNode(kind, parent.Path.Append(step), parent, parts, endParts, start, end, null));
    }

    /// <summary>
    /// An element whose only content is text: no child elements, comments, CDATA or PIs, and at most one text child. A
    /// self-closing <c>&lt;a/&gt;</c> is not text-only; it has no content and no text part. Neither is an element whose only
    /// child is whitespace with a line break (<c>&lt;a&gt;\n&lt;/a&gt;</c>): that is an empty container written across lines,
    /// shown with an end row so that children can be added, not as a text value made of a line break.
    /// </summary>
    private static bool IsTextOnly(XmlElementSyntax element)
    {
        if (element.IsEmpty)
        {
            return false;
        }

        if (element.Children.Count == 0)
        {
            return true;
        }

        return element.Children.Count == 1
            && element.Children[0] is XmlTextSyntax text
            && !(text.IsWhitespace && text.Raw.AsSpan().IndexOfAny('\r', '\n') >= 0);
    }

    /// <summary>
    /// An empty element: a self-closing one, <c>&lt;a&gt;&lt;/a&gt;</c>, or an element written across lines whose only content is
    /// whitespace between markup. It has no child node and no text, so it gets a summary part, <c>(0)</c>. An element with text, or with
    /// any child element, comment, CDATA or PI, does not.
    /// </summary>
    private static bool IsEmptyElement(XmlElementSyntax element)
    {
        if (element.IsEmpty)
        {
            return true;
        }

        if (IsTextOnly(element))
        {
            return element.Children.Count == 0;
        }

        return element.Children.All(child => child is XmlTextSyntax { IsWhitespace: true });
    }

    private static DocNode ReadOnlyNode(DocNode parent, NodeKind kind, PathStep step, XmlNodeSyntax syntax, string source)
    {
        var part = new DocPart(PartKind.ReadOnly, source[syntax.Start..syntax.End], false, false, syntax.Start, syntax.End);
        return new DocNode(kind, parent.Path.Append(step), parent, [part], [], syntax.Start, syntax.End, null);
    }

    private static bool IsXmlSpace(char c)
    {
        return c is ' ' or '\t' or '\r' or '\n';
    }

    // ----- Logical tree -----

    public static LogicalNode BuildLogical(XmlDocumentSyntax document)
    {
        var children = new List<LogicalNode>();
        if (document.Declaration is { } declaration)
        {
            var attributes = new List<LogicalAttribute> { new("version", declaration.Version) };
            if (declaration.Encoding is { } encoding)
            {
                attributes.Add(new LogicalAttribute("encoding", encoding));
            }

            if (declaration.Standalone is { } standalone)
            {
                attributes.Add(new LogicalAttribute("standalone", standalone));
            }

            children.Add(new LogicalNode(LogicalKind.Declaration, null, null, null, attributes, []));
        }

        foreach (XmlNodeSyntax node in document.Prolog)
        {
            AddLogical(children, node);
        }

        children.Add(LogicalElement(document.Root));

        foreach (XmlNodeSyntax node in document.Epilog)
        {
            AddLogical(children, node);
        }

        return new LogicalNode(LogicalKind.Document, null, null, null, [], children);
    }

    private static void AddLogical(List<LogicalNode> children, XmlNodeSyntax node)
    {
        switch (node)
        {
            case XmlTextSyntax text when text.IsWhitespace:
                return;
            case XmlTextSyntax text:
                children.Add(Leaf(LogicalKind.Text, null, text.Value));
                return;
            case XmlCDataSyntax cdata:
                children.Add(Leaf(LogicalKind.CData, null, cdata.Content));
                return;
            case XmlCommentSyntax comment:
                children.Add(Leaf(LogicalKind.Comment, null, comment.Content));
                return;
            case XmlProcessingInstructionSyntax pi:
                children.Add(Leaf(LogicalKind.ProcessingInstruction, pi.Target, pi.Data));
                return;
            case XmlDoctypeSyntax doctype:
                children.Add(new LogicalNode(LogicalKind.Doctype, doctype.Name, null, doctype.InternalSubset, [], []));
                return;
            case XmlElementSyntax element:
                children.Add(LogicalElement(element));
                return;
            default:
                throw new InvalidOperationException("Unknown XML node type: " + node.GetType().Name);
        }
    }

    private static LogicalNode LogicalElement(XmlElementSyntax element)
    {
        var attributes = element.Attributes.Select(a => new LogicalAttribute(a.Name, a.Value)).ToList();
        var children = new List<LogicalNode>();
        if (element.IsEmpty || IsTextOnly(element))
        {
            // A text-only element has its text as one text child. Empty text is no child at all, so <a/>, <a></a> and an empty
            // container written across lines (whose whitespace is skipped below) all compare equal.
            string text = element.Children.Count == 1 && element.Children[0] is XmlTextSyntax single ? single.Value : string.Empty;
            if (text.Length > 0)
            {
                children.Add(Leaf(LogicalKind.Text, null, text));
            }
        }
        else
        {
            foreach (XmlNodeSyntax child in element.Children)
            {
                AddLogical(children, child);
            }
        }

        return new LogicalNode(LogicalKind.Element, element.Name, null, null, attributes, children);
    }

    private static LogicalNode Leaf(LogicalKind kind, string? name, string value)
    {
        return new LogicalNode(kind, name, null, value, [], []);
    }
}
