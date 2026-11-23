using System.Reflection;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Shared.Tui;

/// <summary>
/// A multi-line text box for the applications: the editing of <c>Terminal.Gui.Editor.Editor</c> with the behaviour the applications
/// need from a text box. The control does not indent after Enter, and it does not use tabs as spaces. Enter and every paste write the
/// line breaks as <see cref="LineBreak"/>, whatever the line breaks of the pasted text are, and a terminal paste inserts the text the
/// terminal delivered rather than the clipboard.
/// <para>
/// The control does not handle the keys of the search and replace commands (Find, Find next, Find previous, Replace and Collapse),
/// so that those keys reach the application. Tab and Shift+Tab move the focus unless <see cref="TabInserts"/> is set; then Tab inserts
/// a tab character and Shift+Tab unindents. Ctrl+Z and Ctrl+Y undo and redo, as in the editor.
/// </para>
/// </summary>
public class TextArea : Terminal.Gui.Editor.Editor
{
    /// <summary>The line breaks that <see cref="LineBreak"/> may be set to.</summary>
    private static readonly string[] LineBreaks = ["\n", "\r\n", "\r"];

    /// <summary>The commands whose keys the control gives up, so that the keys reach the application.</summary>
    private static readonly Command[] SearchCommands =
    [
        Command.Find,
        Command.FindNext,
        Command.FindPrevious,
        Command.Replace,
        Command.Collapse,
    ];

    /// <summary>The full name of the command context payload that Terminal.Gui uses for bracketed paste. The type is internal to Terminal.Gui.</summary>
    private const string PastePayloadTypeName = "Terminal.Gui.Input.PastePayload";

    private string _lineBreak = "\n";
    private bool _tabInserts;

    /// <summary>
    /// Creates an empty text box. Auto-indent is off, tabs are kept as tab characters, the search keys are unbound, and Tab moves the focus.
    /// </summary>
    public TextArea()
    {
        IndentationStrategy = null;
        ConvertTabsToSpaces = false;

        // A tab is drawn to the next multiple of the indentation size; 4 is the editor's default, set here so the applications keep it.
        IndentationSize = 4;

        foreach (Command command in SearchCommands)
        {
            foreach (Key key in KeyBindings.GetAllFromCommands(command))
            {
                KeyBindings.Remove(key);
            }
        }

        AddCommand(Command.NewLine, InsertLineBreak);
        AddCommand(Command.Paste, Paste);
        ApplyTabBinding();
    }

    /// <summary>
    /// Whether Tab inserts a tab character. When false, Tab and Shift+Tab are not bound by the control, so they move the focus.
    /// When true, Tab inserts a tab character, and Shift+Tab still unindents. Can be changed at any time.
    /// </summary>
    public bool TabInserts
    {
        get => _tabInserts;
        set
        {
            _tabInserts = value;
            ApplyTabBinding();
        }
    }

    /// <summary>
    /// The text of a line break: <c>"\n"</c> (the default), <c>"\r\n"</c> or <c>"\r"</c>. Enter inserts it, and every paste writes each
    /// line break of the pasted text as it. Text already in the box keeps its line breaks.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">The value is not <c>"\n"</c>, <c>"\r\n"</c> or <c>"\r"</c>.</exception>
    public string LineBreak
    {
        get => _lineBreak;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!LineBreaks.Contains(value))
            {
                throw new ArgumentException("The line break must be \"\\n\", \"\\r\\n\" or \"\\r\".", nameof(value));
            }

            _lineBreak = value;
        }
    }

    /// <summary>
    /// Binds Tab to inserting a tab character and Shift+Tab to unindenting when <see cref="TabInserts"/> is set. Otherwise both keys are
    /// unbound, so that they reach the focus navigation of the application.
    /// </summary>
    private void ApplyTabBinding()
    {
        KeyBindings.Remove(Key.Tab);
        KeyBindings.Remove(Key.Tab.WithShift);
        if (TabInserts)
        {
            KeyBindings.Add(Key.Tab, Command.InsertTab);
            KeyBindings.Add(Key.Tab.WithShift, Command.Unindent);
        }
    }

    /// <summary>
    /// The Enter command: replaces the selection, if there is one, with <see cref="LineBreak"/>, as one undo step. Does nothing when the
    /// box is read-only, and the key is still handled so that it does not move the focus.
    /// </summary>
    /// <param name="ctx">The command context. Not used.</param>
    /// <returns>Always true.</returns>
    private bool? InsertLineBreak(ICommandContext? ctx)
    {
        if (!ReadOnly)
        {
            InsertText(LineBreak);
        }

        return true;
    }

    /// <summary>
    /// The Paste command, for both the paste keys and the terminal's bracketed paste. The text is the terminal's payload when the terminal
    /// delivered one, and the clipboard otherwise. The text is sanitized as the view base does, and then each line break is written as
    /// <see cref="LineBreak"/>. The insertion replaces the selection, as one undo step. The view base's <c>Pasting</c> and <c>Pasted</c>
    /// events are not raised: the base handler that raises them is private, and nothing in the applications listens to them.
    /// </summary>
    /// <param name="ctx">The command context, whose value is the terminal's payload for a bracketed paste.</param>
    /// <returns>
    /// False when there was nothing to paste; true otherwise, including when the box is read-only and nothing changed.
    /// </returns>
    private bool? Paste(ICommandContext? ctx)
    {
        if (!Enabled)
        {
            return false;
        }

        if (ReadOnly)
        {
            return true;
        }

        string? raw = TryGetPayloadText(ctx, out string? payload) ? payload : App?.Clipboard?.GetClipboardData();
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        string sanitized = OnSanitizingPaste(raw);
        if (string.IsNullOrEmpty(sanitized))
        {
            return false;
        }

        InsertText(TextUtilities.NormalizeNewLines(sanitized, LineBreak));
        return true;
    }

    /// <summary>
    /// Replaces the selection with the text, or inserts it at the caret when there is no selection. It is one undo step, and the caret
    /// is left after the text. Any extra carets are removed first, so that the text is inserted once.
    /// </summary>
    /// <param name="text">The text to insert.</param>
    private void InsertText(string text)
    {
        ClearAdditionalCarets();
        int start = HasSelection ? SelectionStart : CaretOffset;
        int length = HasSelection ? SelectionLength : 0;
        TextDocument document = Document ?? throw new InvalidOperationException("The text area has no document.");
        document.UndoStack.StartUndoGroup();
        try
        {
            ClearSelection();
            document.Replace(start, length, text);
        }
        finally
        {
            document.UndoStack.EndUndoGroup();
        }

        CaretOffset = start + text.Length;
    }

    /// <summary>
    /// Reads the text of the terminal's bracketed-paste payload from a command context. Terminal.Gui keeps the payload in an internal type,
    /// so it is read by reflection, by the type's full name and its <c>Text</c> property. Keyboard paste has no payload, and the caller uses
    /// the clipboard then. The paste tests fail if Terminal.Gui renames the type.
    /// </summary>
    /// <param name="ctx">The command context.</param>
    /// <param name="text">The payload's text when there is a payload; otherwise null.</param>
    /// <returns>True when the context carries a bracketed-paste payload.</returns>
    private static bool TryGetPayloadText(ICommandContext? ctx, out string? text)
    {
        object? value = ctx?.Value;
        if (value is null || value.GetType().FullName != PastePayloadTypeName)
        {
            text = null;
            return false;
        }

        PropertyInfo? property = value.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
        text = property?.GetValue(value) as string;
        return true;
    }
}
