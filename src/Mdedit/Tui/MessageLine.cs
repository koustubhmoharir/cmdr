using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Mdedit.Tui;

/// <summary>
/// The message line above the key hints: the status (the heading path on the left and the scroll position on the right), or a message
/// in its place while one is shown. A message is drawn bold, in the error colour when it reports an error. It never takes the focus.
/// </summary>
internal sealed class MessageLine : View
{
    private const int Gap = 2;

    private string _left = string.Empty;
    private string _right = string.Empty;
    private string? _message;
    private bool _messageIsError;

    /// <summary>Creates the line. It shows nothing until <see cref="SetStatus"/> is called.</summary>
    public MessageLine()
    {
        CanFocus = false;
    }

    /// <summary>True while a message replaces the status.</summary>
    public bool HasMessage => _message is not null;

    /// <summary>Sets the status: the left part is cut at its right end to make room for the right part.</summary>
    /// <param name="left">The heading path, or empty.</param>
    /// <param name="right">The scroll position.</param>
    public void SetStatus(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        _left = left;
        _right = right;
        SetNeedsDraw();
    }

    /// <summary>Shows a message in place of the status, until <see cref="ClearMessage"/>.</summary>
    /// <param name="text">The message.</param>
    /// <param name="error">Whether the message reports an error.</param>
    public void ShowMessage(string text, bool error)
    {
        ArgumentNullException.ThrowIfNull(text);
        _message = text;
        _messageIsError = error;
        SetNeedsDraw();
    }

    /// <summary>Removes the message, if one is shown, and shows the status again.</summary>
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
        int width = Math.Max(0, Viewport.Width);
        if (width == 0)
        {
            return true;
        }

        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        string text;
        string right;
        Attribute attribute;
        if (_message is not null)
        {
            text = _message;
            right = string.Empty;
            attribute = new Attribute(_messageIsError ? MdColors.Error : normal.Foreground, normal.Background, TextStyle.Bold);
        }
        else
        {
            text = _left;
            right = _right;
            attribute = normal;
        }

        int rightWidth = TextWidth.Columns(right);
        int leftWidth = right.Length > 0 ? Math.Max(0, width - rightWidth - Gap) : width;
        string left = TextWidth.Truncate(text, leftWidth);
        int gap = width - TextWidth.Columns(left) - rightWidth;

        Move(0, 0);
        SetAttribute(attribute);
        AddStr(left + new string(' ', Math.Max(0, gap)) + right);
        return true;
    }
}
