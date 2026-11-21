using Cfgedit.Core.Model;

namespace Cfgedit.Tui;

/// <summary>
/// One visible row of the tree: a node's row, or the end row (<c>&lt;/name&gt;</c>) of an expanded XML element. Depth is 0 for
/// the top level.
/// </summary>
public sealed record Row(DocNode Node, bool IsEnd, int Depth);

/// <summary>
/// Flattens a document tree into the visible rows, given the set of collapsed nodes. The collapsed set is kept by
/// <see cref="NodePath"/>, so it survives an edit that keeps the paths.
/// </summary>
public static class DocRows
{
    /// <summary>Collapsible nodes at a depth greater than this (top-level nodes are depth 1) start collapsed.</summary>
    private const int MaxInitialDepth = 3;

    /// <summary>A collapsible node with more children than this starts collapsed.</summary>
    private const int MaxInitialChildren = 200;

    /// <summary>The collapsed set that a document starts with.</summary>
    /// <param name="document">The document.</param>
    public static IReadOnlySet<NodePath> Initial(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var collapsed = new HashSet<NodePath>();
        CollectInitial(document.Root, collapsed);
        return collapsed;
    }

    /// <summary>The visible rows of a document, in display order. Collapsed nodes show no children and no end row.</summary>
    /// <param name="document">The document.</param>
    /// <param name="collapsed">The collapsed nodes, by path.</param>
    public static IReadOnlyList<Row> Visible(ConfigDocument document, IReadOnlySet<NodePath> collapsed)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(collapsed);

        var rows = new List<Row>();
        foreach (DocNode child in document.Root.Children)
        {
            AddRows(child, 0, collapsed, rows);
        }

        return rows;
    }

    /// <summary>
    /// The collapsed set after an edit: the paths that still resolve in the new document. Paths that do not resolve are
    /// dropped.
    /// </summary>
    /// <param name="after">The document after the edit.</param>
    /// <param name="collapsed">The collapsed set before the edit.</param>
    public static IReadOnlySet<NodePath> Retain(ConfigDocument after, IReadOnlySet<NodePath> collapsed)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(collapsed);

        var kept = new HashSet<NodePath>();
        foreach (NodePath path in collapsed)
        {
            if (after.Resolve(path) is not null)
            {
                kept.Add(path);
            }
        }

        return kept;
    }

    /// <summary>
    /// True when a node can fold: it is not the document node, and it has children. A JSON container with no children, and an
    /// XML element with only text, are not collapsible.
    /// </summary>
    public static bool IsCollapsible(DocNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Kind != NodeKind.Document && node.Children.Count > 0;
    }

    private static void CollectInitial(DocNode node, HashSet<NodePath> collapsed)
    {
        if (IsCollapsible(node) && (node.Path.Count > MaxInitialDepth || node.Children.Count > MaxInitialChildren))
        {
            collapsed.Add(node.Path);
        }

        foreach (DocNode child in node.Children)
        {
            CollectInitial(child, collapsed);
        }
    }

    private static void AddRows(DocNode node, int depth, IReadOnlySet<NodePath> collapsed, List<Row> rows)
    {
        rows.Add(new Row(node, false, depth));
        bool expanded = !(IsCollapsible(node) && collapsed.Contains(node.Path));
        if (!expanded)
        {
            return;
        }

        foreach (DocNode child in node.Children)
        {
            AddRows(child, depth + 1, collapsed, rows);
        }

        if (node.EndParts.Count > 0)
        {
            rows.Add(new Row(node, true, depth));
        }
    }
}
