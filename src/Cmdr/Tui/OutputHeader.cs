using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// The one-row title line above the output pane: <c>── Title ────</c>, the rule filling the rest of the row. It replaces the
/// output's box, so nothing has to rebuild a border line per frame. Drawn with plain cells, and it redraws only when its
/// title or its focus state changes. The title is drawn in the focus role while the output has focus, as a framed title is.
/// </summary>
internal sealed class OutputHeader : View
{
    private const char Rule = '─';
    private const string Lead = "── ";
    private const string Trail = " ";

    private readonly Action _onClick;
    private string _title = string.Empty;
    private bool _paneFocused;

    /// <summary>Creates the header. <paramref name="onClick"/> runs on a left press, to focus the output.</summary>
    public OutputHeader(Action onClick)
    {
        _onClick = onClick;
        CanFocus = false;
    }

    /// <summary>The title text, drawn after the lead rule. Redraws only when it changes.</summary>
    public string HeaderTitle
    {
        get => _title;
        set
        {
            if (value == _title)
            {
                return;
            }

            _title = value;
            SetNeedsDraw();
        }
    }

    /// <summary>Whether the output pane has focus; the title is drawn in the focus role while it does. Redraws only when it changes.</summary>
    public bool PaneFocused
    {
        get => _paneFocused;
        set
        {
            if (value == _paneFocused)
            {
                return;
            }

            _paneFocused = value;
            SetNeedsDraw();
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        if (width <= 0)
        {
            return true;
        }

        Attribute rule = GetAttributeForRole(VisualRole.Normal);
        Attribute title = GetAttributeForRole(_paneFocused ? VisualRole.Focus : VisualRole.Normal);

        // The title is cut to what the lead and trail leave; below that, only the lead rule fits.
        int room = width - Lead.Length - Trail.Length;
        string shown = room > 0 ? TextWidth.Truncate(_title, room) : string.Empty;
        int used = room > 0 ? Lead.Length + TextWidth.Columns(shown) + Trail.Length : 0;
        int rest = Math.Max(0, width - used);

        Move(0, 0);
        SetAttribute(rule);
        AddStr(room > 0 ? Lead : TextWidth.Truncate(Lead, width));
        if (room > 0)
        {
            SetAttribute(title);
            AddStr(shown);
            SetAttribute(rule);
            AddStr(Trail + new string(Rule, rest));
        }

        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            return base.OnMouseEvent(mouse);
        }

        _onClick();
        return true;
    }
}
