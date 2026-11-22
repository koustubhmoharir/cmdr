using System.Text;
using Cfgedit.Core.Xml;

namespace Cfgedit.Core.Model;

/// <summary>
/// Plans the text edits for an operation on an XML document. It works on the document tree, the concrete syntax tree and the
/// source text: the tree says what the nodes are and where their parts are, the syntax tree gives the spans of names, quotes
/// and tags, and the text gives the layout that new text copies. Every edit replaces a span of the old text, and the spans it
/// leaves alone keep their bytes.
/// </summary>
/// <remarks>
/// Whitespace-only text between elements is formatting. It is the indentation that new nodes copy, and it is not a node. Text
/// that has non-whitespace content, or a CDATA section, makes its element mixed content: new and moved nodes then go in with
/// no added whitespace, and removing a node removes only its own span. Two text runs must never become one run, because the
/// logical check would see a different tree, so an edit that would join them is refused.
/// </remarks>
internal sealed class XmlEditPlanner
{
    private const string XmlPrefix = "xml";
    private const string XmlnsPrefix = "xmlns";
    private const string XmlnsDeclarationStart = "xmlns:";

    private readonly ConfigDocument _document;
    private readonly XmlDocumentSyntax _syntax;
    private readonly string _text;
    private readonly string _newline;
    private readonly string _unit;
    private readonly Dictionary<int, XmlElementSyntax> _elements = [];
    private readonly List<TextEdit> _edits = [];
    private char? _documentQuote;

    /// <summary>Creates a planner for a parsed XML document.</summary>
    public XmlEditPlanner(ConfigDocument document)
    {
        _document = document;
        _syntax = (XmlDocumentSyntax)document.Syntax;
        _text = document.Source.Text;
        _newline = document.Source.Newline;
        _unit = EditLayout.InferIndentUnit(_text);
        IndexElements(_syntax.Root);
    }

    /// <summary>Plans an operation. Throws <see cref="CfgeditException"/> when the operation is invalid.</summary>
    public IReadOnlyList<TextEdit> Plan(EditOp op)
    {
        switch (op)
        {
            case SetPart set:
                PlanSetPart(set);
                break;
            case InsertNode insert:
                PlanInsert(insert);
                break;
            case InsertAttribute insertAttribute:
                PlanInsertAttribute(insertAttribute);
                break;
            case DeleteNode delete:
                PlanDelete(delete);
                break;
            case DeleteAttribute deleteAttribute:
                PlanDeleteAttribute(deleteAttribute);
                break;
            case MoveNode move:
                PlanMove(move);
                break;
            case MoveAttribute moveAttribute:
                PlanMoveAttribute(moveAttribute);
                break;
            case DuplicateNode duplicate:
                PlanDuplicate(duplicate);
                break;
            case ChangeJsonType:
                throw new CfgeditException("XML nodes have no JSON type");
            default:
                throw new CfgeditException("This edit is not supported for XML");
        }

        return _edits;
    }

    // ----- Operations: setting parts -----

    private void PlanSetPart(SetPart op)
    {
        DocNode node = Node(op.Node);
        switch (op.Part)
        {
            case PartKind.ElementName:
                PlanRenameElement(node, op.Value);
                return;
            case PartKind.EndName when node.EndParts.Count > 0:
                PlanRenameElement(node, op.Value);
                return;
            case PartKind.AttributeName:
                PlanRenameAttribute(node, op.AttributeIndex, op.Value);
                return;
            case PartKind.AttributeValue:
                PlanSetAttributeValue(node, op.AttributeIndex, op.Value);
                return;
            case PartKind.Text:
                PlanSetText(node, op.Value);
                return;
            case PartKind.Comment when node.Kind == NodeKind.XmlComment:
                PlanSetComment(node, op.Value);
                return;
            case PartKind.PITarget when node.Kind == NodeKind.XmlProcessingInstruction:
                PlanSetPITarget(node, op.Value);
                return;
            case PartKind.PIData when node.Kind == NodeKind.XmlProcessingInstruction:
                PlanSetPIData(node, op.Value);
                return;
            default:
                throw Uneditable();
        }
    }

    private void PlanRenameElement(DocNode node, string name)
    {
        XmlElementSyntax element = ElementOf(node);
        if (name == element.Name)
        {
            return;
        }

        CheckName(name);
        RequirePrefix(name, ScopeOf(node));
        Replace(element.NameStart, element.NameStart + element.Name.Length, name);
        if (element.EndTag is { } endTag)
        {
            Replace(endTag.NameStart, endTag.NameStart + endTag.Name.Length, name);
        }
    }

    private void PlanRenameAttribute(DocNode node, int index, string name)
    {
        XmlElementSyntax element = ElementOf(node);
        XmlAttributeSyntax attribute = AttributeAt(element, index);
        if (name == attribute.Name)
        {
            return;
        }

        CheckName(name);
        if (element.Attributes.Any(other => !ReferenceEquals(other, attribute) && other.Name == name))
        {
            throw new CfgeditException($"an attribute named '{name}' already exists");
        }

        RequirePrefix(name, ScopeOf(node));
        if (attribute.Name.StartsWith(XmlnsDeclarationStart, StringComparison.Ordinal))
        {
            CheckPrefixUnused(element, attribute.Name[XmlnsDeclarationStart.Length..]);
        }

        Replace(attribute.Start, attribute.Start + attribute.Name.Length, name);
    }

    private void PlanSetAttributeValue(DocNode node, int index, string value)
    {
        XmlElementSyntax element = ElementOf(node);
        XmlAttributeSyntax attribute = AttributeAt(element, index);
        DocPart part = Part(node, PartKind.AttributeValue, index) ?? throw Uneditable();
        if (value == part.Value)
        {
            return;
        }

        if (part.IsRaw)
        {
            Replace(part.Start, part.End, value);
            return;
        }

        CheckChars(value);
        Replace(part.Start, part.End, XmlSyntax.EncodeAttributeValue(value, attribute.Quote));
    }

    private void PlanSetText(DocNode node, string value)
    {
        switch (node.Kind)
        {
            case NodeKind.XmlText:
                PlanSetTextNode(node, value);
                return;
            case NodeKind.XmlCData:
                PlanSetCData(node, value);
                return;
            case NodeKind.XmlElement:
                PlanSetElementText(node, value);
                return;
            default:
                throw Uneditable();
        }
    }

    private void PlanSetTextNode(DocNode node, string value)
    {
        DocPart part = Part(node, PartKind.Text) ?? throw Uneditable();
        if (value == part.Value)
        {
            return;
        }

        if (value.Length == 0)
        {
            PlanRemove(node);
            return;
        }

        if (IsXmlWhitespace(value))
        {
            throw new CfgeditException("text cannot be only whitespace; delete it instead");
        }

        if (part.IsRaw)
        {
            CheckRawText(value);
            Replace(part.Start, part.End, value);
            return;
        }

        CheckChars(value);
        Replace(part.Start, part.End, XmlSyntax.EncodeText(value, _newline));
    }

    private void PlanSetCData(DocNode node, string value)
    {
        DocPart part = Part(node, PartKind.Text) ?? throw Uneditable();
        if (value == part.Value)
        {
            return;
        }

        if (value.Length == 0)
        {
            PlanRemove(node);
            return;
        }

        CheckChars(value);
        Replace(part.Start, part.End, CDataContent(value));
    }

    private void PlanSetElementText(DocNode node, string value)
    {
        XmlElementSyntax element = ElementOf(node);
        DocPart? part = Part(node, PartKind.Text);
        if (part is null)
        {
            // An empty element has no text part. Giving it text makes it <a>text</a>.
            if (!element.IsEmpty)
            {
                throw Uneditable();
            }

            if (value.Length == 0)
            {
                return;
            }

            CheckElementText(value);
            ReplaceContent(node, XmlSyntax.EncodeText(value, _newline));
            return;
        }

        if (value == part.Value)
        {
            return;
        }

        if (part.IsRaw)
        {
            CheckRawText(value);
            CheckLineOnlyText(value);
            Replace(part.Start, part.End, value);
            return;
        }

        CheckElementText(value);
        Replace(part.Start, part.End, XmlSyntax.EncodeText(value, _newline));
    }

    private void PlanSetComment(DocNode node, string value)
    {
        DocPart part = Part(node, PartKind.Comment) ?? throw Uneditable();
        if (value == part.Value)
        {
            return;
        }

        CheckComment(value);
        Replace(part.Start, part.End, value);
    }

    private void PlanSetPITarget(DocNode node, string value)
    {
        DocPart part = Part(node, PartKind.PITarget) ?? throw Uneditable();
        if (value == part.Value)
        {
            return;
        }

        if (!XmlSyntax.IsValidName(value))
        {
            throw new CfgeditException($"'{value}' is not a valid processing instruction target");
        }

        if (string.Equals(value, XmlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new CfgeditException("a processing instruction target cannot be 'xml'");
        }

        Replace(part.Start, part.End, value);
    }

    private void PlanSetPIData(DocNode node, string value)
    {
        DocPart data = Part(node, PartKind.PIData) ?? throw Uneditable();
        if (value == data.Value)
        {
            return;
        }

        CheckChars(value);
        if (value.Contains("?>", StringComparison.Ordinal))
        {
            throw new CfgeditException("a processing instruction's data cannot contain ?>");
        }

        if (value.Contains('\r'))
        {
            throw new CfgeditException("a processing instruction's data cannot contain a carriage return");
        }

        if (value.Length > 0 && IsXmlSpace(value[0]))
        {
            throw new CfgeditException("a processing instruction's data cannot start with whitespace");
        }

        int targetEnd = (Part(node, PartKind.PITarget) ?? throw Uneditable()).End;
        if (value.Length == 0)
        {
            // Without data the target is followed by ?> directly.
            Replace(targetEnd, data.End, string.Empty);
            return;
        }

        // Data must be separated from the target by whitespace. A PI with no data has none, so one is added.
        string separator = data.Start == targetEnd ? " " : string.Empty;
        Replace(data.Start, data.End, separator + value);
    }

    // ----- Operations: inserting -----

    private void PlanInsert(InsertNode op)
    {
        DocNode parent = Node(op.Parent);
        if (parent.Kind is not (NodeKind.Document or NodeKind.XmlElement))
        {
            throw new CfgeditException("This node cannot have children");
        }

        int count = parent.Children.Count;
        if (op.Index < 0 || op.Index > count)
        {
            throw new CfgeditException($"Index {op.Index} is outside the children, 0 to {count}");
        }

        if (parent.Kind == NodeKind.Document)
        {
            PlanDocumentInsert(parent, op);
            return;
        }

        switch (op.Node)
        {
            case NewXmlText text:
                PlanInsertText(parent, op.Index, text);
                return;
            case NewXmlComment comment:
                InsertPiece(parent, op.Index, "<!--" + CommentText(comment.Text) + "-->", isText: false);
                return;
            case NewXmlElement element:
                InsertPiece(parent, op.Index, BuildElement(element, parent, op.Index), isText: false);
                return;
            default:
                throw new CfgeditException("This kind of node cannot be inserted into XML");
        }
    }

    /// <summary>At the document level only comments can go, and never before the XML declaration.</summary>
    private void PlanDocumentInsert(DocNode document, InsertNode op)
    {
        if (op.Node is not NewXmlComment comment)
        {
            throw new CfgeditException("Only comments can be placed at the document level");
        }

        if (op.Index == 0 && document.Children.Count > 0 && document.Children[0].Kind == NodeKind.XmlDeclaration)
        {
            throw new CfgeditException("a comment cannot come before the XML declaration");
        }

        string core = "<!--" + CommentText(comment.Text) + "-->";
        if (op.Index < document.Children.Count)
        {
            PlaceBefore(document, document.Children[op.Index], null, core);
        }
        else
        {
            PlaceAfter(document, document.Children[^1], core);
        }
    }

    private void PlanInsertText(DocNode parent, int index, NewXmlText text)
    {
        if (text.Text.Length == 0)
        {
            throw new CfgeditException("text cannot be empty");
        }

        CheckChars(text.Text);
        if (text.CData)
        {
            InsertPiece(parent, index, "<![CDATA[" + CDataContent(text.Text) + "]]>", isText: false);
            return;
        }

        if (IsXmlWhitespace(text.Text))
        {
            throw new CfgeditException("text cannot be only whitespace; it would be formatting, not a node");
        }

        InsertPiece(parent, index, XmlSyntax.EncodeText(text.Text, _newline), isText: true);
    }

    /// <summary>
    /// Inserts a piece of markup or text into an element at a child index. An element with no content takes it on a line of its
    /// own, indented one unit deeper. A text-only element takes it after its text. Otherwise it goes before the child at the
    /// index, or after the last child, by the layout rules.
    /// </summary>
    private void InsertPiece(DocNode parent, int index, string core, bool isText)
    {
        if (IsBlank(parent))
        {
            ReplaceContent(parent, isText ? core : Layout(parent, core));
            return;
        }

        if (parent.Children.Count == 0)
        {
            // Text-only content: the new piece goes after the text, which keeps it as the first child.
            int at = (Part(parent, PartKind.Text) ?? throw new InvalidOperationException("A text-only element has no text part.")).End;
            if (isText)
            {
                CheckTextSlot(parent, at);
            }

            Replace(at, at, core);
            return;
        }

        if (index < parent.Children.Count)
        {
            DocNode site = parent.Children[index];
            if (isText)
            {
                CheckTextSlot(parent, site.Start);
                Replace(site.Start, site.Start, core);
                return;
            }

            PlaceBefore(parent, site, index > 0 ? parent.Children[index - 1] : null, core);
            return;
        }

        DocNode anchor = parent.Children[^1];
        if (isText)
        {
            CheckTextSlot(parent, anchor.End);
            Replace(anchor.End, anchor.End, core);
            return;
        }

        PlaceAfter(parent, anchor, core);
    }

    /// <summary>
    /// The text of an element that has no content: the new content replaces the span between its start tag and its end tag, or
    /// for <c>&lt;a/&gt;</c> the rest of the start tag, which becomes <c>&lt;a&gt;</c> with the content and <c>&lt;/a&gt;</c>.
    /// </summary>
    private void ReplaceContent(DocNode element, string content)
    {
        XmlElementSyntax syntax = ElementOf(element);
        if (syntax.IsEmpty)
        {
            // The whitespace before "/>" goes with it, so the start tag reads <a>.
            int start = syntax.StartTagEnd - 2;
            while (start > element.Start && IsXmlSpace(_text[start - 1]))
            {
                start--;
            }

            Replace(start, syntax.End, ">" + content + "</" + syntax.Name + ">");
            return;
        }

        Replace(syntax.StartTagEnd, syntax.EndTag!.Start, content);
    }

    /// <summary>The content of a blank element written on lines of its own: one unit deeper than the element's own line.</summary>
    private string Layout(DocNode element, string core)
    {
        string closing = EditLayout.LineIndent(_text, element.Start);
        return _newline + closing + _unit + core + _newline + closing;
    }

    /// <summary>
    /// Builds the markup of a new element. The self-closing style is copied from a sibling, and the attribute quote is the
    /// document's majority quote.
    /// </summary>
    private string BuildElement(NewXmlElement element, DocNode parent, int index)
    {
        CheckName(element.Name);
        var scope = ScopeOf(parent);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, _) in element.Attributes)
        {
            if (name.StartsWith(XmlnsDeclarationStart, StringComparison.Ordinal))
            {
                scope.Add(name[XmlnsDeclarationStart.Length..]);
            }
        }

        RequirePrefix(element.Name, scope);
        char quote = DocumentQuote();
        var attributes = new StringBuilder();
        foreach ((string name, string value) in element.Attributes)
        {
            CheckName(name);
            if (!names.Add(name))
            {
                throw new CfgeditException($"an attribute named '{name}' already exists");
            }

            RequirePrefix(name, scope);
            CheckChars(value);
            attributes.Append(' ').Append(name).Append('=').Append(quote)
                .Append(XmlSyntax.EncodeAttributeValue(value, quote)).Append(quote);
        }

        if (string.IsNullOrEmpty(element.Text))
        {
            return "<" + element.Name + attributes + SelfClosingStyle(parent, index);
        }

        CheckElementText(element.Text);
        return "<" + element.Name + attributes + ">" + XmlSyntax.EncodeText(element.Text, _newline) + "</" + element.Name + ">";
    }

    // ----- Operations: deleting, moving, duplicating -----

    private void PlanDelete(DeleteNode op)
    {
        PlanRemove(Node(op.Node));
    }

    /// <summary>Removes a node with its attached comments.</summary>
    private void PlanRemove(DocNode node)
    {
        CheckRemovable(node, "deleted");
        DocNode parent = Parent(node);
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), _text);
        RemoveGroup(parent, first, last);
    }

    private void PlanMove(MoveNode op)
    {
        DocNode node = Node(op.Node);
        CheckRemovable(node, "moved");
        if (node.Kind == NodeKind.XmlText)
        {
            throw new CfgeditException("A text node cannot be moved");
        }

        DocNode parent = Parent(node);
        IReadOnlyList<DocNode> children = parent.Children;
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), _text);
        var rest = new List<DocNode>();
        for (int k = 0; k < children.Count; k++)
        {
            if (k < first || k > last)
            {
                rest.Add(children[k]);
            }
        }

        int target = Math.Clamp(op.NewIndex, 0, rest.Count);
        if (target == first)
        {
            return;
        }

        string core = _text[children[first].Start..children[last].End];
        RemoveGroup(parent, first, last);
        if (target < rest.Count)
        {
            PlaceBefore(parent, rest[target], target > 0 ? rest[target - 1] : null, core);
        }
        else
        {
            PlaceAfter(parent, rest[^1], core);
        }
    }

    private void PlanDuplicate(DuplicateNode op)
    {
        DocNode node = Node(op.Node);
        CheckRemovable(node, "duplicated");
        if (node.Kind == NodeKind.XmlText)
        {
            throw new CfgeditException("A text node cannot be duplicated");
        }

        DocNode parent = Parent(node);
        IReadOnlyList<DocNode> children = parent.Children;
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), _text);
        string core = _text[children[first].Start..children[last].End];
        PlaceAfter(parent, children[last], core);
    }

    /// <summary>
    /// Removes the children [first, last] with their layout. In mixed content only the span goes. Otherwise the group's whole
    /// lines go when it stands on lines of its own, else the text up to the next sibling, or from the previous sibling.
    /// </summary>
    private void RemoveGroup(DocNode parent, int first, int last)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        int start = children[first].Start;
        int end = children[last].End;
        CheckJoin(parent, start, end);
        if (parent.Kind != NodeKind.Document && IsMixed(parent))
        {
            Replace(start, end, string.Empty);
            return;
        }

        if (EditLayout.StartsLine(_text, start) && EditLayout.RestOfLineBlank(_text, end))
        {
            Replace(EditLayout.LineStart(_text, start), EditLayout.AfterLineBreak(_text, end), string.Empty);
            return;
        }

        if (last + 1 < children.Count)
        {
            Replace(start, children[last + 1].Start, string.Empty);
            return;
        }

        if (first > 0)
        {
            Replace(children[first - 1].End, end, string.Empty);
            return;
        }

        Replace(start, end, string.Empty);
    }

    /// <summary>
    /// Refuses a removal that would put two text runs next to each other. Whitespace between them is not a node, and the
    /// joined text would be a different node. Two whitespace runs join to whitespace, which is harmless.
    /// </summary>
    private void CheckJoin(DocNode parent, int start, int end)
    {
        if (parent.Kind == NodeKind.Document)
        {
            return;
        }

        IReadOnlyList<XmlNodeSyntax> nodes = ElementOf(parent).Children;
        int first = IndexWhere(nodes, n => n.Start == start);
        int last = IndexWhere(nodes, n => n.End == end);
        if (first < 0 || last < first)
        {
            throw new InvalidOperationException("The removed nodes are not among the children.");
        }

        if (first > 0 && last + 1 < nodes.Count
            && nodes[first - 1] is XmlTextSyntax before
            && nodes[last + 1] is XmlTextSyntax after
            && !(before.IsWhitespace && after.IsWhitespace))
        {
            throw new CfgeditException("removing this would join two pieces of text");
        }
    }

    private static int IndexWhere(IReadOnlyList<XmlNodeSyntax> nodes, Func<XmlNodeSyntax, bool> predicate)
    {
        for (int k = 0; k < nodes.Count; k++)
        {
            if (predicate(nodes[k]))
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>
    /// Refuses an insertion of text next to other text. A text node placed there would merge with it, so the text
    /// would be a different node.
    /// </summary>
    private void CheckTextSlot(DocNode parent, int offset)
    {
        foreach (XmlNodeSyntax child in ElementOf(parent).Children)
        {
            if (child is XmlTextSyntax && (child.End == offset || child.Start == offset))
            {
                throw new CfgeditException("text cannot sit next to other text");
            }
        }
    }

    private void CheckRemovable(DocNode node, string verb)
    {
        DocNode parent = Parent(node);
        if (node.Kind == NodeKind.XmlElement && parent.Kind == NodeKind.Document)
        {
            throw new CfgeditException($"The root element cannot be {verb}");
        }

        if (node.Kind is NodeKind.XmlDeclaration or NodeKind.XmlDoctype)
        {
            throw new CfgeditException($"This node cannot be {verb}");
        }
    }

    // ----- Operations: attributes -----

    private void PlanInsertAttribute(InsertAttribute op)
    {
        DocNode element = Node(op.Element);
        XmlElementSyntax syntax = ElementForAttributes(element);
        int count = syntax.Attributes.Count;
        if (op.Index < 0 || op.Index > count)
        {
            throw new CfgeditException($"Index {op.Index} is outside the attributes, 0 to {count}");
        }

        CheckName(op.Name);
        if (syntax.Attributes.Any(attribute => attribute.Name == op.Name))
        {
            throw new CfgeditException($"an attribute named '{op.Name}' already exists");
        }

        RequirePrefix(op.Name, ScopeOf(element));
        CheckChars(op.Value);
        char quote = count > 0 ? syntax.Attributes[0].Quote : DocumentQuote();
        string item = op.Name + "=" + quote + XmlSyntax.EncodeAttributeValue(op.Value, quote) + quote;

        if (count == 0)
        {
            int nameEnd = syntax.NameStart + syntax.Name.Length;
            Replace(nameEnd, nameEnd, " " + item);
            return;
        }

        if (op.Index < count)
        {
            // The new attribute takes the gap that the attribute at the index had, so both keep their layout.
            int gapStart = GapStart(syntax, op.Index);
            string gap = _text[gapStart..syntax.Attributes[op.Index].Start];
            Replace(gapStart, gapStart, gap + item);
            return;
        }

        XmlAttributeSyntax last = syntax.Attributes[^1];
        string separator = IsAttributeLayoutMultiline(syntax) ? _newline + Alignment(last) : " ";
        Replace(last.End, last.End, separator + item);
    }

    private void PlanDeleteAttribute(DeleteAttribute op)
    {
        DocNode element = Node(op.Element);
        XmlElementSyntax syntax = ElementForAttributes(element);
        XmlAttributeSyntax attribute = AttributeAt(syntax, op.Index);
        if (attribute.Name.StartsWith(XmlnsDeclarationStart, StringComparison.Ordinal))
        {
            CheckPrefixUnused(syntax, attribute.Name[XmlnsDeclarationStart.Length..]);
        }

        if (EditLayout.StartsLine(_text, attribute.Start) && EditLayout.RestOfLineBlank(_text, attribute.End))
        {
            // On a line of its own, the whole line goes.
            Replace(EditLayout.LineStart(_text, attribute.Start), EditLayout.AfterLineBreak(_text, attribute.End), string.Empty);
            return;
        }

        Replace(GapStart(syntax, op.Index), attribute.End, string.Empty);
    }

    /// <summary>
    /// Swaps the attributes' text and keeps each position's leading whitespace, so the layout does not change.
    /// </summary>
    private void PlanMoveAttribute(MoveAttribute op)
    {
        DocNode element = Node(op.Element);
        XmlElementSyntax syntax = ElementForAttributes(element);
        int count = syntax.Attributes.Count;
        AttributeAt(syntax, op.Index);
        int target = Math.Clamp(op.NewIndex, 0, count - 1);
        if (target == op.Index)
        {
            return;
        }

        var order = Enumerable.Range(0, count).Where(k => k != op.Index).ToList();
        order.Insert(target, op.Index);
        var rebuilt = new StringBuilder();
        for (int k = 0; k < count; k++)
        {
            int gapStart = GapStart(syntax, k);
            XmlAttributeSyntax moved = syntax.Attributes[order[k]];
            rebuilt.Append(_text, gapStart, syntax.Attributes[k].Start - gapStart);
            rebuilt.Append(_text, moved.Start, moved.End - moved.Start);
        }

        Replace(syntax.NameStart + syntax.Name.Length, syntax.Attributes[^1].End, rebuilt.ToString());
    }

    // ----- Placement -----

    /// <summary>
    /// Places a piece before a child. Where the child starts a line in an element with element content, the piece goes on a
    /// line of its own, indented as its neighbours are. Otherwise it goes straight before the child, with no added whitespace.
    /// </summary>
    private void PlaceBefore(DocNode parent, DocNode site, DocNode? previous, string core)
    {
        if (parent.Kind == NodeKind.Document)
        {
            if (EditLayout.StartsLine(_text, site.Start))
            {
                int lineStart = EditLayout.LineStart(_text, site.Start);
                Replace(lineStart, lineStart, EditLayout.LineIndent(_text, site.Start) + core + _newline);
            }
            else
            {
                Replace(site.Start, site.Start, core + " ");
            }

            return;
        }

        if (!IsMixed(parent) && EditLayout.StartsLine(_text, site.Start))
        {
            int lineStart = EditLayout.LineStart(_text, site.Start);
            Replace(lineStart, lineStart, IndentFor(previous, site, parent) + core + _newline);
            return;
        }

        Replace(site.Start, site.Start, core);
    }

    /// <summary>
    /// Places a piece after a child, which is the end of its group. In element content that has line breaks, the piece starts a
    /// new line. Otherwise it goes straight after, with no added whitespace.
    /// </summary>
    private void PlaceAfter(DocNode parent, DocNode anchor, string core)
    {
        int at = anchor.End;
        if (parent.Kind == NodeKind.Document)
        {
            Replace(at, at, _newline + core);
            return;
        }

        if (!IsMixed(parent) && HasLineLayout(parent))
        {
            Replace(at, at, _newline + IndentFor(anchor, null, parent) + core);
            return;
        }

        Replace(at, at, core);
    }

    /// <summary>The indentation of a new line for a child: that of the previous sibling, else the next, else one unit deeper than the parent.</summary>
    private string IndentFor(DocNode? previous, DocNode? next, DocNode parent)
    {
        if (previous is not null)
        {
            return EditLayout.LineIndent(_text, previous.Start);
        }

        if (next is not null)
        {
            return EditLayout.LineIndent(_text, next.Start);
        }

        return EditLayout.LineIndent(_text, parent.Start) + _unit;
    }

    /// <summary>True when the element's content has a line break between its start tag and its end tag.</summary>
    private bool HasLineLayout(DocNode element)
    {
        XmlElementSyntax syntax = ElementOf(element);
        int end = syntax.EndTag?.Start ?? syntax.StartTagEnd;
        return EditLayout.HasLineBreak(_text, syntax.StartTagEnd, end);
    }

    /// <summary>True when the element has non-whitespace text or CDATA among its children.</summary>
    private static bool IsMixed(DocNode parent)
    {
        return parent.Children.Any(child => child.Kind is NodeKind.XmlText or NodeKind.XmlCData);
    }

    /// <summary>True when the element has no child nodes, and its text (if any) is only whitespace.</summary>
    private bool IsBlank(DocNode element)
    {
        if (element.Children.Count > 0)
        {
            return false;
        }

        DocPart? text = Part(element, PartKind.Text);
        return text is null || IsXmlWhitespace(text.Value);
    }

    /// <summary>The self-closing style of the nearest empty sibling: <c>&lt;x /&gt;</c> or <c>&lt;x/&gt;</c>. The default is <c>&lt;x /&gt;</c>.</summary>
    private string SelfClosingStyle(DocNode parent, int index)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        int count = children.Count;
        for (int k = Math.Min(index, count) - 1; k >= 0; k--)
        {
            if (EmptySiblingStyle(children[k]) is { } style)
            {
                return style;
            }
        }

        for (int k = Math.Min(index, count); k < count; k++)
        {
            if (EmptySiblingStyle(children[k]) is { } style)
            {
                return style;
            }
        }

        return " />";
    }

    private string? EmptySiblingStyle(DocNode node)
    {
        if (node.Kind != NodeKind.XmlElement)
        {
            return null;
        }

        XmlElementSyntax syntax = _elements[node.Start];
        if (!syntax.IsEmpty)
        {
            return null;
        }

        return IsXmlSpace(_text[syntax.StartTagEnd - 3]) ? " />" : "/>";
    }

    /// <summary>
    /// True when the whitespace before any attribute holds a line break. The attributes are then written one to a line,
    /// aligned with each other.
    /// </summary>
    private bool IsAttributeLayoutMultiline(XmlElementSyntax element)
    {
        for (int k = 0; k < element.Attributes.Count; k++)
        {
            if (EditLayout.HasLineBreak(_text, GapStart(element, k), element.Attributes[k].Start))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The whitespace that puts an attribute in the same column as the attribute: its line's indentation, or spaces.</summary>
    private string Alignment(XmlAttributeSyntax attribute)
    {
        if (EditLayout.StartsLine(_text, attribute.Start))
        {
            return EditLayout.LineIndent(_text, attribute.Start);
        }

        return new string(' ', attribute.Start - EditLayout.LineStart(_text, attribute.Start));
    }

    /// <summary>The attribute quote for a new value: the quote of the element's first attribute, else the document's majority, else <c>"</c>.</summary>
    private char DocumentQuote()
    {
        _documentQuote ??= ComputeDocumentQuote();
        return _documentQuote.Value;
    }

    private char ComputeDocumentQuote()
    {
        int doubles = 0;
        int singles = 0;
        foreach (XmlElementSyntax element in _elements.Values)
        {
            foreach (XmlAttributeSyntax attribute in element.Attributes)
            {
                if (attribute.Quote == '"')
                {
                    doubles++;
                }
                else
                {
                    singles++;
                }
            }
        }

        return singles > doubles ? '\'' : '"';
    }

    // ----- Names and scope -----

    /// <summary>The prefixes that are in scope at a node: those declared on it and its ancestors, and <c>xml</c>, which is predeclared.</summary>
    private HashSet<string> ScopeOf(DocNode node)
    {
        var scope = new HashSet<string>(StringComparer.Ordinal) { XmlPrefix };
        for (DocNode? current = node; current is not null && current.Kind == NodeKind.XmlElement; current = current.Parent)
        {
            foreach (XmlAttributeSyntax attribute in _elements[current.Start].Attributes)
            {
                if (attribute.Name.StartsWith(XmlnsDeclarationStart, StringComparison.Ordinal))
                {
                    scope.Add(attribute.Name[XmlnsDeclarationStart.Length..]);
                }
            }
        }

        return scope;
    }

    private static void RequirePrefix(string name, ISet<string> scope)
    {
        int colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return;
        }

        string prefix = name[..colon];
        if (prefix == XmlnsPrefix || scope.Contains(prefix))
        {
            return;
        }

        throw new CfgeditException($"the namespace prefix '{prefix}' is not declared");
    }

    private static void CheckName(string name)
    {
        if (!XmlSyntax.IsValidQName(name))
        {
            throw new CfgeditException($"'{name}' is not a valid XML name");
        }
    }

    /// <summary>
    /// Refuses the removal or renaming of a <c>xmlns:p</c> declaration while <c>p</c> is used in its scope: by the element, a
    /// descendant, or an attribute on them. A descendant that declares <c>p</c> again has its own scope and is not checked.
    /// </summary>
    private void CheckPrefixUnused(XmlElementSyntax declaring, string prefix)
    {
        if (IsPrefixUsed(declaring, prefix))
        {
            throw new CfgeditException($"the prefix '{prefix}' is still used in its scope");
        }
    }

    private static bool IsPrefixUsed(XmlElementSyntax element, string prefix)
    {
        if (PrefixOf(element.Name) == prefix || element.Attributes.Any(attribute => PrefixOf(attribute.Name) == prefix))
        {
            return true;
        }

        foreach (XmlNodeSyntax child in element.Children)
        {
            if (child is XmlElementSyntax nested
                && !nested.Attributes.Any(attribute => attribute.Name == XmlnsDeclarationStart + prefix)
                && IsPrefixUsed(nested, prefix))
            {
                return true;
            }
        }

        return false;
    }

    private static string? PrefixOf(string name)
    {
        int colon = name.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? null : name[..colon];
    }

    // ----- Values -----

    /// <summary>Refuses a character XML 1.0 cannot hold.</summary>
    private static void CheckChars(string value)
    {
        int index = XmlSyntax.FindInvalidChar(value);
        if (index >= 0)
        {
            throw new CfgeditException($"the value contains U+{(int)value[index]:X4}, which XML 1.0 cannot hold");
        }
    }

    /// <summary>The content of a comment, which must be a comment that XML can hold.</summary>
    private static string CommentText(string value)
    {
        CheckComment(value);
        return value;
    }

    private static void CheckComment(string value)
    {
        CheckChars(value);
        if (value.Contains('\r'))
        {
            throw new CfgeditException("a comment cannot contain a carriage return");
        }

        if (XmlSyntax.EncodeComment(value) is null)
        {
            throw new CfgeditException("a comment cannot contain -- or end with -");
        }
    }

    /// <summary>The content of CDATA sections that hold the value.</summary>
    private string CDataContent(string value)
    {
        return XmlSyntax.EncodeCData(value, _newline)
            ?? throw new CfgeditException("CDATA cannot hold a carriage return");
    }

    /// <summary>Checks the text of an element with content: a character XML can hold, and not only line breaks.</summary>
    private static void CheckElementText(string value)
    {
        CheckChars(value);
        CheckLineOnlyText(value);
    }

    /// <summary>
    /// Refuses a raw text value with a <c>&lt;</c>. Source text that is valid XML and holds markup changes the tree, and the
    /// wildcard of the logical check cannot see that, so the refusal is made here.
    /// </summary>
    private static void CheckRawText(string value)
    {
        if (value.Contains('<'))
        {
            throw new CfgeditException("text cannot contain '<'; markup is not text");
        }
    }

    /// <summary>
    /// Refuses text that is only whitespace with a line break. Such an element would read as an empty container, not as a text
    /// value, so the logical check could not match it.
    /// </summary>
    private static void CheckLineOnlyText(string value)
    {
        if (IsXmlWhitespace(value) && value.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new CfgeditException("text cannot be only whitespace with a line break, which would make the element a container");
        }
    }

    private static bool IsXmlWhitespace(string value)
    {
        return value.All(IsXmlSpace);
    }

    private static bool IsXmlSpace(char c)
    {
        return c is ' ' or '\t' or '\r' or '\n';
    }

    // ----- Tree and text helpers -----

    private void IndexElements(XmlElementSyntax element)
    {
        _elements[element.Start] = element;
        foreach (XmlNodeSyntax child in element.Children)
        {
            if (child is XmlElementSyntax nested)
            {
                IndexElements(nested);
            }
        }
    }

    private XmlElementSyntax ElementOf(DocNode node)
    {
        if (node.Kind != NodeKind.XmlElement)
        {
            throw Uneditable();
        }

        return _elements[node.Start];
    }

    private XmlElementSyntax ElementForAttributes(DocNode node)
    {
        if (node.Kind != NodeKind.XmlElement)
        {
            throw new CfgeditException("Only an element has attributes");
        }

        return _elements[node.Start];
    }

    private static XmlAttributeSyntax AttributeAt(XmlElementSyntax element, int index)
    {
        if (index < 0 || index >= element.Attributes.Count)
        {
            throw new CfgeditException($"There is no attribute at index {index}");
        }

        return element.Attributes[index];
    }

    /// <summary>The offset where an attribute's gap starts: the element name's end, or the previous attribute's end.</summary>
    private static int GapStart(XmlElementSyntax element, int index)
    {
        return index == 0 ? element.NameStart + element.Name.Length : element.Attributes[index - 1].End;
    }

    private DocNode Node(NodePath path)
    {
        return _document.Resolve(path) ?? throw new CfgeditException($"There is no node at {path}");
    }

    private static DocNode Parent(DocNode node)
    {
        return node.Parent ?? throw new CfgeditException("the document node cannot be edited this way");
    }

    private static int IndexOf(DocNode parent, DocNode child)
    {
        for (int k = 0; k < parent.Children.Count; k++)
        {
            if (ReferenceEquals(parent.Children[k], child))
            {
                return k;
            }
        }

        throw new CfgeditException("The node is not a child of its parent");
    }

    private static DocPart? Part(DocNode node, PartKind kind, int attributeIndex = -1)
    {
        foreach (DocPart part in node.Parts)
        {
            if (part.Kind == kind && part.AttributeIndex == attributeIndex)
            {
                return part;
            }
        }

        return null;
    }

    private static CfgeditException Uneditable()
    {
        return new CfgeditException("this part cannot be edited");
    }

    private void Replace(int start, int end, string text)
    {
        _edits.Add(new TextEdit(start, end, text));
    }
}
