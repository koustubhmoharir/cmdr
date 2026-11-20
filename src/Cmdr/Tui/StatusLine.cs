using System.Text;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// The bottom row: key hints, or a transient message in its place. Drawn inverted, like a status bar. The keys of the hints are
/// click targets, drawn as buttons on <see cref="HintColors"/>: a left press on one performs its key, through the application's
/// keyboard, as a key press from the terminal does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Clicks.</b> The press is handled, not the release. A press on text with no target, on a message, or past the text does nothing.
/// The mapping is by display columns (<see cref="TextWidth"/>), so a wide glyph takes the columns it is drawn in. The line cannot take
/// the focus, so a press leaves the focused view and its selection as they are.
/// </para>
/// <para>
/// <b>Layout.</b> The right part stays at the right edge; the left text is cut at its right end to make room for it. A message is
/// drawn bold.
/// </para>
/// </remarks>
internal sealed class StatusLine : View
{
    private string _text = string.Empty;
    private string _right = string.Empty;
    private bool _message;
    private IReadOnlyList<HintTarget> _textTargets = [];
    private IReadOnlyList<HintTarget> _rightTargets = [];

    /// <summary>Creates the line. It shows nothing until <see cref="Show(string, bool, string)"/> or <see cref="Show(Hint, Hint)"/> is called.</summary>
    public StatusLine()
    {
        CanFocus = false;
    }

    /// <summary>The left hint as last shown, with its click targets. Empty while a message is shown.</summary>
    internal Hint Shown => new(_text, _textTargets);

    /// <summary>The right hint as last shown, with its click targets.</summary>
    internal Hint ShownRight => new(_right, _rightTargets);

    /// <summary>
    /// Shows the text, with no click targets. A message is drawn bold. <paramref name="right"/> stays at the right edge; the text is
    /// truncated at its right end to make room for it.
    /// </summary>
    public void Show(string text, bool message, string right = "") => Set(text, message, right, [], []);

    /// <summary>
    /// Shows key hints: <paramref name="text"/> truncated at its right end to make room for <paramref name="right"/>, which stays at the
    /// right edge. Their targets are clickable.
    /// </summary>
    public void Show(Hint text, Hint right)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(right);
        Set(text.Text, message: false, right.Text, text.Targets, right.Targets);
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
        Attribute normal = GetAttributeForRole(VisualRole.Normal);
        var bar = new Attribute(normal.Background, normal.Foreground, _message ? TextStyle.Bold : TextStyle.None);
        var button = new Attribute(HintColors.TargetText, HintColors.TargetBackground);

        // The graphemes of a run share one attribute: a run is drawn as a button while its cells have a key.
        var run = new StringBuilder();
        bool? runOnButton = null;
        int drawn = 0;
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
            drawn = cell.Column + cell.Width;
        }

        if (runOnButton is { } last)
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

    /// <summary>
    /// The graphemes as drawn in a line of <paramref name="width"/> columns: the left text in the columns the right part leaves, and the
    /// right part at the right edge (or cut to the width when the line is narrower than it).
    /// </summary>
    private List<Cell> Place(int width)
    {
        var cells = new List<Cell>();
        int rightWidth = TextWidth.Columns(_right);
        if (_right.Length == 0)
        {
            AddPart(cells, _text, _textTargets, width, 0);
            return cells;
        }

        int rightStart = Math.Max(0, width - rightWidth);
        AddPart(cells, _text, _textTargets, rightStart, 0);
        AddPart(cells, _right, _rightTargets, Math.Min(width, rightWidth), rightStart);
        return cells;
    }

    /// <summary>
    /// Adds the part, fitted to <paramref name="columns"/>, from <paramref name="startColumn"/>. A grapheme of the fitted text takes the key of
    /// the target that covers its character index in the text.
    /// </summary>
    private static void AddPart(List<Cell> cells, string text, IReadOnlyList<HintTarget> targets, int columns, int startColumn)
    {
        string fitted = TextWidth.Fit(text, columns);
        int column = startColumn;
        foreach ((int offset, int length, int cellWidth) in TextWidth.Graphemes(fitted))
        {
            cells.Add(new Cell(fitted.Substring(offset, length), column, cellWidth, KeyOf(targets, offset)));
            column += cellWidth;
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
