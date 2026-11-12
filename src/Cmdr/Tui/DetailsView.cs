using System.Collections;
using Cmdr.Core.Session;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// The details strip for the focused row: a command shows its command line, working directory, environment, pid and log,
/// and description; a group shows its mode, working directory, environment and description (see <see cref="DetailsText"/>).
/// The lines are wrapped to the width and scroll when they do not fit. The process state is read at draw time. UI thread only.
/// </summary>
internal sealed class DetailsView : View
{
    private const int WheelLines = 3;

    private readonly MainController _controller;
    private ListRow? _row;
    private string _configDir = string.Empty;
    private int _offset;

    public DetailsView(MainController controller)
    {
        _controller = controller;
        CanFocus = true;
        TabStop = TabBehavior.TabStop;
    }

    /// <summary>The scroll position, clamped to the current content and height.</summary>
    public DetailsScroll Scroll
    {
        get
        {
            int height = Math.Max(0, Viewport.Height);
            int total = Wrapped(Viewport.Width).Count;
            return new DetailsScroll(DetailsText.ClampOffset(_offset, total, height), height, total);
        }
    }

    /// <summary>
    /// Shows the row. <paramref name="configDir"/> is where relative working directories resolve. The scroll position goes back
    /// to the top when the item changes; showing the same item again keeps it.
    /// </summary>
    public void Show(ListRow? row, string configDir)
    {
        if (!ReferenceEquals(KeyOf(_row), KeyOf(row)))
        {
            _offset = 0;
        }

        _row = row;
        _configDir = configDir;
        SetNeedsDraw();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (_controller.OnGlobalKey(key))
        {
            return true;
        }

        if (key == Key.Tab || key == Key.Tab.WithShift)
        {
            _controller.CycleFocusPane(backward: key == Key.Tab.WithShift);
            return true;
        }

        DetailsScroll scroll = Scroll;
        int page = Math.Max(1, scroll.Height);
        if (key == Key.CursorUp)
        {
            return ScrollTo(scroll.Offset - 1);
        }

        if (key == Key.CursorDown)
        {
            return ScrollTo(scroll.Offset + 1);
        }

        if (key == Key.PageUp)
        {
            return ScrollTo(scroll.Offset - page);
        }

        if (key == Key.PageDown)
        {
            return ScrollTo(scroll.Offset + page);
        }

        if (key == Key.Home)
        {
            return ScrollTo(0);
        }

        if (key == Key.End)
        {
            return ScrollTo(scroll.Total);
        }

        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            return ScrollTo(Scroll.Offset - WheelLines);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            return ScrollTo(Scroll.Offset + WheelLines);
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            SetFocus();
            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        IReadOnlyList<DetailsLine> lines = Wrapped(width);
        _offset = DetailsText.ClampOffset(_offset, lines.Count, height);

        for (int row = 0; row < height; row++)
        {
            int index = _offset + row;
            Move(0, row);
            if (index < lines.Count)
            {
                DrawLine(lines[index], width, normal);
            }
            else
            {
                SetAttribute(normal);
                AddStr(new string(' ', width));
            }
        }

        return true;
    }

    /// <summary>
    /// Draws one wrapped line: each span in its own attribute, then the rest of the width in the normal attribute, so the row is
    /// exactly <paramref name="width"/> columns.
    /// </summary>
    private void DrawLine(DetailsLine line, int width, Attribute normal)
    {
        string plain = line.Text;
        if (TextWidth.Columns(plain) > width)
        {
            // Only a single character wider than the width gets here; it is fitted like any other text.
            SetAttribute(normal);
            AddStr(TextWidth.Fit(plain, width));
            return;
        }

        int used = 0;
        foreach (DetailsSpan span in line.Spans)
        {
            SetAttribute(AttributeFor(span.Style, normal));
            AddStr(span.Text);
            used += TextWidth.Columns(span.Text);
        }

        SetAttribute(normal);
        AddStr(new string(' ', Math.Max(0, width - used)));
    }

    /// <summary>
    /// The attribute of a span: a 16-color name, so the colors follow the terminal theme, on the normal background. The bright
    /// names are used where the normal ones have poor contrast on dark backgrounds.
    /// </summary>
    internal static Attribute AttributeFor(DetailsStyle style, Attribute normal)
    {
        Color fg = normal.Foreground;
        Color bg = normal.Background;
        return style switch
        {
            DetailsStyle.Prompt => new Attribute(fg, bg, TextStyle.Faint),
            DetailsStyle.Command => new Attribute(ColorName16.BrightGreen, bg, TextStyle.None),
            DetailsStyle.Label => new Attribute(fg, bg, TextStyle.Faint),
            DetailsStyle.Path => new Attribute(ColorName16.BrightCyan, bg, TextStyle.None),
            DetailsStyle.EnvName => new Attribute(ColorName16.BrightMagenta, bg, TextStyle.None),
            DetailsStyle.EnvValue => new Attribute(fg, bg, TextStyle.None),
            DetailsStyle.Unset => new Attribute(ColorName16.BrightRed, bg, TextStyle.None),
            DetailsStyle.Faint => new Attribute(fg, bg, TextStyle.Faint),
            _ => normal,
        };
    }

    private bool ScrollTo(int offset)
    {
        DetailsScroll scroll = Scroll;
        _offset = DetailsText.ClampOffset(offset, scroll.Total, scroll.Height);
        SetNeedsDraw();
        _controller.OnDetailsScrolled();
        return true;
    }

    /// <summary>The lines of the row, wrapped to <paramref name="width"/>. Empty for a non-positive width.</summary>
    private IReadOnlyList<DetailsLine> Wrapped(int width) =>
        width <= 0 ? Array.Empty<DetailsLine>() : DetailsText.Wrap(Lines(), width);

    private IReadOnlyList<DetailsLine> Lines() => _row switch
    {
        CommandRow command => DetailsText.CommandLines(
            command.Entry,
            _configDir,
            OperatingSystem.IsWindows(),
            ProcessEnvironment(),
            Path.PathSeparator),
        GroupRow group => DetailsText.GroupLines(group.Group, _configDir),
        _ => Array.Empty<DetailsLine>(),
    };

    private static IReadOnlyDictionary<string, string?> ProcessEnvironment()
    {
        var environment = new Dictionary<string, string?>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string?)entry.Value;
        }

        return environment;
    }

    /// <summary>The item a row shows. Rows are rebuilt on every structure change, so the item is compared, not the row.</summary>
    private static object? KeyOf(ListRow? row) => row switch
    {
        CommandRow command => command.Entry,
        GroupRow group => group.Group,
        _ => null,
    };
}
