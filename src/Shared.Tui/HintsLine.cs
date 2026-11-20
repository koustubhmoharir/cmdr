using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Shared.Tui;

/// <summary>
/// The bar on the window's last row: key hints, or a transient message in their place. The row is drawn on the bar colours
/// (<see cref="BarColors"/>). Each click target is drawn as a button, on the target colours, and the text outside the targets stays on
/// the bar colours. A message is drawn bold and has no targets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> The left text is cut at its right end to make room for the right part (see <see cref="Show(Hint, Hint)"/>), which stays
/// at the right edge. A line narrower than the right part shows the start of it, at column zero, and no left text.
/// </para>
/// <para>
/// <b>Clicks.</b> A left button press on a target performs its key. The press is handled, not the release. The key is raised through the application's keyboard (<see cref="Terminal.Gui.App.IKeyboard.RaiseKeyDownEvent(Terminal.Gui.Input.Key)"/>), as a key press from the
/// terminal is: the focused view takes it first, then the window. A press on text with no target, on a message, or past the text, does nothing.
/// </para>
/// <para>
/// <b>Focus.</b> The line cannot take the focus, so a press leaves the focused view and its selection as they are. The mapping of a press
/// is by display columns, measured with <see cref="TextWidth"/>, so a wide glyph such as <c>←</c> takes the columns it is drawn in.
/// </para>
/// </remarks>
public sealed class HintsLine : View
{
    private string _text = string.Empty;
    private string _right = string.Empty;
    private bool _message;
    private IReadOnlyList<HintTarget> _textTargets = [];
    private IReadOnlyList<HintTarget> _rightTargets = [];

    /// <summary>Creates the line. It shows nothing until one of the <c>Show</c> methods is called.</summary>
    public HintsLine()
    {
        CanFocus = false;
    }

    /// <summary>The left hint as last shown, with its click targets. Empty while a message is shown.</summary>
    internal Hint Shown => new(_text, _textTargets);

    /// <summary>The right hint as last shown, with its click targets.</summary>
    internal Hint ShownRight => new(_right, _rightTargets);

    /// <summary>Shows the hint, replacing the one shown.</summary>
    /// <param name="hint">The hint.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hint"/> is <c>null</c>.</exception>
    public void Show(Hint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        Set(hint.Text, message: false, string.Empty, hint.Targets, []);
    }

    /// <summary>
    /// Shows key hints: <paramref name="text"/> truncated at its right end to make room for <paramref name="right"/>, which stays at the
    /// right edge. The targets of both are clickable.
    /// </summary>
    /// <param name="text">The left hint.</param>
    /// <param name="right">The right hint, such as the quit key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="right"/> is <c>null</c>.</exception>
    public void Show(Hint text, Hint right)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(right);
        Set(text.Text, message: false, right.Text, text.Targets, right.Targets);
    }

    /// <summary>
    /// Shows the text, with no click targets. A message is drawn bold. <paramref name="right"/> stays at the right edge; the text is
    /// truncated at its right end to make room for it.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="message">Whether the text is a message, which is drawn bold.</param>
    /// <param name="right">Plain text that stays at the right edge.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="right"/> is <c>null</c>.</exception>
    public void Show(string text, bool message, string right = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(right);
        Set(text, message, right, [], []);
    }

    private void Set(string text, bool message, string right, IReadOnlyList<HintTarget> textTargets, IReadOnlyList<HintTarget> rightTargets)
    {
        _text = text;
        _right = right;
        _message = message;
        _textTargets = textTargets;
        _rightTargets = rightTargets;
        SetNeedsDraw();
    }

    /// <summary>One grapheme as drawn: its text, its first column, its width in columns, and the key a click on it performs (null for none).</summary>
    private readonly record struct Cell(string Text, int Column, int Width, Key? Key);

    /// <inheritdoc/>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        var bar = new Attribute(BarColors.Text, BarColors.Background, _message ? TextStyle.Bold : TextStyle.None);
        var button = new Attribute(BarColors.TargetText, BarColors.TargetBackground);

        // The graphemes of a run share one attribute: a run is drawn as a button while its cells have a key.
        var run = new StringBuilder();
        bool? runOnButton = null;
        Move(0, 0);
        foreach (Cell cell in Place(width))
        {
            bool onButton = cell.Key is not null;
            if (runOnButton is { } previous && previous != onButton)
            {
                DrawRun(run, previous ? button : bar);
            }

            runOnButton = onButton;
            run.Append(cell.Text);
        }

        if (runOnButton is { } last)
        {
            DrawRun(run, last ? button : bar);
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

    /// <summary>
    /// The graphemes as drawn in a line of <paramref name="width"/> columns: the left text in the columns the right part leaves, and the
    /// right part at the right edge (or cut to the width when the line is narrower than it). Each column of the line is in one cell.
    /// </summary>
    private List<Cell> Place(int width)
    {
        var cells = new List<Cell>();
        if (_right.Length == 0)
        {
            AddPart(cells, _text, _textTargets, width, 0);
            return cells;
        }

        int rightWidth = TextWidth.Columns(_right);
        int rightStart = Math.Max(0, width - rightWidth);
        AddPart(cells, _text, _textTargets, rightStart, 0);
        AddPart(cells, _right, _rightTargets, Math.Min(width, rightWidth), rightStart);
        return cells;
    }

    /// <summary>
    /// Adds the part, cut to <paramref name="columns"/> and padded with plain cells to that width, from <paramref name="startColumn"/>.
    /// A grapheme of the cut text takes the key of the target that covers its character index in the text. The padding has no key.
    /// </summary>
    private static void AddPart(List<Cell> cells, string text, IReadOnlyList<HintTarget> targets, int columns, int startColumn)
    {
        string cut = TextWidth.Truncate(text, columns);
        int column = startColumn;
        foreach ((int offset, int length, int cellWidth) in TextWidth.Graphemes(cut))
        {
            cells.Add(new Cell(cut.Substring(offset, length), column, cellWidth, KeyOf(targets, offset)));
            column += cellWidth;
        }

        for (int pad = Math.Max(0, columns) - TextWidth.Columns(cut); pad > 0; pad--)
        {
            cells.Add(new Cell(" ", column, 1, null));
            column++;
        }
    }

    /// <summary>The key of the target covering the character at <paramref name="index"/>, or null when none does.</summary>
    private static Key? KeyOf(IReadOnlyList<HintTarget> targets, int index)
    {
        foreach (HintTarget target in targets)
        {
            if (index >= target.Start && index < target.Start + target.Length)
            {
                return target.Key;
            }
        }

        return null;
    }

    /// <summary>The key that a click on the display column performs in a line of <paramref name="width"/> columns, or null for none.</summary>
    /// <param name="column">The display column, from the left edge of the line.</param>
    /// <param name="width">The width of the line.</param>
    internal Key? KeyAt(int column, int width)
    {
        foreach (Cell cell in Place(width))
        {
            if (column >= cell.Column && column < cell.Column + cell.Width)
            {
                return cell.Key;
            }
        }

        return null;
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

        if (KeyAt(position.X, Viewport.Width) is { } key)
        {
            App?.Keyboard.RaiseKeyDownEvent(key);
        }

        return true;
    }
}
