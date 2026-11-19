using System.Text;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tui;

/// <summary>
/// The key hints on the window's last row. The row is drawn on the bar colours (<see cref="BarColors"/>), left-aligned and cut at the right
/// edge. Each click target is drawn as a button, on the target colours, and the text outside the targets stays on the bar colours.
/// </summary>
/// <remarks>
/// <para>
/// <b>Clicks.</b> A left button press on a target performs its key. The press is handled, not the release, as the timeline and the detail
/// handle theirs. The key is raised through the application's keyboard (<see cref="Terminal.Gui.App.IKeyboard.RaiseKeyDownEvent(Terminal.Gui.Input.Key)"/>), as a key press from the
/// terminal is: the focused view takes it first, then the window. A press on text with no target, or past the text, does nothing.
/// </para>
/// <para>
/// <b>Focus.</b> The line cannot take the focus, so a press leaves the focused view and its selection as they are. The mapping of a press
/// is by display columns, measured with <see cref="TextWidth.Columns"/>, so a wide glyph such as <c>←</c> takes the columns it is drawn in.
/// </para>
/// </remarks>
internal sealed class HintsLine : View
{
    private Hint _shown = KeyHints.Plain(string.Empty);

    /// <summary>Creates the line. It shows no hint until <see cref="Show"/> is called.</summary>
    public HintsLine()
    {
        CanFocus = false;
    }

    /// <summary>The hint as last shown.</summary>
    internal Hint Shown => _shown;

    /// <summary>Shows the hint, replacing the one shown.</summary>
    /// <param name="hint">The hint.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hint"/> is <c>null</c>.</exception>
    public void Show(Hint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        _shown = hint;
        SetNeedsDraw();
    }

    /// <summary>The key that the text under a display column performs, or <c>null</c> when the text there has no target.</summary>
    /// <param name="column">The display column, from the left edge of the line.</param>
    internal Key? KeyAt(int column)
    {
        string text = _shown.Text;
        foreach (HintTarget target in _shown.Targets)
        {
            int start = TextWidth.Columns(text[..target.Start]);
            int end = start + TextWidth.Columns(text.Substring(target.Start, target.Length));
            if (column >= start && column < end)
            {
                return target.Key;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        string text = TextWidth.Truncate(_shown.Text, width);

        // A character is on a target when a target's span covers it; the graphemes of a run share one attribute.
        var onTarget = new bool[_shown.Text.Length];
        foreach (HintTarget target in _shown.Targets)
        {
            for (int i = target.Start; i < target.Start + target.Length; i++)
            {
                onTarget[i] = true;
            }
        }

        var bar = new Attribute(BarColors.Text, BarColors.Background);
        var button = new Attribute(BarColors.TargetText, BarColors.TargetBackground);
        var run = new StringBuilder();
        bool? runOnTarget = null;
        int drawn = 0;
        Move(0, 0);
        foreach ((int offset, int length, int cells) in TextWidth.Graphemes(text))
        {
            bool on = offset < onTarget.Length && onTarget[offset];
            if (runOnTarget is { } previous && previous != on)
            {
                DrawRun(run, previous ? button : bar);
            }

            runOnTarget = on;
            run.Append(text, offset, length);
            drawn += cells;
        }

        if (runOnTarget is { } last)
        {
            DrawRun(run, last ? button : bar);
        }

        if (drawn < width)
        {
            SetAttribute(bar);
            AddStr(new string(' ', width - drawn));
        }

        return true;
    }

    /// <summary>Draws a run of graphemes in an attribute, at the cursor, and clears the run.</summary>
    private void DrawRun(StringBuilder run, Attribute attribute)
    {
        SetAttribute(attribute);
        AddStr(run.ToString());
        run.Clear();
    }

    /// <inheritdoc/>
    protected override bool OnMouseEvent(Mouse mouse) => HandleMouse(mouse) || base.OnMouseEvent(mouse);

    /// <summary>Performs the key under a left press. Returns <c>true</c> for a left press on the line, whether or not it had a target.</summary>
    /// <param name="mouse">The mouse event.</param>
    internal bool HandleMouse(Mouse mouse)
    {
        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) || mouse.Position is not { } position)
        {
            return false;
        }

        if (KeyAt(position.X) is { } key)
        {
            App?.Keyboard.RaiseKeyDownEvent(key);
        }

        return true;
    }
}
