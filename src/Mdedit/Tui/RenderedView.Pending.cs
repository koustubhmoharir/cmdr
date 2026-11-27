using System.Text;
using Mdedit.Core.Editing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Mdedit.Tui;

/// <summary>
/// The text keys of the view, and the pending insertion it draws. Typed characters, Backspace and Delete are the view's edit keys; every
/// other key is a command. A pending insertion is the source text that <see cref="ViewEdit"/> holds at the caret because it would not show
/// (a space at a block's end). It is drawn at the caret, with the rest of the caret's line moved right, and the window gives it back to the
/// next insertion.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> The view drops the pending insertion when the cursor is placed or moves (a handled key, or a press, a double-click or a
/// right-click), when the document or the folds change, and when the window sets another one. The window drops it for a command that the
/// view does not handle, and Ctrl+Z drops it without an undo.
/// </para>
/// </remarks>
internal sealed partial class RenderedView
{
    /// <summary>The pending insertion held at the caret, or null when there is none.</summary>
    public ViewEditResult.Pending? Pending { get; private set; }

    /// <summary>
    /// Sets or drops the pending insertion. It is drawn at the cursor, and the cursor moves after it, until the view drops it (see the remarks
    /// of the class).
    /// </summary>
    /// <param name="pending">The pending insertion, or null to drop it.</param>
    public void SetPending(ViewEditResult.Pending? pending)
    {
        Pending = pending;
        SetNeedsDraw();
    }

    /// <summary>The number of columns the pending insertion takes: one per character of its source text.</summary>
    private int PendingWidth => Pending?.SourceText.Length ?? 0;

    /// <summary>
    /// Draws the pending insertion at a column of the caret's line: its text is written there and the rest of the line moves right, clipped at
    /// the edge, so no text is overwritten. Nothing is drawn for a column outside the line.
    /// </summary>
    /// <param name="texts">The text of each column of the line; a null column is the second half of a wide cell.</param>
    /// <param name="attributes">The attribute of each column.</param>
    /// <param name="column">The column of the caret.</param>
    /// <param name="attribute">The attribute of the pending text.</param>
    private void ShowPending(string?[] texts, Attribute[] attributes, int column, Attribute attribute)
    {
        if (Pending is not { } pending || column < 0 || column >= texts.Length)
        {
            return;
        }

        string text = pending.SourceText;
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

    /// <summary>Whether a key edits the text in the view: a typed character (see <see cref="IsTyping"/>), Backspace or Delete.</summary>
    /// <param name="key">The key.</param>
    internal static bool IsEditKey(Key key) => IsTyping(key) || key == Key.Backspace || key == Key.Delete;

    /// <summary>Whether a mouse event places the cursor: a left press (or a drag from one), a left double-click, or a right-click.</summary>
    /// <param name="mouse">The event.</param>
    private static bool PlacesCursor(Mouse mouse) =>
        mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed)
        || mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked)
        || mouse.Flags.HasFlag(MouseFlags.RightButtonClicked);
}
