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

/// <summary>An attribute of a flat node. <see cref="Value"/> is null for a raw-mode value, which is compared by position only.</summary>
internal sealed record FlatAttribute(string Name, int Offset, char Quote, string? Value);

/// <summary>
/// A node in document order, as the CST gives it. <see cref="Offset"/> is the position the reader reports for this kind of
/// node, and <see cref="Value"/> is null when the value is not compared.
/// </summary>
internal sealed record FlatNode(
    FlatKind Kind,
    string? Name,
    int Offset,
    string? Value,
    bool IsEmpty,
    IReadOnlyList<FlatAttribute> Attributes);

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
    /// Rejects a document whose entity expansion put markup into the document. The reader then reports nodes positioned inside
    /// the DOCTYPE, after the DOCTYPE node itself. The error is at the first reference to such an entity, or at the DOCTYPE when
    /// no reference is found.
    /// </summary>
    internal static void RejectMarkupEntities(SourceText source, XmlScanResult scan, IReadOnlyList<ReaderNode> nodes)
    {
        XmlDoctypeSyntax? doctype = scan.Doctype;
        if (doctype is null)
        {
            return;
        }

        int doctypeIndex = -1;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Kind == XmlNodeType.DocumentType)
            {
                doctypeIndex = i;
                break;
            }
        }

        if (doctypeIndex < 0)
        {
            return;
        }

        LinePosition from = source.GetPosition(doctype.Start);
        LinePosition to = source.GetPosition(doctype.End);
        for (int i = doctypeIndex + 1; i < nodes.Count; i++)
        {
            LinePosition position = nodes[i].Position;
            if (Compare(position, from) < 0 || Compare(position, to) >= 0)
            {
                continue;
            }

            int offset = doctype.Start;
            foreach ((string name, int referenceOffset) in scan.EntityReferences)
            {
                if (scan.MarkupEntities.Contains(name))
                {
                    offset = referenceOffset;
                    break;
                }
            }

            LinePosition at = source.GetPosition(offset);
            throw new CfgeditException("entities that contain markup are not supported")
            {
                Line = at.Line,
                Column = at.Column,
            };
        }
    }

    /// <summary>
    /// Compares the CST with the reader's nodes. <paramref name="perturbedIndex"/> is a test hook: it moves the offset of one
    /// CST node by one character, so that the mismatch path can be exercised. It is null in every real parse.
    /// </summary>
    internal static void Verify(SourceText source, XmlDocumentSyntax document, IReadOnlyList<ReaderNode> nodes, int? perturbedIndex)
    {
        List<FlatNode> expected = Flatten(document);
        if (perturbedIndex is int index && (uint)index < (uint)expected.Count)
        {
            expected[index] = expected[index] with { Offset = expected[index].Offset + 1 };
        }

        int common = Math.Min(nodes.Count, expected.Count);
        for (int i = 0; i < common; i++)
        {
            CompareNode(source, expected[i], nodes[i]);
        }

        if (nodes.Count > expected.Count)
        {
            ReaderNode extra = nodes[expected.Count];
            throw Mismatch($"the reader has a {extra.Kind} node that the scanner does not", extra.Position);
        }

        if (expected.Count > nodes.Count)
        {
            FlatNode missing = expected[nodes.Count];
            throw Mismatch(
                $"the scanner has {Describe(missing)} that the reader does not",
                source.GetPosition(missing.Offset));
        }
    }

    private static void CompareNode(SourceText source, FlatNode expected, ReaderNode actual)
    {
        LinePosition at = source.GetPosition(expected.Offset);
        FlatKind? kind = ToFlatKind(actual.Kind);
        if (kind is null)
        {
            throw Mismatch($"the reader reports an unexpected {actual.Kind} node", actual.Position);
        }

        if (kind != expected.Kind)
        {
            throw Mismatch($"the scanner has {Describe(expected)} where the reader has {actual.Kind}", at);
        }

        if (actual.Position != at)
        {
            throw Mismatch(
                $"{Describe(expected)} is at {Where(at)} in the scanner but {Where(actual.Position)} in the reader",
                at);
        }

        if (expected.Name is not null && expected.Name != actual.Name)
        {
            throw Mismatch(
                $"{Describe(expected)} is named '{expected.Name}' in the scanner but '{actual.Name}' in the reader",
                at);
        }

        if (expected.Value is not null && expected.Value != actual.Value)
        {
            throw Mismatch($"{Describe(expected)} has a different value in the scanner and the reader", at);
        }

        if (expected.Kind != FlatKind.Element)
        {
            return;
        }

        if (expected.IsEmpty != actual.IsEmptyElement)
        {
            throw Mismatch($"{Describe(expected)} is written differently in the scanner and the reader", at);
        }

        CompareAttributes(source, expected, actual, at);
    }

    private static void CompareAttributes(SourceText source, FlatNode element, ReaderNode actual, LinePosition at)
    {
        List<ReaderAttribute> reported = actual.Attributes.Where(a => !a.IsDefault).ToList();
        if (reported.Count != element.Attributes.Count)
        {
            throw Mismatch(
                $"{Describe(element)} has {element.Attributes.Count} attributes in the scanner but {reported.Count} in the reader",
                at);
        }

        for (int j = 0; j < reported.Count; j++)
        {
            FlatAttribute expected = element.Attributes[j];
            ReaderAttribute attribute = reported[j];
            LinePosition attributeAt = source.GetPosition(expected.Offset);
            if (attribute.Name != expected.Name)
            {
                throw Mismatch(
                    $"attribute {j + 1} of {Describe(element)} is '{expected.Name}' in the scanner but '{attribute.Name}' in the reader",
                    attributeAt);
            }

            if (attribute.Position != attributeAt)
            {
                throw Mismatch(
                    $"attribute '{expected.Name}' is at {Where(attributeAt)} in the scanner but {Where(attribute.Position)} in the reader",
                    attributeAt);
            }

            if (attribute.Quote != expected.Quote)
            {
                throw Mismatch(
                    $"attribute '{expected.Name}' is quoted with {expected.Quote} in the scanner but {attribute.Quote} in the reader",
                    attributeAt);
            }

            if (expected.Value is not null && attribute.Value != expected.Value)
            {
                throw Mismatch($"attribute '{expected.Name}' has a different value in the scanner and the reader", attributeAt);
            }
        }
    }

    /// <summary>The CST's nodes in document order. Elements are followed by their attributes, children and end tag.</summary>
    private static List<FlatNode> Flatten(XmlDocumentSyntax document)
    {
        var list = new List<FlatNode>();
        if (document.Declaration is { } declaration)
        {
            list.Add(new FlatNode(FlatKind.XmlDeclaration, "xml", declaration.Start + 2, null, false, []));
        }

        foreach (XmlNodeSyntax node in document.Prolog)
        {
            AddLeaf(list, node);
        }

        // The element tree is walked with an explicit stack, as the scanner builds it.
        XmlElementSyntax root = document.Root;
        list.Add(ElementNode(root));
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
                    list.Add(ElementNode(childElement));
                    stack.Push((childElement, 0));
                }
                else
                {
                    AddLeaf(list, child);
                }
            }
            else if (element.EndTag is { } endTag)
            {
                list.Add(new FlatNode(FlatKind.EndElement, endTag.Name, endTag.NameStart, null, false, []));
            }
        }

        foreach (XmlNodeSyntax node in document.Epilog)
        {
            AddLeaf(list, node);
        }

        return list;
    }

    private static FlatNode ElementNode(XmlElementSyntax element)
    {
        var attributes = element.Attributes
            .Select(a => new FlatAttribute(a.Name, a.Start, a.Quote, a.IsRaw ? null : a.Value))
            .ToArray();
        return new FlatNode(FlatKind.Element, element.Name, element.NameStart, null, element.IsEmpty, attributes);
    }

    private static void AddLeaf(List<FlatNode> list, XmlNodeSyntax node)
    {
        switch (node)
        {
            case XmlDoctypeSyntax doctype:
                list.Add(new FlatNode(FlatKind.DocumentType, doctype.Name, doctype.NameStart, null, false, []));
                break;
            case XmlCommentSyntax comment:
                list.Add(new FlatNode(FlatKind.Comment, null, comment.Start + 4, comment.Content, false, []));
                break;
            case XmlProcessingInstructionSyntax pi:
                list.Add(new FlatNode(FlatKind.ProcessingInstruction, pi.Target, pi.Start + 2, pi.Data, false, []));
                break;
            case XmlTextSyntax text:
                list.Add(new FlatNode(FlatKind.Text, null, text.Start, text.IsRaw ? null : text.Value, false, []));
                break;
            case XmlCDataSyntax cdata:
                list.Add(new FlatNode(FlatKind.CData, null, cdata.Start + 9, cdata.Content, false, []));
                break;
            default:
                throw new InvalidOperationException($"A {node.GetType().Name} is not a leaf node.");
        }
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
}
