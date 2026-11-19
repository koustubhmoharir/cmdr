using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tui;

/// <summary>
/// The status bar on the window's first row: the status text, or a transient message in its place. It is drawn on the bar colours
/// (<see cref="BarColors"/>), left-aligned and cut at the right edge; a message is drawn bold. It never takes the focus.
/// </summary>
internal sealed class StatusBar : View
{
    private string _text = string.Empty;
    private bool _message;

    /// <summary>Creates the bar. It shows no text until <see cref="Show"/> is called.</summary>
    public StatusBar()
    {
        CanFocus = false;
    }

    /// <summary>Shows the text. A message is drawn bold.</summary>
    /// <param name="text">The text.</param>
    /// <param name="message">Whether the text is a message.</param>
    public void Show(string text, bool message)
    {
        _text = text ?? string.Empty;
        _message = message;
        SetNeedsDraw();
    }

    /// <inheritdoc/>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        var attribute = new Attribute(BarColors.Text, BarColors.Background, _message ? TextStyle.Bold : TextStyle.None);
        Move(0, 0);
        SetAttribute(attribute);
        AddStr(TextWidth.Fit(_text, Viewport.Width));
        return true;
    }
}
