using System.Text;
using Mdedit.Core.Editing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Mdedit.Tui;

/// <summary>
/// The text keys of the view, and the pending insertion it draws. Typed characters, Backspace, Delete and Enter are the view's edit keys; every
/// other key is a command. A pending insertion is the source text that <see cref="ViewEdit"/> holds at the caret: a held space (<see cref="PendingPlacement.Inline"/>,
/// drawn at the caret), or a new block drawn on a virtual line of its own, below the caret's line (<see cref="PendingPlacement.Below"/>) or above it
/// (<see cref="PendingPlacement.Above"/>). The window gives the pending back to the next insertion.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> The view drops the pending insertion when the cursor is placed or moves (a handled key, or a press, a double-click or a
/// right-click), when the document or the folds change, and when the window sets another one. The window drops it for a command that the
/// view does not handle, and Ctrl+Z drops it without an undo.
/// </para>
/// <para>
/// <b>The virtual line.</b> A block pending is drawn on a row of its own: the gutter (two blanks, as a content line has), then its
/// <see cref="ViewEditResult.Pending.DisplayText"/>, and the cursor after it. A below pending's row is the one under the caret's row, and an above
/// pending's row is the caret's row; the caret's line and the lines after it (or before it) move down by one row. Rows are mapped by
/// <see cref="LineAtRow"/>, which gives the caret's line for the virtual row itself, so a click on it places the caret on that line. The view's
/// line count includes the virtual line, so that it can be scrolled to the end of the document. A document with no block has an empty line of its
/// own, with the caret on it (see <see cref="IsEmptyDocument"/>), which an inline pending is drawn on.
/// </para>
/// </remarks>
internal sealed partial class RenderedView
{
    // The blank columns before the content of a line: the fold gutter, as LinePrefix.Gutter gives it.
    private const int GutterWidth = 2;

    /// <summary>The pending insertion held at the caret, or null when there is none.</summary>
    public ViewEditResult.Pending? Pending { get; private set; }

    /// <summary>
    /// Sets or drops the pending insertion. An inline one is drawn at the cursor, and the cursor moves after it; a block one is drawn on a
    /// virtual line, which is scrolled into view. It stays until the view drops it (see the remarks of the class).
    /// </summary>
    /// <param name="pending">The pending insertion, or null to drop it.</param>
    public void SetPending(ViewEditResult.Pending? pending)
    {
        Pending = pending;
        if (pending is not null && (HasCursor || IsEmptyDocument))
        {
            int topBefore = _top;
            EnsureVisible();
            if (_top != topBefore)
            {
                SyncTracking();
                PositionChanged?.Invoke();
            }
        }

        UpdateCursor();
        SetNeedsDraw();
    }

    /// <summary>Whether the document has no block, so that it has no caret stop: its caret is on the empty line the view draws.</summary>
    private bool IsEmptyDocument => _document.Blocks.Length == 0;

    /// <summary>Whether the view draws a virtual line: an empty document's line, or a block pending with a cursor (see the remarks of the class).</summary>
    private bool HasVirtualLine => IsEmptyDocument || (Pending is { Placement: not PendingPlacement.Inline } && HasCursor);

    /// <summary>The screen row of the virtual line. A below pending's row is under the caret's row, an above pending's is the caret's row.</summary>
    private int VirtualRow => IsEmptyDocument ? 0 : _line - _top + (Pending?.Placement == PendingPlacement.Below ? 1 : 0);

    /// <summary>The text of the virtual line: the pending's display text, or nothing for an empty document without a pending.</summary>
    private string VirtualText => Pending?.DisplayText ?? string.Empty;

    /// <summary>The number of rows of the layout the view scrolls over: the display lines, and the virtual line when there is one.</summary>
    private int DisplayedLineCount => LineCount + (HasVirtualLine ? 1 : 0);

    /// <summary>The screen row of the cursor: the virtual line's when it shows one, else the caret's line's.</summary>
    private int CursorRow => HasVirtualLine ? VirtualRow : _line - _top;

    /// <summary>
    /// The layout line that a screen row shows. The virtual row shows the caret's line, so that a click on it places the caret there; the rows
    /// after a virtual line show the line one above their own row. A row past the layout's last line shows a line past the end.
    /// </summary>
    /// <param name="y">The screen row within the view.</param>
    private int LineAtRow(int y)
    {
        if (!HasVirtualLine)
        {
            return _top + y;
        }

        int row = VirtualRow;
        return y == row ? _line : y < row ? _top + y : _top + y - 1;
    }

    /// <summary>
    /// Draws the virtual line on its row: the gutter, then the pending's display text, clipped at the edge. An empty document with no pending has
    /// a blank line.
    /// </summary>
    /// <param name="y">The screen row.</param>
    /// <param name="width">The width of the view.</param>
    /// <param name="normal">The attribute of the text.</param>
    private void DrawVirtualLine(int y, int width, Attribute normal)
    {
        string?[] texts = new string?[width];
        var attributes = new Attribute[width];
        Array.Fill(texts, " ");
        Array.Fill(attributes, normal);
        string text = VirtualText;
        for (int i = 0; i < text.Length && GutterWidth + i < width; i++)
        {
            texts[GutterWidth + i] = text[i].ToString();
        }

        DrawColumns(y, texts, attributes);
    }

    /// <summary>
    /// Scrolls so that the virtual line is on the screen: a below pending's row is one under the caret's, so when that row is past the bottom the
    /// top line scrolls by the rows it is past. The top stays within the rows of the layout and the virtual line.
    /// </summary>
    private void EnsureVirtualLineVisible()
    {
        if (!HasVirtualLine)
        {
            return;
        }

        int past = VirtualRow - (ViewportHeight - 1);
        if (past > 0)
        {
            _top += past;
        }

        ClampTop();
    }

    /// <summary>The number of columns the inline pending insertion takes: its display text, which the view draws.</summary>
    private int PendingWidth => Pending is { Placement: PendingPlacement.Inline } inline ? inline.DisplayText.Length : 0;

    /// <summary>
    /// Draws the inline pending insertion at a column of the caret's line: its display text is written there and the rest of the line moves
    /// right, clipped at the edge, so no text is overwritten. Nothing is drawn for a column outside the line.
    /// </summary>
    /// <param name="texts">The text of each column of the line; a null column is the second half of a wide cell.</param>
    /// <param name="attributes">The attribute of each column.</param>
    /// <param name="column">The column of the caret.</param>
    /// <param name="attribute">The attribute of the pending text.</param>
    private void ShowPending(string?[] texts, Attribute[] attributes, int column, Attribute attribute)
    {
        if (Pending is not { Placement: PendingPlacement.Inline } pending || column < 0 || column >= texts.Length)
        {
            return;
        }

        string text = pending.DisplayText;
        for (int x = texts.Length - 1; x >= column + text.Length; x--)
        {
            texts[x] = texts[x - text.Length];
            attributes[x] = attributes[x - text.Length];
        }

        for (int i = 0; i < text.Length && column + i < texts.Length; i++)
        {
            texts[column + i] = text[i].ToString();
            attributes[column + i] = attribute;
        }
    }

    /// <summary>
    /// Whether a key types a character: a key with neither Ctrl nor Alt whose character is printable (Shift is allowed), or a key with Ctrl and
    /// Alt (AltGr on Windows) whose character is printable and not a letter. Such keys are text, so the view's letters and digits are not
    /// commands.
    /// </summary>
    /// <param name="key">The key.</param>
    internal static bool IsTyping(Key key)
    {
        Rune rune = key.AsRune;
        if (Rune.IsControl(rune))
        {
            return false;
        }

        if (key.IsCtrl && key.IsAlt)
        {
            return !Rune.IsLetter(rune);
        }

        return !key.IsCtrl && !key.IsAlt;
    }

    /// <summary>Whether a key edits the text in the view: a typed character (see <see cref="IsTyping"/>), Enter, Backspace or Delete.</summary>
    /// <param name="key">The key.</param>
    internal static bool IsEditKey(Key key) => IsTyping(key) || key == Key.Enter || key == Key.Backspace || key == Key.Delete;

    /// <summary>Whether a mouse event places the cursor: a left press (or a drag from one), a left double-click, or a right-click.</summary>
    /// <param name="mouse">The event.</param>
    private static bool PlacesCursor(Mouse mouse) =>
        mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed)
        || mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked)
        || mouse.Flags.HasFlag(MouseFlags.RightButtonClicked);
}
