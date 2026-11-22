using Cfgedit.Core.Model;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

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
    private string? _message;
    private bool _messageIsError;

    // The table's cell, when a table cell is shown instead of a part: its path, kind and raw value (null when the tree's part is shown).
    private string? _cellPath;
    private string? _cellKind;
    private string? _cellValue;

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
        _cellPath = null;
        SetNeedsDraw();
    }

    /// <summary>
    /// Shows a table cell: its path, its kind and its value. The value is the logical value, with an empty JSON string as <c>""</c>; it
    /// is cut at the width like a part's value. The cell replaces the part until <see cref="Show"/> is called.
    /// </summary>
    /// <param name="path">The path of the cell, or of the row for the index column.</param>
    /// <param name="kind">The kind, such as <c>string</c>, or <c>missing</c>.</param>
    /// <param name="value">The logical value, or empty.</param>
    public void ShowCell(string path, string kind, string value)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(value);
        _document = null;
        _node = null;
        _part = null;
        _cellPath = path;
        _cellKind = kind;
        _cellValue = value;
        SetNeedsDraw();
    }

    /// <summary>The text the line shows at a width: the path, two spaces, the kind, two spaces and the value, cut at the width.</summary>
    /// <param name="document">The document.</param>
    /// <param name="node">The node.</param>
    /// <param name="part">The focused part, or null.</param>
    /// <param name="width">The width in columns.</param>
    public static string Describe(ConfigDocument document, DocNode node, DocPart? part, int width)
    {
        return Compose(PathText.Format(document, node, part), PathText.KindName(node, part), ValueOf(node, part, width), width);
    }

    /// <summary>The path, the kind and the value, separated by two spaces, cut at the width.</summary>
    private static string Compose(string path, string kind, string value, int width) =>
        TextWidth.Truncate(path + "  " + kind + "  " + value, width);

    /// <summary>True while a message replaces the detail.</summary>
    public bool HasMessage => _message is not null;

    /// <summary>
    /// Shows a message in place of the detail until <see cref="ClearMessage"/> is called. An error is drawn in the error colour, and
    /// information in the normal one.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="error">Whether the message is an error.</param>
    public void ShowMessage(string text, bool error)
    {
        ArgumentNullException.ThrowIfNull(text);
        _message = text;
        _messageIsError = error;
        SetNeedsDraw();
    }

    /// <summary>Removes the message, so that the detail comes back.</summary>
    public void ClearMessage()
    {
        if (_message is null)
        {
            return;
        }

        _message = null;
        SetNeedsDraw();
    }

    /// <inheritdoc/>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        if (width <= 0)
        {
            return true;
        }

        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        if (_message is not null)
        {
            Attribute attribute = _messageIsError ? new Attribute(TreeColors.Error, normal.Background, TextStyle.None) : normal;
            Move(0, 0);
            SetAttribute(attribute);
            AddStr(TextWidth.Fit(_message, width));
            return true;
        }

        string text = _cellPath is not null
            ? Compose(_cellPath, _cellKind!, RowLayout.DisplayValue(_cellValue!, width), width)
            : _document is null || _node is null ? string.Empty : Describe(_document, _node, _part, width);
        Move(0, 0);
        SetAttribute(normal);
        AddStr(TextWidth.Fit(text, width));
        return true;
    }

    // The value of the part as the rows show it, with an empty JSON string or .env value as "". Nothing for a node without a part.
    private static string ValueOf(DocNode node, DocPart? part, int width)
    {
        // The root marker has no value to show: its kind already says root.
        if (part is null || part.Kind == PartKind.Root)
        {
            return string.Empty;
        }

        bool stringValue = node.JsonKind == JsonValueKind.String || node.Kind == NodeKind.EnvEntry;
        if (part.Kind == PartKind.Value && stringValue && part.Value.Length == 0)
        {
            return "\"\"";
        }

        return RowLayout.DisplayValue(part.Value, width);
    }
}
