using Shared.Tui;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Editor.Highlighting;
using Terminal.Gui.Input;

namespace Mdedit.Tui;

/// <summary>
/// The source editor of edit mode, and the one copy of the document's text: its <see cref="Editor.Document"/> is the source buffer that
/// both modes show. It is a <see cref="TextArea"/> with Markdown highlighting and word wrap on, and Alt+Z toggles the wrap. Its undo
/// history is the buffer's, so <see cref="Undo"/> undoes and redoes in either mode.
/// </summary>
/// <remarks>
/// <para>
/// <b>Version.</b> <see cref="Version"/> counts the document's changes, undo and redo included. The window compares it with the version
/// its rendered view was built from, so a render happens only when the text moved.
/// </para>
/// <para>
/// <b>Highlighting.</b> The built-in definition is <see cref="HighlightingName"/> (the <c>MarkDown-Mode</c> grammar), found through the
/// default <see cref="HighlightingManager"/>. A missing definition is a broken installation and fails the editor's construction.
/// </para>
/// </remarks>
internal sealed class SourceEditor : TextArea
{
    /// <summary>The name of the built-in Markdown highlighting definition, as <see cref="HighlightingManager"/> registers it.</summary>
    internal const string HighlightingName = "MarkDown";

    /// <summary>The extension whose definition is used when the name is not found.</summary>
    internal const string HighlightingExtension = ".md";

    /// <summary>The key that toggles word wrap.</summary>
    internal static readonly Key WrapKey = Key.Z.WithAlt;

    private int _version;

    /// <summary>
    /// Creates an empty editor with Tab inserting tabs, word wrap on, and the Markdown highlighting.
    /// </summary>
    /// <exception cref="InvalidOperationException">The built-in Markdown highlighting definition cannot be found.</exception>
    public SourceEditor()
    {
        TabInserts = true;
        WordWrap = true;
        HighlightingDefinition = HighlightingManager.Instance.GetDefinition(HighlightingName)
            ?? HighlightingManager.Instance.GetDefinitionByExtension(HighlightingExtension)
            ?? throw new InvalidOperationException($"The highlighting definition '{HighlightingName}' is not available.");
        Buffer.TextChanged += (_, _) =>
        {
            _version++;
            BufferChanged?.Invoke();
        };
    }

    /// <summary>The source buffer: the editor's document, which holds the text of both modes. It is never null once the editor is constructed.</summary>
    public TextDocument Buffer => Document ?? throw new InvalidOperationException("The editor has no document.");

    /// <summary>Raised after each change of the document, undo and redo included, so that the window can update the title.</summary>
    public event Action? BufferChanged;

    /// <summary>Raised when a key reaches the editor, before it is handled, so that the window can clear a transient message.</summary>
    public event Action? KeyPressed;

    /// <summary>The number of changes the document has had. It only increases.</summary>
    public int Version => _version;

    /// <summary>
    /// The offset that the selection starts from, the end that is not the caret; null when nothing is selected. It is the anchor of a
    /// selection that the view shows as the same range.
    /// </summary>
    public int? SelectionAnchor => HasSelection ? CaretOffset == SelectionEnd ? SelectionStart : SelectionEnd : null;

    /// <summary>
    /// Places the caret at an offset, with the selection from an anchor to it when the anchor is another offset (else no selection).
    /// Offsets are clamped to the text. The editor's caret setter clears a selection and its selection API sets the caret at the end of a
    /// range, so a selection that runs backward (the caret before the anchor) is placed with the caret at its end, which is the editor's
    /// only way to select a range; the anchor is then the selection's start.
    /// </summary>
    /// <param name="offset">The caret's offset.</param>
    /// <param name="anchor">The offset the selection starts from, or null for none.</param>
    public void PlaceCaret(int offset, int? anchor)
    {
        int length = Buffer.TextLength;
        int caret = Math.Clamp(offset, 0, length);
        if (anchor is { } from && Math.Clamp(from, 0, length) is int start && start != caret)
        {
            SelectRange(Math.Min(start, caret), Math.Abs(start - caret));
            return;
        }

        ClearSelection();
        CaretOffset = caret;
    }

    /// <summary>
    /// Handles Alt+Z, which toggles word wrap; every other key is the text area's. Returns true when the key was handled.
    /// </summary>
    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        KeyPressed?.Invoke();
        if (key == WrapKey)
        {
            WordWrap = !WordWrap;
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>
    /// Undoes or redoes one step of the document's history and reports where the change was. The position is the offset just after the
    /// first changed text (the end of what was restored, or the place of what was removed).
    /// </summary>
    /// <param name="redo">True to redo, false to undo.</param>
    /// <returns>The caret offset for the change, or null when there was nothing to undo or redo.</returns>
    public int? Undo(bool redo)
    {
        TextDocument buffer = Buffer;
        int? caret = null;
        void Capture(object? sender, TextChangeEventArgs args) => caret ??= args.Offset + args.InsertionLength;

        ((IDocument)buffer).TextChanged += Capture;
        try
        {
            if (redo)
            {
                if (buffer.UndoStack.CanRedo)
                {
                    buffer.UndoStack.Redo();
                }
            }
            else if (buffer.UndoStack.CanUndo)
            {
                buffer.UndoStack.Undo();
            }
        }
        finally
        {
            ((IDocument)buffer).TextChanged -= Capture;
        }

        return caret;
    }
}
