using System.Text;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Model;

/// <summary>
/// A parsed config document: its format, its source text, its document tree and the format's concrete syntax tree. A
/// document is immutable. An edit produces a new one.
/// </summary>
public sealed class ConfigDocument
{
    internal ConfigDocument(IConfigFormat format, SourceText source, DocNode root, object syntax)
    {
        Format = format;
        Source = source;
        Root = root;
        Syntax = syntax;
    }

    /// <summary>The format the document was parsed as.</summary>
    public IConfigFormat Format { get; }

    /// <summary>The decoded source text.</summary>
    public SourceText Source { get; }

    /// <summary>The document node. Its children are the top-level nodes of the document.</summary>
    public DocNode Root { get; }

    /// <summary>The format's concrete syntax tree: a <c>JsonDocument</c> or an <c>XmlDocumentSyntax</c>.</summary>
    public object Syntax { get; }

    /// <summary>
    /// Parses a file's bytes. The JSON family decodes the bytes as UTF-8 or UTF-16 by BOM. XML decodes them itself, as
    /// XML 1.0 Appendix F describes.
    /// </summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="path">The file's path, carried by any error. Null when unknown.</param>
    /// <param name="format">The format to parse as.</param>
    /// <exception cref="Cfgedit.Core.CfgeditException">The bytes cannot be decoded, or the text is not valid in the format.</exception>
    public static ConfigDocument Load(ReadOnlySpan<byte> bytes, string? path, IConfigFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format is XmlFormat xml)
        {
            return xml.ParseBytes(bytes, path);
        }

        return format.Parse(SourceText.Decode(bytes, path));
    }

    /// <summary>Parses text that is already decoded.</summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">The text is not valid in the format.</exception>
    public static ConfigDocument Parse(SourceText source, IConfigFormat format)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(format);
        return format.Parse(source);
    }

    /// <summary>
    /// Applies an edit operation. The operation is planned, its text edits are spliced into the text, the new text is encoded
    /// and reparsed, and the logical tree of the result is compared with the operation applied to the logical tree of this
    /// document. This document is not changed. An operation whose edit changes nothing returns this document.
    /// </summary>
    /// <param name="op">The operation.</param>
    /// <returns>The new document and the path of the node to focus.</returns>
    /// <exception cref="Cfgedit.Core.CfgeditException">
    /// The operation is invalid, or the new text cannot be written in the file's encoding. An internal failure says
    /// "internal edit error" and asks for a report.
    /// </exception>
    public EditResult Apply(EditOp op)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (op is CompositeEdit composite)
        {
            return ApplyComposite(composite);
        }

        IReadOnlyList<TextEdit> edits = Format.Plan(this, op);
        if (edits.Count == 0)
        {
            return new EditResult(this, FocusAfter(this, op));
        }

        string text = SpliceEdits(edits);
        if (!CanEncode(text, Source.Encoding))
        {
            throw new CfgeditException($"the text contains a character that {Source.Encoding.WebName} cannot represent");
        }

        ConfigDocument after;
        try
        {
            after = Format.Parse(Source.WithText(text));
        }
        catch (CfgeditException ex) when (Format.IsUserError(ex))
        {
            // The edit made the document invalid, and the message says how: the user's change is refused, not the editor.
            throw new CfgeditException("this change would make the document invalid: " + ex.Message) { Line = ex.Line, Column = ex.Column };
        }
        catch (CfgeditException ex)
        {
            throw InternalEditError("the edited text does not parse: " + ex.Message);
        }

        LogicalNode expected;
        try
        {
            expected = LogicalEdit.Apply(this, Format.ToLogical(this), op);
        }
        catch (InvalidOperationException ex)
        {
            throw InternalEditError("the operation cannot be applied to the logical tree: " + ex.Message);
        }

        LogicalNode actual = Format.ToLogical(after);
        if (!LogicalNode.Matches(expected, actual))
        {
            throw InternalEditError(
                "the edited text does not match the operation.\nExpected:\n" + expected + "\nActual:\n" + actual);
        }

        return new EditResult(after, FocusAfter(after, op));
    }

    /// <summary>
    /// Applies the steps of a composite in order. Every step is checked before any is applied, so a nested composite is refused
    /// without changing anything. Each step is a full <see cref="Apply"/> on the document the previous step made; a failing step
    /// throws, and this document is kept because documents are immutable.
    /// </summary>
    private EditResult ApplyComposite(CompositeEdit composite)
    {
        ArgumentNullException.ThrowIfNull(composite.Steps);
        foreach (EditOp step in composite.Steps)
        {
            ArgumentNullException.ThrowIfNull(step, nameof(composite));
            if (step is CompositeEdit)
            {
                throw new ArgumentException("A composite edit cannot contain another composite edit.", nameof(composite));
            }
        }

        if (composite.Steps.Count == 0)
        {
            return new EditResult(this, null);
        }

        EditResult result = new(this, null);
        foreach (EditOp step in composite.Steps)
        {
            result = result.Document.Apply(step);
        }

        return result;
    }

    /// <summary>Returns the node at a path, or null when no node is there.</summary>
    public DocNode? Resolve(NodePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        DocNode node = Root;
        foreach (PathStep step in path.Steps)
        {
            DocNode? next = FindChild(node, step);
            if (next is null)
            {
                return null;
            }

            node = next;
        }

        return node;
    }

    /// <summary>
    /// Returns the node at a path if it exists. Otherwise it returns the deepest existing ancestor, or the nearest sibling of
    /// the first missing step: the sibling with the same name (or the same kind of step) whose ordinal or index is closest to
    /// the missing one, the lower one on a tie. The document node is returned when nothing closer exists.
    /// </summary>
    public DocNode ResolveNearest(NodePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        DocNode node = Root;
        int depth = 0;
        while (depth < path.Count)
        {
            DocNode? next = FindChild(node, path.Steps[depth]);
            if (next is null)
            {
                break;
            }

            node = next;
            depth++;
        }

        if (depth == path.Count)
        {
            return node;
        }

        return NearestSibling(node, path.Steps[depth]) ?? node;
    }

    /// <summary>
    /// The index among the parent's children at which an <see cref="InsertNode"/> goes before the node: before the node's
    /// attached leading comments, so that the new node does not separate them from the node they belong to.
    /// </summary>
    /// <param name="node">The node to insert before.</param>
    /// <exception cref="ArgumentException">There is no such node, or it is the document node.</exception>
    public int InsertionIndexBefore(NodePath node)
    {
        return AttachedGroup(node).First;
    }

    /// <summary>
    /// The index among the parent's children at which an <see cref="InsertNode"/> goes after the node: after the node's
    /// attached trailing comment.
    /// </summary>
    /// <param name="node">The node to insert after.</param>
    /// <exception cref="ArgumentException">There is no such node, or it is the document node.</exception>
    public int InsertionIndexAfter(NodePath node)
    {
        return AttachedGroup(node).Last + 1;
    }

    /// <summary>
    /// Moves a node one sibling unit up (<paramref name="delta"/> -1) or down (+1). A unit is an entry with its attached comments
    /// (see <see cref="InsertionIndexBefore"/>), and every other comment is a unit of its own. A comment moves alone. A neighbour
    /// that is part of the node's own unit (a comment attached to the same entry) is passed one child at a time.
    /// </summary>
    /// <param name="node">The node to move.</param>
    /// <param name="delta">-1 to move up, +1 to move down.</param>
    /// <returns>
    /// The move, with <see cref="MoveNode.NewIndex"/> already applied; or null when the node is at that end of its parent, or cannot
    /// move at all: a child of the document node other than a comment or a processing instruction (the JSON root value, the XML root
    /// element, declaration and doctype), or a node that would move above the XML declaration.
    /// </returns>
    /// <exception cref="ArgumentException">There is no such node, or it is the document node.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is neither -1 nor 1.</exception>
    public MoveNode? MoveBy(NodePath node, int delta)
    {
        ArgumentNullException.ThrowIfNull(node);
        CheckDelta(delta);
        DocNode target = Resolve(node) ?? throw new ArgumentException($"There is no node at {node}.", nameof(node));
        DocNode parent = target.Parent ?? throw new ArgumentException("The document node cannot be moved.", nameof(node));
        if (IsFixed(target))
        {
            return null;
        }

        int position = IndexOfChild(parent, target);
        (int first, int last) = EditLayout.IsComment(target) ? (position, position) : EditLayout.Group(parent, position, Source.Text);
        int size = last - first + 1;
        List<(int First, int Last)> units = SiblingUnits(parent);
        int newIndex;
        if (delta < 0)
        {
            if (first == 0 || parent.Children[first - 1].Kind == NodeKind.XmlDeclaration)
            {
                return null;
            }

            int before = first - 1;
            (int unitFirst, int unitLast) = UnitAt(units, before);
            newIndex = unitLast >= first ? before : unitFirst;
        }
        else
        {
            if (last == parent.Children.Count - 1)
            {
                return null;
            }

            int after = last + 1;
            (int unitFirst, int unitLast) = UnitAt(units, after);
            newIndex = (unitFirst <= last ? after + 1 : unitLast + 1) - size;
        }

        return new MoveNode(target.Path, newIndex);
    }

    /// <summary>
    /// Moves an attribute one position up (<paramref name="delta"/> -1) or down (+1) among its element's attributes.
    /// </summary>
    /// <param name="element">The element that holds the attribute.</param>
    /// <param name="index">The attribute's index among the element's attributes.</param>
    /// <param name="delta">-1 to move up, +1 to move down.</param>
    /// <returns>The move, or null when the attribute is already first (for -1) or last (for +1).</returns>
    /// <exception cref="ArgumentException">There is no such node.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The element has no attribute at <paramref name="index"/>, or delta is neither -1 nor 1.</exception>
    public MoveAttribute? MoveAttributeBy(NodePath element, int index, int delta)
    {
        ArgumentNullException.ThrowIfNull(element);
        CheckDelta(delta);
        DocNode node = Resolve(element) ?? throw new ArgumentException($"There is no node at {element}.", nameof(element));
        int count = node.Parts.Count(part => part.Kind == PartKind.AttributeName);
        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "There is no attribute at this index.");
        }

        int newIndex = index + delta;
        return newIndex < 0 || newIndex >= count ? null : new MoveAttribute(element, index, newIndex);
    }

    private static void CheckDelta(int delta)
    {
        if (delta is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(delta), delta, "The delta must be -1 or 1.");
        }
    }

    /// <summary>The nodes that cannot move: a child of the document node other than a comment or a processing instruction.</summary>
    private static bool IsFixed(DocNode node)
    {
        return node.Parent?.Kind == NodeKind.Document
            && node.Kind is not (NodeKind.JsonComment or NodeKind.XmlComment or NodeKind.XmlProcessingInstruction);
    }

    /// <summary>
    /// The units of a parent's children, in order. Each entry is a unit with the comments attached to it, and each other comment is a
    /// unit alone. Together the units cover every child once.
    /// </summary>
    private List<(int First, int Last)> SiblingUnits(DocNode parent)
    {
        var units = new List<(int First, int Last)>();
        int count = parent.Children.Count;
        int k = 0;
        while (k < count)
        {
            int entry = k;
            while (entry < count && EditLayout.IsComment(parent.Children[entry]))
            {
                entry++;
            }

            (int unitFirst, int unitLast) = entry < count ? EditLayout.Group(parent, entry, Source.Text) : (count, count - 1);
            unitFirst = Math.Max(unitFirst, k);
            for (; k < unitFirst; k++)
            {
                units.Add((k, k)); // comments that are not attached to the entry after them
            }

            if (entry == count)
            {
                break;
            }

            units.Add((unitFirst, unitLast));
            k = unitLast + 1;
        }

        return units;
    }

    private static (int First, int Last) UnitAt(List<(int First, int Last)> units, int index)
    {
        foreach ((int first, int last) in units)
        {
            if (first <= index && index <= last)
            {
                return (first, last);
            }
        }

        throw new InvalidOperationException($"No sibling unit holds child {index}.");
    }

    /// <summary>The sibling range that moves with a node, as the attachment rules give it.</summary>
    private (int First, int Last) AttachedGroup(NodePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        DocNode node = Resolve(path) ?? throw new ArgumentException($"There is no node at {path}.", nameof(path));
        DocNode parent = node.Parent ?? throw new ArgumentException("The document node has no insertion index.", nameof(path));
        return EditLayout.Group(parent, IndexOfChild(parent, node), Source.Text);
    }

    private static int IndexOfChild(DocNode parent, DocNode child)
    {
        for (int k = 0; k < parent.Children.Count; k++)
        {
            if (ReferenceEquals(parent.Children[k], child))
            {
                return k;
            }
        }

        throw new InvalidOperationException("The node is not among its parent's children.");
    }

    /// <summary>
    /// Checks the edits and splices them into the text. They must lie inside the text and must not overlap. Edits are applied
    /// from the end backwards; at one offset, a replacement goes before an insertion, and insertions keep their order.
    /// </summary>
    private string SpliceEdits(IReadOnlyList<TextEdit> edits)
    {
        string text = Source.Text;
        var ordered = edits
            .Select((edit, index) => (Edit: edit, Index: index))
            .OrderBy(item => item.Edit.Start)
            .ThenBy(item => item.Edit.End)
            .ThenBy(item => item.Index)
            .ToList();
        for (int k = 0; k < ordered.Count; k++)
        {
            TextEdit edit = ordered[k].Edit;
            if (edit.Start < 0 || edit.End < edit.Start || edit.End > text.Length)
            {
                throw InternalEditError("an edit lies outside the text");
            }

            if (k > 0 && ordered[k - 1].Edit.End > edit.Start)
            {
                throw InternalEditError("the planned edits overlap");
            }
        }

        var builder = new StringBuilder(text);
        for (int k = ordered.Count - 1; k >= 0; k--)
        {
            TextEdit edit = ordered[k].Edit;
            builder.Remove(edit.Start, edit.End - edit.Start);
            builder.Insert(edit.Start, edit.NewText);
        }

        return builder.ToString();
    }

    private static bool CanEncode(string text, System.Text.Encoding encoding)
    {
        try
        {
            return encoding.GetString(encoding.GetBytes(text)) == text;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static CfgeditException InternalEditError(string detail)
    {
        return new CfgeditException("internal edit error: " + detail + " (please report this)");
    }

    /// <summary>
    /// The path of the node to focus after an operation: the inserted, renamed, moved, edited or duplicated node, or after a
    /// delete the next sibling, else the previous one, else the parent. Attribute operations focus their element.
    /// </summary>
    private NodePath? FocusAfter(ConfigDocument after, EditOp op)
    {
        switch (op)
        {
            case SetPart { Part: PartKind.AttributeName or PartKind.AttributeValue } attribute:
                return attribute.Node;
            case SetPart set:
                return ChildAt(after, ParentPath(set.Node), IndexOfNode(set.Node));
            case InsertNode insert:
                return ChildAt(after, insert.Parent, insert.Index);
            case InsertAttribute insertAttribute:
                return insertAttribute.Element;
            case DeleteAttribute deleteAttribute:
                return deleteAttribute.Element;
            case MoveAttribute moveAttribute:
                return moveAttribute.Element;
            case DeleteNode delete:
            {
                (NodePath parent, int first, int last) = GroupOf(delete.Node);
                int remaining = ChildCount(parent) - (last - first + 1);
                if (first < remaining)
                {
                    return ChildAt(after, parent, first);
                }

                return first > 0 ? ChildAt(after, parent, first - 1) : parent;
            }
            case MoveNode move:
            {
                (NodePath parent, int first, int last) = GroupOf(move.Node);
                int remaining = ChildCount(parent) - (last - first + 1);
                return ChildAt(after, parent, Math.Clamp(move.NewIndex, 0, remaining));
            }
            case ChangeJsonType change:
                return ChildAt(after, ParentPath(change.Node), IndexOfNode(change.Node));
            case DuplicateNode duplicate:
            {
                (NodePath parent, int first, int last) = GroupOf(duplicate.Node);
                return ChildAt(after, parent, last + 1 + (IndexOfNode(duplicate.Node) - first));
            }
            default:
                return null;
        }
    }

    private static NodePath? ChildAt(ConfigDocument document, NodePath parentPath, int index)
    {
        DocNode? parent = document.Resolve(parentPath);
        if (parent is null || index < 0 || index >= parent.Children.Count)
        {
            return null;
        }

        return parent.Children[index].Path;
    }

    private int ChildCount(NodePath parentPath)
    {
        return Resolve(parentPath)?.Children.Count ?? 0;
    }

    private NodePath ParentPath(NodePath path)
    {
        return (Resolve(path)?.Parent ?? throw new InvalidOperationException("The node has no parent.")).Path;
    }

    private int IndexOfNode(NodePath path)
    {
        DocNode node = Resolve(path) ?? throw new InvalidOperationException("There is no node at " + path + ".");
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node has no index.");
        for (int k = 0; k < parent.Children.Count; k++)
        {
            if (ReferenceEquals(parent.Children[k], node))
            {
                return k;
            }
        }

        throw new InvalidOperationException("The node is not among its parent's children.");
    }

    /// <summary>The parent path and the sibling range that moves or copies with a node.</summary>
    private (NodePath Parent, int First, int Last) GroupOf(NodePath path)
    {
        DocNode node = Resolve(path) ?? throw new InvalidOperationException("There is no node at " + path + ".");
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node has no group.");
        (int first, int last) = EditLayout.Group(parent, IndexOfNode(path), Source.Text);
        return (parent.Path, first, last);
    }

    private static DocNode? FindChild(DocNode parent, PathStep step)
    {
        foreach (DocNode child in parent.Children)
        {
            if (child.Path.Steps[child.Path.Count - 1].Equals(step))
            {
                return child;
            }
        }

        return null;
    }

    private static DocNode? NearestSibling(DocNode parent, PathStep missing)
    {
        return missing switch
        {
            MemberStep member => Nearest<MemberStep>(parent, member.Ordinal, step => step.Key == member.Key, step => step.Ordinal),
            ElementStep element => Nearest<ElementStep>(parent, element.Ordinal, step => step.Name == element.Name, step => step.Ordinal),
            ItemStep item => Nearest<ItemStep>(parent, item.Index, _ => true, step => step.Index),
            CommentStep comment => Nearest<CommentStep>(parent, comment.Index, _ => true, step => step.Index),
            TextStep text => Nearest<TextStep>(parent, text.Index, _ => true, step => step.Index),
            PIStep pi => Nearest<PIStep>(parent, pi.Index, _ => true, step => step.Index),
            _ => null,
        };
    }

    private static DocNode? Nearest<TStep>(DocNode parent, int wanted, Func<TStep, bool> sameFamily, Func<TStep, int> position)
        where TStep : PathStep
    {
        DocNode? best = null;
        int bestDistance = int.MaxValue;
        int bestPosition = int.MaxValue;
        foreach (DocNode child in parent.Children)
        {
            if (child.Path.Steps[child.Path.Count - 1] is not TStep step || !sameFamily(step))
            {
                continue;
            }

            int candidate = position(step);
            int distance = Math.Abs(candidate - wanted);
            if (distance < bestDistance || (distance == bestDistance && candidate < bestPosition))
            {
                best = child;
                bestDistance = distance;
                bestPosition = candidate;
            }
        }

        return best;
    }
}
