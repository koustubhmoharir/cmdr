using Cfgedit.Core.Model;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace Cfgedit.Tui;

/// <summary>
/// The detail line above the key hints: the path of the focused part, its kind, and its full logical value, cut with an ellipsis at
/// the width. It never takes the focus.
/// </summary>
internal sealed class DetailLine : View
{
    private ConfigDocument? _document;
    private DocNode? _node;
    private DocPart? _part;

    /// <summary>Creates the line. It shows nothing until <see cref="Show"/> is called.</summary>
    public DetailLine()
    {
        CanFocus = false;
    }

    /// <summary>Shows the focused part of a node. The part is null for a node with no part of its own.</summary>
    /// <param name="document">The document the node belongs to, or null to show nothing.</param>
    /// <param name="node">The node.</param>
    /// <param name="part">The focused part.</param>
    public void Show(ConfigDocument? document, DocNode? node, DocPart? part)
    {
        _document = document;
        _node = node;
        _part = part;
        SetNeedsDraw();
    }

    /// <summary>The text the line shows at a width: the path, two spaces, the kind, two spaces and the value, cut at the width.</summary>
    /// <param name="document">The document.</param>
    /// <param name="node">The node.</param>
    /// <param name="part">The focused part, or null.</param>
    /// <param name="width">The width in columns.</param>
    public static string Describe(ConfigDocument document, DocNode node, DocPart? part, int width)
    {
        string text = PathText.Format(document, node, part) + "  " + PathText.KindName(node, part) + "  " + ValueOf(node, part, width);
        return TextWidth.Truncate(text, width);
    }

    /// <inheritdoc/>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        if (width <= 0)
        {
            return true;
        }

        string text = _document is null || _node is null ? string.Empty : Describe(_document, _node, _part, width);
        Move(0, 0);
        SetAttribute(GetAttributeForRole(VisualRole.Normal));
        AddStr(TextWidth.Fit(text, width));
        return true;
    }

    // The value of the part as the rows show it, with an empty JSON string as "". Nothing for a node without a part.
    private static string ValueOf(DocNode node, DocPart? part, int width)
    {
        if (part is null)
        {
            return string.Empty;
        }

        if (part.Kind == PartKind.Value && node.JsonKind == JsonValueKind.String && part.Value.Length == 0)
        {
            return "\"\"";
        }

        return RowLayout.DisplayValue(part.Value, width);
    }
}
