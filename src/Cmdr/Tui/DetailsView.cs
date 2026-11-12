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
/// <remarks>
/// The text can be selected with the mouse: a press starts a character selection, a drag extends it (dragging past the top or
/// bottom scrolls), and <c>c</c> copies it. The selection is kept as logical positions (see <see cref="DetailsSelection"/>), so
/// it survives redraws and scrolling; it is cleared by Esc and when another row is shown.
/// </remarks>
internal sealed class DetailsView : View
{
    private const int WheelLines = 3;

    private readonly MainController _controller;
    private ListRow? _row;
    private string _configDir = string.Empty;
    private int _offset;

    private SelectionState? _selection;
    private bool _dragging;

    private readonly record struct SelectionState(LogicalPoint Anchor, LogicalPoint Active);

    /// <summary>The wrapped rows of the content, and the logical text of each line.</summary>
    private sealed record DetailsLayout(IReadOnlyList<DetailsRow> Rows, IReadOnlyList<string> Texts);

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
            int total = Arrange(Viewport.Width).Rows.Count;
            return new DetailsScroll(DetailsText.ClampOffset(_offset, total, height), height, total);
        }
    }

    /// <summary>
    /// Shows the row. <paramref name="configDir"/> is where relative working directories resolve. The scroll position goes back
    /// to the top when the item changes, and the selection and any drag end with it; showing the same item again keeps them.
    /// </summary>
    public void Show(ListRow? row, string configDir)
    {
        if (!ReferenceEquals(KeyOf(_row), KeyOf(row)))
        {
            _offset = 0;
            _selection = null;
            StopDrag();
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

        // Esc means "stop" in cmdr; in this view it clears the selection and never reaches the application.
        if (key == Key.Esc)
        {
            return ClearSelection();
        }

        if (key == Key.C)
        {
            return CopySelectionOrCommandLine();
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

        if (_dragging)
        {
            // The grab routes every event here, including those outside the view, until the button is released.
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased) || mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                EndDrag();
            }
            else
            {
                DragTo(mouse);
            }

            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            return BeginDrag(mouse);
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
        DetailsLayout layout = Arrange(width);
        _offset = DetailsText.ClampOffset(_offset, layout.Rows.Count, height);
        LogicalRange? selection = SelectedRange(layout.Texts);

        for (int row = 0; row < height; row++)
        {
            int index = _offset + row;
            Move(0, row);
            if (index < layout.Rows.Count)
            {
                DrawRow(layout.Rows[index], width, normal, selection);
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
    /// Draws one wrapped row: each span in its own attribute, then the rest of the width in the normal attribute, so the row is
    /// exactly <paramref name="width"/> columns. The selected characters are drawn in reverse video.
    /// </summary>
    private void DrawRow(DetailsRow row, int width, Attribute normal, LogicalRange? selection)
    {
        if (TextWidth.Columns(row.Line.Text) > width)
        {
            // Only a single character wider than the width gets here; it is fitted like any other text.
            SetAttribute(normal);
            AddStr(TextWidth.Fit(row.Line.Text, width));
            return;
        }

        int used = 0;
        int offset = row.Start;
        foreach (DetailsSpan span in row.Line.Spans)
        {
            Attribute attr = AttributeFor(span.Style, normal);
            string text = span.Text;
            int i = 0;
            while (i < text.Length)
            {
                // A piece is a run of characters with the same selection state.
                bool selected = IsSelected(selection, row.LogicalLine, offset + i);
                int j = i + DetailsSelection.RuneAt(text, i).Length;
                while (j < text.Length && IsSelected(selection, row.LogicalLine, offset + j) == selected)
                {
                    j += DetailsSelection.RuneAt(text, j).Length;
                }

                string piece = text[i..j];
                SetAttribute(selected ? Invert(attr) : attr);
                AddStr(piece);
                used += TextWidth.Columns(piece);
                i = j;
            }

            offset += text.Length;
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

    /// <summary>Selected cells use the terminal's reverse video rather than swapped colors, as the output view does.</summary>
    private static Attribute Invert(Attribute attr) => new(attr.Foreground, attr.Background, attr.Style | TextStyle.Reverse);

    private static bool IsSelected(LogicalRange? selection, int line, int offset) =>
        selection is { } range && range.Covers(line, offset);

    private bool ScrollTo(int offset)
    {
        DetailsScroll scroll = Scroll;
        _offset = DetailsText.ClampOffset(offset, scroll.Total, scroll.Height);
        SetNeedsDraw();
        _controller.OnDetailsScrolled();
        return true;
    }

    /// <summary>
    /// Left press: focuses the view and starts a character selection at the cell under the pointer. The mouse is grabbed, so
    /// the drag keeps reporting when the pointer leaves the view.
    /// </summary>
    private bool BeginDrag(Mouse mouse)
    {
        SetFocus();
        DetailsLayout layout = Arrange(Viewport.Width);
        if (layout.Rows.Count == 0)
        {
            return true;
        }

        LogicalPoint point = DetailsSelection.PointAt(
            layout.Rows,
            layout.Texts,
            Scroll.Offset + (mouse.Position?.Y ?? 0),
            mouse.Position?.X ?? 0);
        _selection = new SelectionState(point, point);
        _dragging = true;
        App?.Mouse.GrabMouse(this);
        SetNeedsDraw();
        return true;
    }

    /// <summary>
    /// Drag: extends the selection to the cell under the pointer. Above or below the view, the content scrolls by one row per
    /// motion event and the pointer counts as being on the first or last visible row.
    /// </summary>
    private void DragTo(Mouse mouse)
    {
        if (_selection is null)
        {
            return;
        }

        int height = Math.Max(1, Viewport.Height);
        int row = mouse.Position?.Y ?? 0;
        int column = mouse.Position?.X ?? 0;
        if (row < 0)
        {
            ScrollTo(Scroll.Offset - 1);
            row = 0;
        }
        else if (row >= height)
        {
            ScrollTo(Scroll.Offset + 1);
            row = height - 1;
        }

        DetailsLayout layout = Arrange(Viewport.Width);
        if (layout.Rows.Count == 0)
        {
            return;
        }

        LogicalPoint point = DetailsSelection.PointAt(layout.Rows, layout.Texts, Scroll.Offset + row, column);
        _selection = _selection.Value with { Active = point };
        SetNeedsDraw();
    }

    /// <summary>Left release: releases the grab. A press without a drag leaves no selection.</summary>
    private void EndDrag()
    {
        StopDrag();
        if (_selection is { } s && s.Anchor == s.Active)
        {
            _selection = null;
        }

        SetNeedsDraw();
    }

    private void StopDrag()
    {
        if (_dragging)
        {
            _dragging = false;
            App?.Mouse.UngrabMouse();
        }
    }

    private bool ClearSelection()
    {
        if (_selection is not null)
        {
            _selection = null;
            SetNeedsDraw();
        }

        return true;
    }

    /// <summary>
    /// The selection on the current text, clamped to it: null when there is none or it is empty. The text can change between
    /// redraws (running times, a changed environment), so the stored positions are only trusted after clamping.
    /// </summary>
    private LogicalRange? SelectedRange(IReadOnlyList<string> texts)
    {
        if (_selection is not { } s)
        {
            return null;
        }

        LogicalRange range = DetailsSelection.Clamp(s.Anchor, s.Active, texts);
        return range.IsEmpty ? null : range;
    }

    /// <summary>
    /// Copies the selection. Without one, copies the command line of a command row, as the command list does (a group row
    /// copies nothing). The text goes to the controller, which writes it to the clipboard and reports the result.
    /// </summary>
    private bool CopySelectionOrCommandLine()
    {
        IReadOnlyList<string> texts = DetailsSelection.Texts(Lines());
        if (SelectedRange(texts) is { } range)
        {
            string text = DetailsSelection.Extract(texts, range);
            _controller.CopyToClipboard(text, $"Copied {CharacterCount(text)} characters");
            return true;
        }

        _controller.CopyCommandLine(_row);
        return true;
    }

    private static int CharacterCount(string text) => text.EnumerateRunes().Count();

    /// <summary>The rows of the content wrapped to <paramref name="width"/>, with the logical text. Empty for a non-positive width.</summary>
    private DetailsLayout Arrange(int width)
    {
        if (width <= 0)
        {
            return new DetailsLayout(Array.Empty<DetailsRow>(), Array.Empty<string>());
        }

        IReadOnlyList<DetailsLine> lines = Lines();
        return new DetailsLayout(DetailsText.WrapRows(lines, width), DetailsSelection.Texts(lines));
    }

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
