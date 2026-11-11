using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>The bottom row: key hints, or a transient message in its place. Drawn inverted, like a status bar.</summary>
internal sealed class StatusLine : View
{
    private string _text = string.Empty;
    private bool _message;

    /// <summary>Shows the text. A message is drawn bold.</summary>
    public void Show(string text, bool message)
    {
        _text = text;
        _message = message;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        var inverted = new Attribute(normal.Background, normal.Foreground, _message ? TextStyle.Bold : TextStyle.None);

        Move(0, 0);
        SetAttribute(inverted);
        AddStr(TextWidth.Fit(_text, width));
        return true;
    }
}
