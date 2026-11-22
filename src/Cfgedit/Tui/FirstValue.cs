using Cfgedit.Core.Json;
using Cfgedit.Core.Model;

namespace Cfgedit.Tui;

/// <summary>
/// The part an inserted node opens its inline editor on: the first part, in document order, that is a string or number value, an
/// attribute value, or text, and that the tree edits inline or in the multi-line editor. A boolean, a null, a key, and a container
/// have no such part.
/// </summary>
internal static class FirstValue
{
    /// <summary>The node that holds the part, and the part's index among that node's parts.</summary>
    public readonly record struct Target(DocNode Node, int Part);

    /// <summary>Finds the first editable value of a node or of its descendants, the node's own parts first, then its children in order.</summary>
    /// <param name="node">The node to search, normally the one just inserted.</param>
    /// <returns>The target, or null when no part of the node or its descendants is a value.</returns>
    public static Target? In(DocNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        for (int i = 0; i < node.Parts.Count; i++)
        {
            if (IsValue(node, node.Parts[i]))
            {
                return new Target(node, i);
            }
        }

        foreach (DocNode child in node.Children)
        {
            Target? found = In(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The part of an attribute's value on an element, by the attribute's index, or null when there is none.</summary>
    public static Target? AttributeValue(DocNode element, int attributeIndex)
    {
        ArgumentNullException.ThrowIfNull(element);
        for (int i = 0; i < element.Parts.Count; i++)
        {
            DocPart part = element.Parts[i];
            if (part.Kind == PartKind.AttributeValue && part.AttributeIndex == attributeIndex && part.Editable)
            {
                return new Target(element, i);
            }
        }

        return null;
    }

    private static bool IsValue(DocNode node, DocPart part)
    {
        bool isValue = part.Kind is PartKind.AttributeValue or PartKind.Text
            || (part.Kind == PartKind.Value && (node.Kind == NodeKind.EnvEntry || node.JsonKind is JsonValueKind.String or JsonValueKind.Number));
        return isValue && EditRules.KindOf(node, part) is EditorKind.Inline or EditorKind.Multiline;
    }
}
