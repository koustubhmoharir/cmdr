using System.Text;
using Cmdr.Core.Output;
using Cmdr.Core.Session;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// Shows the output buffer of one command: no wrapping, tail-follow by default. Scrolling up pauses following; End, or
/// scrolling back to the bottom, resumes it. While paused, the top line is remembered by absolute number so it stays
/// put as old lines are evicted. UI thread only.
/// </summary>
internal sealed class OutputView : View
{
    private const int WheelLines = 3;
    private const int HorizontalStep = 4;

    private readonly MainController _controller;
    private CommandEntry? _entry;
    private OutputBuffer? _buffer;
    private long _lastVersion = -1;
    private bool _following = true;
    private long _topLine;
    private int _hOffset;

    public OutputView(MainController controller)
    {
        _controller = controller;
        CanFocus = true;
        TabStop = TabBehavior.TabStop;
    }

    /// <summary>True while the view shows the last lines and follows new output.</summary>
    public bool Following => _following;

    /// <summary>Shows the output of a command, or nothing for null. Resets scrolling to tail-follow.</summary>
    public void Show(CommandEntry? entry)
    {
        _entry = entry;
        _buffer = entry?.Process.Output.Buffer;
        _following = true;
        _topLine = 0;
        _hOffset = 0;
        _lastVersion = -1;
        SetNeedsDraw();
    }

    /// <summary>Called on the UI thread every 100 ms. Redraws when the buffer changed.</summary>
    public void Tick()
    {
        long version = _buffer?.Version ?? -1;
        if (version != _lastVersion)
        {
            _lastVersion = version;
            SetNeedsDraw();
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (_controller.OnGlobalKey(key))
        {
            return true;
        }

        int page = Math.Max(1, Viewport.Height);
        if (key == Key.Tab || key == Key.Tab.WithShift)
        {
            _controller.ToggleFocusPane();
            return true;
        }

        if (key == Key.CursorUp)
        {
            return Scroll(-1);
        }

        if (key == Key.CursorDown)
        {
            return Scroll(1);
        }

        if (key == Key.PageUp)
        {
            return Scroll(-page);
        }

        if (key == Key.PageDown)
        {
            return Scroll(page);
        }

        if (key == Key.Home)
        {
            return ScrollToTop();
        }

        if (key == Key.End)
        {
            return ResumeFollowing();
        }

        if (key == Key.CursorLeft)
        {
            return PanHorizontal(-HorizontalStep);
        }

        if (key == Key.CursorRight)
        {
            return PanHorizontal(HorizontalStep);
        }

        // Esc means "stop" in cmdr; in this view it must not fall through to the application.
        return key == Key.Esc || base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        bool shift = mouse.Flags.HasFlag(MouseFlags.Shift);
        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            return shift ? PanHorizontal(-HorizontalStep) : Scroll(-WheelLines);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            return shift ? PanHorizontal(HorizontalStep) : Scroll(WheelLines);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledLeft))
        {
            return PanHorizontal(-HorizontalStep);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledRight))
        {
            return PanHorizontal(HorizontalStep);
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
        IReadOnlyList<OutputLine> lines = Array.Empty<OutputLine>();
        if (_buffer is not null)
        {
            Geometry geometry = GetGeometry();
            lines = _buffer.GetLines(TopIndex(geometry), height);
        }

        for (int row = 0; row < height; row++)
        {
            if (row < lines.Count)
            {
                DrawLine(lines[row], row, width, normal);
            }
            else
            {
                FillRow(row, 0, width, normal);
            }
        }

        return true;
    }

    private readonly record struct Geometry(long First, int Total, int MaxTop);

    private Geometry GetGeometry()
    {
        int height = Math.Max(0, Viewport.Height);
        long first = _buffer!.FirstLineNumber;
        int total = _buffer.Count;
        return new Geometry(first, total, Math.Max(0, total - height));
    }

    /// <summary>Index relative to the oldest retained completed line of the first visible line.</summary>
    private int TopIndex(Geometry geometry)
    {
        if (_following)
        {
            return geometry.MaxTop;
        }

        long relative = _topLine - geometry.First;
        return (int)Math.Clamp(relative, 0, geometry.MaxTop);
    }

    private bool Scroll(int delta)
    {
        if (_buffer is null)
        {
            return false;
        }

        Geometry geometry = GetGeometry();
        int top = TopIndex(geometry);
        int target = Math.Clamp(top + delta, 0, geometry.MaxTop);
        _topLine = geometry.First + target;
        _following = delta > 0 && target >= geometry.MaxTop;
        SetNeedsDraw();
        return true;
    }

    private bool ScrollToTop()
    {
        if (_buffer is null)
        {
            return false;
        }

        _topLine = _buffer.FirstLineNumber;
        _following = false;
        SetNeedsDraw();
        return true;
    }

    private bool ResumeFollowing()
    {
        _following = true;
        SetNeedsDraw();
        return true;
    }

    private bool PanHorizontal(int delta)
    {
        _hOffset = Math.Max(0, _hOffset + delta);
        SetNeedsDraw();
        return true;
    }

    /// <summary>Draws one line from column 0 of the viewport, honouring the horizontal offset, and pads the rest of the row.</summary>
    private void DrawLine(OutputLine line, int row, int width, Attribute normal)
    {
        int drawn = 0;
        int hidden = _hOffset;
        bool full = false;

        foreach (StyledSpan span in line.Spans)
        {
            if (full)
            {
                break;
            }

            Attribute attr = ColorMap.ToAttribute(span.Style, line.Source, normal);
            var run = new StringBuilder();
            int runStart = drawn;

            foreach (Rune original in span.Text.EnumerateRunes())
            {
                Rune rune = Rune.IsControl(original) ? new Rune(' ') : original;
                int cols = Math.Max(0, rune.GetColumns());
                if (cols == 0)
                {
                    continue;
                }

                if (hidden >= cols)
                {
                    hidden -= cols;
                    continue;
                }

                int visible = cols - hidden;
                hidden = 0;
                if (drawn + visible > width)
                {
                    full = true;
                    break;
                }

                if (visible == cols)
                {
                    run.Append(rune.ToString());
                }
                else
                {
                    run.Append(' ', visible);
                }

                drawn += visible;
            }

            if (run.Length > 0)
            {
                Move(runStart, row);
                SetAttribute(attr);
                AddStr(run.ToString());
            }
        }

        FillRow(row, drawn, width, normal);
    }

    private void FillRow(int row, int fromCol, int width, Attribute normal)
    {
        if (fromCol >= width)
        {
            return;
        }

        Move(fromCol, row);
        SetAttribute(normal);
        AddStr(new string(' ', width - fromCol));
    }
}
