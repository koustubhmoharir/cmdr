using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Shared.Tui;

/// <summary>A one-row bar: key hints or status text, or a transient message in its place. Drawn inverted, like a status bar.</summary>
public sealed class StatusLine : View
{
    private string _text = string.Empty;
    private string _right = string.Empty;
    private bool _message;

    /// <summary>
    /// Shows the text. A message is drawn bold. <paramref name="right"/> stays at the right edge; the text is truncated
    /// at its right end to make room for it.
    /// </summary>
    public void Show(string text, bool message, string right = "")
    {
        _text = text;
        _right = right;
        _message = message;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        var inverted = new Attribute(normal.Background, normal.Foreground, _message ? TextStyle.Bold : TextStyle.None);

        string line = _right.Length == 0
            ? TextWidth.Fit(_text, width)
            : TextWidth.Fit(_text, Math.Max(0, width - TextWidth.Columns(_right))) + TextWidth.Fit(_right, Math.Min(width, TextWidth.Columns(_right)));

        Move(0, 0);
        SetAttribute(inverted);
        AddStr(TextWidth.Fit(line, width));
        return true;
    }
}
