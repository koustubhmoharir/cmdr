using System.Xml;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Xml;

/// <summary>The kinds of node that the cross-check compares, shared by the reader's nodes and the CST's.</summary>
internal enum FlatKind
{
    XmlDeclaration,
    DocumentType,
    Element,
    EndElement,
    Text,
    CData,
    Comment,
    ProcessingInstruction,
}

/// <summary>
/// A node in document order, as the CST gives it. <see cref="Offset"/> is the position the reader reports for this kind of
/// node, and <see cref="Value"/> is null when the value is not compared. For an element, <see cref="Element"/> is the element
/// itself, whose attributes are compared with the reader's; for every other kind it is null.
/// </summary>
internal readonly record struct FlatNode(
    FlatKind Kind,
    string? Name,
    int Offset,
    string? Value,
    bool IsEmpty,
    XmlElementSyntax? Element);

/// <summary>
/// Compares the reader's nodes with the CST, node by node in document order, and rejects files whose entities contain markup.
/// </summary>
internal static class XmlCrossCheck
{
    /// <summary>The error for a disagreement between the reader and the scanner. It is cfgedit's bug, not the file's.</summary>
    internal static CfgeditException Mismatch(string what, LinePosition? at)
    {
        return new CfgeditException($"internal parse mismatch: {what} (please report this)")
        {
            Line = at?.Line,
            Column = at?.Column,
        };
    }

    /// <summary>
    /// The CST's nodes in document order, produced lazily. Elements are followed by their children and end tag, using an
    /// explicit stack as the scanner does.
    /// </summary>
    private static IEnumerable<FlatNode> Walk(XmlDocumentSyntax document)
    {
        if (document.Declaration is { } declaration)
        {
            yield return new FlatNode(FlatKind.XmlDeclaration, "xml", declaration.Start + 2, null, false, null);
        }

        foreach (XmlNodeSyntax node in document.Prolog)
        {
            yield return Leaf(node);
        }

        XmlElementSyntax root = document.Root;
        yield return ElementNode(root);
        var stack = new Stack<(XmlElementSyntax Element, int Next)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            (XmlElementSyntax element, int next) = stack.Pop();
            if (next < element.Children.Count)
            {
                stack.Push((element, next + 1));
                XmlNodeSyntax child = element.Children[next];
                if (child is XmlElementSyntax childElement)
                {
                    yield return ElementNode(childElement);
                    stack.Push((childElement, 0));
                }
                else
                {
                    yield return Leaf(child);
                }
            }
            else if (element.EndTag is { } endTag)
            {
                yield return new FlatNode(FlatKind.EndElement, endTag.Name, endTag.NameStart, null, false, null);
            }
        }

        foreach (XmlNodeSyntax node in document.Epilog)
        {
            yield return Leaf(node);
        }
    }

    private static FlatNode ElementNode(XmlElementSyntax element)
    {
        return new FlatNode(FlatKind.Element, element.Name, element.NameStart, null, element.IsEmpty, element);
    }

    private static FlatNode Leaf(XmlNodeSyntax node)
    {
        return node switch
        {
            XmlDoctypeSyntax doctype => new FlatNode(FlatKind.DocumentType, doctype.Name, doctype.NameStart, null, false, null),
            XmlCommentSyntax comment => new FlatNode(FlatKind.Comment, null, comment.Start + 4, comment.Content, false, null),
            XmlProcessingInstructionSyntax pi => new FlatNode(FlatKind.ProcessingInstruction, pi.Target, pi.Start + 2, pi.Data, false, null),
            XmlTextSyntax text => new FlatNode(FlatKind.Text, null, text.Start, text.IsRaw ? null : text.Value, false, null),
            XmlCDataSyntax cdata => new FlatNode(FlatKind.CData, null, cdata.Start + 9, cdata.Content, false, null),
            _ => throw new InvalidOperationException($"A {node.GetType().Name} is not a leaf node."),
        };
    }

    private static FlatKind? ToFlatKind(XmlNodeType kind)
    {
        return kind switch
        {
            XmlNodeType.XmlDeclaration => FlatKind.XmlDeclaration,
            XmlNodeType.DocumentType => FlatKind.DocumentType,
            XmlNodeType.Element => FlatKind.Element,
            XmlNodeType.EndElement => FlatKind.EndElement,
            XmlNodeType.Text or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace => FlatKind.Text,
            XmlNodeType.CDATA => FlatKind.CData,
            XmlNodeType.Comment => FlatKind.Comment,
            XmlNodeType.ProcessingInstruction => FlatKind.ProcessingInstruction,
            _ => null,
        };
    }

    private static string Describe(FlatNode node)
    {
        return node.Kind switch
        {
            FlatKind.XmlDeclaration => "the XML declaration",
            FlatKind.DocumentType => $"the document type '{node.Name}'",
            FlatKind.Element => $"element '{node.Name}'",
            FlatKind.EndElement => $"end tag '{node.Name}'",
            FlatKind.Text => "a text run",
            FlatKind.CData => "a CDATA section",
            FlatKind.Comment => "a comment",
            _ => $"processing instruction '{node.Name}'",
        };
    }

    private static string Where(LinePosition position) => $"line {position.Line}, column {position.Column}";

    private static int Compare(LinePosition a, LinePosition b)
    {
        return a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column);
    }

    /// <summary>
    /// The cross-check, driven by the reader. The reader calls <see cref="Node"/> for each node in document order, then
    /// <see cref="ThrowFirstProblem"/>. Nothing is thrown while the reader runs: the first disagreement is kept, so that a
    /// well-formedness error, which takes precedence, is still the one reported.
    /// </summary>
    /// <remarks>
    /// The CST is walked lazily, in the order the reader reports nodes, so no second list of nodes is built. The reader's
    /// positions are compared with the CST's offsets converted by a <see cref="LineCursor"/>, which moves forward through the
    /// text as the walk does.
    /// </remarks>
    internal sealed class ReaderComparison
    {
        private readonly SourceText _source;
        private readonly XmlScanResult? _scan;
        private readonly int _doctypeOffset;
        private readonly bool _hasDoctype;
        private readonly LinePosition _doctypeStart;
        private readonly LinePosition _doctypeEnd;
        private readonly IEnumerator<FlatNode>? _walk;
        private readonly int? _perturbedIndex;
        private readonly LineCursor _lines;
        private int _walkIndex;
        private bool _seenDoctype;
        private bool _markupViolation;
        private CfgeditException? _problem;

        /// <param name="source">The decoded text.</param>
        /// <param name="document">The tree to compare with, or null when the scanner failed. Then nothing is compared.</param>
        /// <param name="scan">The scanner's result, or null when it failed.</param>
        /// <param name="perturbedIndex">The test hook of <see cref="XmlDocumentSyntax"/>: the CST node to move, or null.</param>
        public ReaderComparison(SourceText source, XmlDocumentSyntax? document, XmlScanResult? scan, int? perturbedIndex)
        {
            _source = source;
            _scan = scan;
            _walk = document is null ? null : Walk(document).GetEnumerator();
            _perturbedIndex = perturbedIndex;
            _lines = new LineCursor(source.Text);
            if (scan?.Doctype is { } doctype)
            {
                _hasDoctype = true;
                _doctypeOffset = doctype.Start;
                _doctypeStart = source.GetPosition(doctype.Start);
                _doctypeEnd = source.GetPosition(doctype.End);
            }
        }

        /// <summary>Compares the next node that the reader reports. Nodes arrive in document order.</summary>
        /// <param name="attributes">The element's attributes. The list is reused by the reader, so it is not kept.</param>
        public void Node(XmlNodeType kind, string name, string value, LinePosition position, bool isEmpty, List<ReaderAttribute> attributes)
        {
            if (_hasDoctype)
            {
                TrackMarkupEntities(kind, position);
            }

            if (_problem is not null || _walk is null)
            {
                return;
            }

            if (!_walk.MoveNext())
            {
                _problem = Mismatch($"the reader has a {kind} node that the scanner does not", position);
                return;
            }

            FlatNode expected = Perturbed(_walk.Current, _walkIndex);
            _walkIndex++;
            _problem = CompareNode(expected, kind, name, value, position, isEmpty, attributes);
        }

        /// <summary>
        /// Throws the first problem, in the order the pipeline reports them: an entity with markup, then the first disagreement,
        /// then a node that the scanner has and the reader does not. It returns when there is none. Call it once the reader has
        /// read the whole document.
        /// </summary>
        public void ThrowFirstProblem()
        {
            if (_markupViolation && _scan is not null)
            {
                throw MarkupEntityError(_scan);
            }

            if (_problem is not null)
            {
                throw _problem;
            }

            if (_walk is not null && _walk.MoveNext())
            {
                FlatNode missing = Perturbed(_walk.Current, _walkIndex);
                throw Mismatch($"the scanner has {Describe(missing)} that the reader does not", _lines.At(missing.Offset));
            }
        }

        /// <summary>
        /// Rejects a document whose entity expansion put markup into the document. The reader then reports nodes positioned inside
        /// the DOCTYPE, after the DOCTYPE node itself. The error is at the first reference to such an entity, or at the DOCTYPE when
        /// no reference is found.
        /// </summary>
        private void TrackMarkupEntities(XmlNodeType kind, LinePosition position)
        {
            if (!_seenDoctype)
            {
                // The first DOCTYPE node starts the range. The nodes after it are the ones that can fall inside the DOCTYPE.
                _seenDoctype = kind == XmlNodeType.DocumentType;
                return;
            }

            if (!_markupViolation && Compare(position, _doctypeStart) >= 0 && Compare(position, _doctypeEnd) < 0)
            {
                _markupViolation = true;
            }
        }

        private CfgeditException MarkupEntityError(XmlScanResult scan)
        {
            int offset = _doctypeOffset;
            foreach ((string name, int referenceOffset) in scan.EntityReferences)
            {
                if (scan.MarkupEntities.Contains(name))
                {
                    offset = referenceOffset;
                    break;
                }
            }

            LinePosition at = _source.GetPosition(offset);
            return new CfgeditException("entities that contain markup are not supported")
            {
                Line = at.Line,
                Column = at.Column,
            };
        }

        private FlatNode Perturbed(FlatNode node, int index)
        {
            return _perturbedIndex == index ? node with { Offset = node.Offset + 1 } : node;
        }

        /// <summary>Returns the first disagreement between a CST node and a reader node, or null when they agree.</summary>
        private CfgeditException? CompareNode(
            FlatNode expected, XmlNodeType kind, string name, string value, LinePosition position, bool isEmpty, List<ReaderAttribute> attributes)
        {
            LinePosition at = _lines.At(expected.Offset);
            FlatKind? flat = ToFlatKind(kind);
            if (flat is null)
            {
                return Mismatch($"the reader reports an unexpected {kind} node", position);
            }

            if (flat != expected.Kind)
            {
                return Mismatch($"the scanner has {Describe(expected)} where the reader has {kind}", at);
            }

            if (position != at)
            {
                return Mismatch(
                    $"{Describe(expected)} is at {Where(at)} in the scanner but {Where(position)} in the reader",
                    at);
            }

            if (expected.Name is not null && expected.Name != name)
            {
                return Mismatch(
                    $"{Describe(expected)} is named '{expected.Name}' in the scanner but '{name}' in the reader",
                    at);
            }

            if (expected.Value is not null && expected.Value != value)
            {
                return Mismatch($"{Describe(expected)} has a different value in the scanner and the reader", at);
            }

            if (expected.Kind != FlatKind.Element)
            {
                return null;
            }

            if (expected.IsEmpty != isEmpty)
            {
                return Mismatch($"{Describe(expected)} is written differently in the scanner and the reader", at);
            }

            return CompareAttributes(expected, attributes, at);
        }

        /// <summary>
        /// Compares the attributes of an element. The reader's attributes that a DTD supplies are not in the source, so they are
        /// skipped; every other attribute is compared in name, position, quote and, unless it is a raw-mode value, value.
        /// </summary>
        private CfgeditException? CompareAttributes(FlatNode element, List<ReaderAttribute> attributes, LinePosition at)
        {
            IReadOnlyList<XmlAttributeSyntax> expectedAttributes = element.Element!.Attributes;
            int reportedCount = 0;
            foreach (ReaderAttribute attribute in attributes)
            {
                if (!attribute.IsDefault)
                {
                    reportedCount++;
                }
            }

            if (reportedCount != expectedAttributes.Count)
            {
                return Mismatch(
                    $"{Describe(element)} has {expectedAttributes.Count} attributes in the scanner but {reportedCount} in the reader",
                    at);
            }

            int j = 0;
            foreach (ReaderAttribute attribute in attributes)
            {
                if (attribute.IsDefault)
                {
                    continue;
                }

                XmlAttributeSyntax expected = expectedAttributes[j];
                LinePosition attributeAt = _lines.At(expected.Start);
                if (attribute.Name != expected.Name)
                {
                    return Mismatch(
                        $"attribute {j + 1} of {Describe(element)} is '{expected.Name}' in the scanner but '{attribute.Name}' in the reader",
                        attributeAt);
                }

                if (attribute.Position != attributeAt)
                {
                    return Mismatch(
                        $"attribute '{expected.Name}' is at {Where(attributeAt)} in the scanner but {Where(attribute.Position)} in the reader",
                        attributeAt);
                }

                if (attribute.Quote != expected.Quote)
                {
                    return Mismatch(
                        $"attribute '{expected.Name}' is quoted with {expected.Quote} in the scanner but {attribute.Quote} in the reader",
                        attributeAt);
                }

                if (!expected.IsRaw && attribute.Value != expected.Value)
                {
                    return Mismatch($"attribute '{expected.Name}' has a different value in the scanner and the reader", attributeAt);
                }

                j++;
            }

            return null;
        }
    }

    /// <summary>
    /// The line and column of offsets, with the same answers as <see cref="SourceText.GetPosition"/>. The cross-check asks in
    /// document order, so the cursor moves forward through the text and the whole comparison is one pass. A line break is
    /// LF, CR, or CRLF counted once, as in SourceText. An offset before the last one asked for starts the count again.
    /// </summary>
    private sealed class LineCursor
    {
        private readonly string _text;
        private int _scan;
        private int _line = 1;
        private int _lineStart;

        public LineCursor(string text)
        {
            _text = text;
        }

        public LinePosition At(int offset)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            int target = Math.Min(offset, _text.Length);
            if (target < _scan)
            {
                _scan = 0;
                _line = 1;
                _lineStart = 0;
            }

            while (_scan < target)
            {
                // Only CR and LF can start a line, so the search skips the ordinary characters in between.
                int relative = _text.AsSpan(_scan, target - _scan).IndexOfAny('\r', '\n');
                if (relative < 0)
                {
                    _scan = target;
                    break;
                }

                int at = _scan + relative;
                char c = _text[at];
                if (c == '\n' || (c == '\r' && (at + 1 == _text.Length || _text[at + 1] != '\n')))
                {
                    _line++;
                    _lineStart = at + 1;
                }

                _scan = at + 1;
            }

            return new LinePosition(_line, target - _lineStart + 1);
        }
    }
}
