using Cfgedit.Core;
using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;

namespace Cfgedit.Tui;

/// <summary>What the user answers when the file changed on disk before a save.</summary>
public enum ExternalChangeChoice
{
    /// <summary>Write the document over the changed file.</summary>
    Overwrite,

    /// <summary>Discard the document and load the file from disk.</summary>
    Reload,

    /// <summary>Do nothing.</summary>
    Cancel,
}

/// <summary>What the user answers when a file with unsaved changes is about to be closed or replaced.</summary>
public enum UnsavedChoice
{
    /// <summary>Save first, then close.</summary>
    Save,

    /// <summary>Close without saving.</summary>
    Discard,

    /// <summary>Stay open.</summary>
    Cancel,
}

/// <summary>How a save flow ended.</summary>
public enum SaveOutcome
{
    /// <summary>The document was written.</summary>
    Saved,

    /// <summary>The user cancelled, so nothing was written.</summary>
    Cancelled,

    /// <summary>The user chose to reload, so the document was replaced by the file on disk.</summary>
    Reloaded,

    /// <summary>The save failed; the error says why.</summary>
    Failed,
}

/// <summary>The result of a save flow.</summary>
/// <param name="Outcome">How the flow ended.</param>
/// <param name="Error">The error when the save failed, otherwise null.</param>
public sealed record SaveResult(SaveOutcome Outcome, string? Error);

/// <summary>How a quit or switch flow ended.</summary>
public enum CloseOutcome
{
    /// <summary>The file may be closed.</summary>
    Closed,

    /// <summary>The user chose to stay open.</summary>
    Cancelled,

    /// <summary>The user chose to reload during the save, so the file stays open with the reloaded content.</summary>
    Reloaded,

    /// <summary>The save failed, so the file stays open; the error says why.</summary>
    Failed,
}

/// <summary>The result of a quit or switch flow.</summary>
/// <param name="Outcome">How the flow ended.</param>
/// <param name="Error">The error when the save failed, otherwise null.</param>
public sealed record CloseResult(CloseOutcome Outcome, string? Error);

/// <summary>How a check for a change on disk ended.</summary>
public enum ExternalOutcome
{
    /// <summary>Nothing changed on disk since the last check.</summary>
    None,

    /// <summary>The file changed and the document was reloaded from it. The reload is an undo step.</summary>
    Reloaded,

    /// <summary>The file changed and the user kept the document. The file's content on disk is accepted, so the next save does not ask.</summary>
    KeptMine,

    /// <summary>The file was deleted and the user kept the document, which is now an unsaved new file.</summary>
    KeptDeleted,

    /// <summary>The file was deleted and the user chose to close it. Nothing else changed.</summary>
    Close,

    /// <summary>The check or the reload failed; the error says why. Reported once for each state of the file.</summary>
    Failed,
}

/// <summary>The result of a check for a change on disk.</summary>
/// <param name="Outcome">How the check ended.</param>
/// <param name="Quiet">
/// For <see cref="ExternalOutcome.Reloaded"/>: true when the undo history was empty before the reload, so there is nothing to say; otherwise
/// the UI shows <see cref="EditController.Message"/>.
/// </param>
/// <param name="DiscardedText">True when the reload replaced text the user typed in text mode, which has no undo.</param>
/// <param name="Focus">For <see cref="ExternalOutcome.Reloaded"/>: the path to focus, which is the path given when it still resolves, otherwise null.</param>
/// <param name="Error">The error when the check failed, otherwise null.</param>
public sealed record ExternalResult(ExternalOutcome Outcome, bool Quiet, bool DiscardedText, NodePath? Focus, string? Error);

/// <summary>
/// The state of one open file, with no UI types: its <see cref="DocumentFile"/>, its edit history, the text mode, and the last message.
/// The file is in tree mode, where edits go through the history, or in text mode, where the text is the view's text and edits are
/// typed into it. A file that does not parse starts in text mode, and a file in text mode becomes a tree again when its text parses.
/// The UI calls these methods and shows the results. Each confirmation is a delegate, so the flows can be tested without dialogs. The
/// default button of each confirmation is the action the user started, and the UI sets it.
/// </summary>
/// <remarks>
/// Dirty means the current text's encoded bytes differ from the bytes last loaded or saved, in either mode. The history is kept for
/// the tree; a text-mode edit that parses is recorded as one step with <see cref="EditHistory.RecordExternal"/>.
/// </remarks>
public sealed class EditController
{
    // Null until the file first parses. In tree mode it is never null.
    private EditHistory? _history;

    // The text in text mode, as the view holds it, with the cursor's offset in it. Null in tree mode.
    private SourceText? _text;
    private int _cursorOffset;

    // The file's line break. The text view inserts it on Enter and on a paste, so the text it adds has the file's own line breaks.
    private string _newline = Environment.NewLine;

    /// <summary>Starts the state for a file. A file that parses starts in tree mode; one that does not starts in text mode with its text.</summary>
    /// <param name="file">The file, with the document parsed from it, or with its text and parse error.</param>
    public EditController(DocumentFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        File = file;
        if (file.Document is { } document)
        {
            _history = new EditHistory(document);
            return;
        }

        _text = file.Text;
        _newline = file.Text.Newline;
    }

    /// <summary>The file this state belongs to.</summary>
    public DocumentFile File { get; }

    /// <summary>
    /// The mode: <see cref="EditMode.Tree"/> while the document is shown, <see cref="EditMode.Text"/> while the text is.
    /// </summary>
    public EditMode Mode => _text is null ? EditMode.Tree : EditMode.Text;

    /// <summary>
    /// The current document: the file's document with every edit applied since it was loaded or saved. In text mode it is the last
    /// document that parsed, which is null for a file that has never parsed (so this throws then).
    /// </summary>
    /// <exception cref="InvalidOperationException">The file has never parsed.</exception>
    public ConfigDocument Document => History.Current;

    /// <summary>In text mode, the text as the view holds it, in the file's encoding and with the file's line breaks. Null in tree mode.</summary>
    public SourceText? Text => _text;

    /// <summary>In text mode, the cursor's offset in <see cref="Text"/>. Zero in tree mode.</summary>
    public int CursorOffset => _cursorOffset;

    /// <summary>The file's line break. The text view inserts it on Enter and on a paste.</summary>
    public string Newline => _newline;

    /// <summary>True when an edit can be undone. Undo is a tree-mode operation.</summary>
    public bool CanUndo => Mode == EditMode.Tree && History.CanUndo;

    /// <summary>True when an undone edit can be redone. Redo is a tree-mode operation.</summary>
    public bool CanRedo => Mode == EditMode.Tree && History.CanRedo;

    /// <summary>
    /// True when the current text differs from the bytes last loaded or saved, or when the file has never been saved. A new file is
    /// dirty until its first save. A text that cannot be encoded is dirty.
    /// </summary>
    public bool IsDirty
    {
        get
        {
            if (!File.Exists)
            {
                return true;
            }

            try
            {
                return !CurrentSource.Encode().AsSpan().SequenceEqual(File.Text.Encode());
            }
            catch (CfgeditException)
            {
                return true;
            }
        }
    }

    /// <summary>The last error or information message, such as "Saved" or "Nothing to undo". Null when there is none.</summary>
    public string? Message { get; private set; }

    private SourceText CurrentSource => _text ?? History.Current.Source;

    private EditHistory History => _history ?? throw new InvalidOperationException("The file has never parsed, so there is no document.");

    /// <summary>Applies an edit to the current document, as one undo step. Refused in text mode.</summary>
    /// <param name="op">The edit.</param>
    /// <param name="error">The error when the edit is refused, otherwise null. The document is then unchanged.</param>
    /// <returns>The result of the edit, or null when it was refused.</returns>
    public EditResult? Apply(EditOp op, out string? error)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (Mode == EditMode.Text)
        {
            error = "the document cannot be edited in text mode";
            Message = error;
            return null;
        }

        try
        {
            EditResult result = History.Apply(op);
            error = null;
            Message = null;
            return result;
        }
        catch (CfgeditException ex)
        {
            error = ex.Message;
            Message = ex.Message;
            return null;
        }
    }

    /// <summary>Undoes the last edit.</summary>
    /// <returns>The path to focus after the undo, or null when there is nothing to undo (the message then says so).</returns>
    public NodePath? Undo()
    {
        if (!CanUndo)
        {
            Message = "Nothing to undo";
            return null;
        }

        Message = null;
        return History.Undo();
    }

    /// <summary>Redoes the last undone edit.</summary>
    /// <returns>The path to focus after the redo, or null when there is nothing to redo (the message then says so).</returns>
    public NodePath? Redo()
    {
        if (!CanRedo)
        {
            Message = "Nothing to redo";
            return null;
        }

        Message = null;
        return History.Redo();
    }

    /// <summary>
    /// Switches to text mode with the document's own text, with the cursor at an offset in it. Does nothing in text mode, and needs a
    /// document otherwise (a file in text mode has one only when it parsed before).
    /// </summary>
    /// <param name="cursorOffset">The cursor's offset in the document's text.</param>
    public void EnterText(int cursorOffset)
    {
        if (Mode == EditMode.Text)
        {
            return;
        }

        _text = History.Current.Source;
        _newline = _text.Newline;
        _cursorOffset = cursorOffset;
        Message = null;
    }

    /// <summary>
    /// In text mode, takes the view's text and caret. The text is the view's text verbatim, with its line breaks as they are, and the
    /// caret is an offset in it. Does nothing in tree mode.
    /// </summary>
    /// <param name="viewText">The view's text.</param>
    /// <param name="caretOffset">The view's caret, as an offset in <paramref name="viewText"/>.</param>
    public void SetText(string viewText, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(viewText);
        if (_text is null)
        {
            return;
        }

        _text = _text.WithText(viewText);
        _cursorOffset = caretOffset;
    }

    /// <summary>
    /// Parses the text, in text mode, and on success switches to tree mode. The edit is one undo step of the history, or the history
    /// starts from the new document when the file had never parsed. A text that does not parse keeps the text mode, and the error says
    /// where. Does nothing in tree mode.
    /// </summary>
    /// <param name="error">The parse error when the text does not parse, otherwise null.</param>
    /// <returns>The node to focus in the tree after a successful parse (null for the document itself), otherwise null.</returns>
    public NodePath? Reparse(out CfgeditException? error)
    {
        if (_text is null)
        {
            error = null;
            return null;
        }

        ConfigDocument document;
        try
        {
            document = File.ParseText(_text);
        }
        catch (CfgeditException ex)
        {
            error = ex;
            Message = Describe(ex);
            return null;
        }

        error = null;
        NodePath? focus = FocusAt(document, _cursorOffset);
        if (_history is null)
        {
            _history = new EditHistory(document);
        }
        else
        {
            _history.RecordExternal(document, focusBefore: null, focusAfter: focus);
        }

        _text = null;
        Message = null;
        return focus;
    }

    /// <summary>Writes the current document to the file, without checking for an external change. Tree mode only.</summary>
    /// <returns>The error when the save failed, otherwise null.</returns>
    public string? Save()
    {
        if (Mode == EditMode.Text)
        {
            return "the document cannot be saved in text mode without reparsing";
        }

        try
        {
            File.Save(History.Current);
            History.MarkSaved();
            Message = "Saved";
            return null;
        }
        catch (CfgeditException ex)
        {
            Message = ex.Message;
            return ex.Message;
        }
    }

    /// <summary>
    /// Writes the text to the file, as it is, whether or not it parses, without checking for an external change. Text mode only. Afterwards
    /// <see cref="DocumentFile.ParseError"/> says how the saved text parses.
    /// </summary>
    /// <returns>The error when the save failed, otherwise null.</returns>
    public string? SaveText()
    {
        if (_text is null)
        {
            return "the text cannot be saved in tree mode";
        }

        try
        {
            File.SaveText(_text);
            Message = "Saved";
            return null;
        }
        catch (CfgeditException ex)
        {
            Message = ex.Message;
            return ex.Message;
        }
    }

    /// <summary>
    /// Replaces the document with the file on disk. The undo and redo history is cleared, and the document is clean. A file whose text
    /// does not parse comes back in text mode, as it would when opened.
    /// </summary>
    /// <returns>The error when the file cannot be read, otherwise null.</returns>
    public string? Reload()
    {
        try
        {
            ConfigDocument? document = File.Reload();
            if (document is null)
            {
                _history = null;
                _text = File.Text;
                _newline = _text.Newline;
                _cursorOffset = 0;
            }
            else
            {
                if (_history is null)
                {
                    _history = new EditHistory(document);
                }
                else
                {
                    _history.Reset(document);
                }

                _text = null;
            }

            Message = "Reloaded";
            return null;
        }
        catch (CfgeditException ex)
        {
            Message = ex.Message;
            return ex.Message;
        }
    }

    /// <summary>
    /// Checks the file for a change on disk, as the UI's timer does. A change to a document with no unsaved edits reloads without asking.
    /// A change to a dirty document asks <paramref name="askReloadOverMine"/>: Reload replaces the document with the file (as an undo step, so
    /// the edits are one Ctrl+Z away), and Keep mine leaves the document as it is and accepts the disk's content, so the next save does not
    /// ask again for it. A deleted file asks <paramref name="askKeepDeleted"/>: Keep makes the document an unsaved new file, and Close
    /// changes nothing. A failed read is reported once for each state of the file.
    /// </summary>
    /// <param name="focus">The path the UI has focused, which is focused again after a reload when it still resolves.</param>
    /// <param name="askReloadOverMine">
    /// Asks Reload (true) or Keep mine (false). Its argument says whether the reload would discard text typed in text mode. Its default
    /// button is Keep mine, and the UI sets it.
    /// </param>
    /// <param name="askKeepDeleted">Asks Keep (true) or Close (false), when the file was deleted. Its default button is Keep.</param>
    public ExternalResult CheckExternal(NodePath? focus, Func<bool, bool> askReloadOverMine, Func<bool> askKeepDeleted)
    {
        ArgumentNullException.ThrowIfNull(askReloadOverMine);
        ArgumentNullException.ThrowIfNull(askKeepDeleted);

        DiskChange change;
        try
        {
            change = File.PollDisk();
        }
        catch (CfgeditException ex)
        {
            Message = ex.Message;
            return new ExternalResult(ExternalOutcome.Failed, Quiet: false, DiscardedText: false, Focus: null, ex.Message);
        }

        switch (change)
        {
            case DiskChange.Deleted:
                return askKeepDeleted() ? KeepDeleted() : new ExternalResult(ExternalOutcome.Close, Quiet: false, DiscardedText: false, null, null);

            case DiskChange.Changed:
                bool dirty = IsDirty;
                bool discardsText = dirty && Mode == EditMode.Text;
                if (dirty && !askReloadOverMine(discardsText))
                {
                    return KeepMine();
                }

                return ReloadFromDisk(focus, discardsText);

            default:
                return new ExternalResult(ExternalOutcome.None, Quiet: false, DiscardedText: false, Focus: null, Error: null);
        }
    }

    /// <summary>
    /// Replaces the document with the file's new text, after a change on disk. In tree mode the reload is recorded as one undo step, so
    /// Undo goes back to the edited document, which is then dirty. In text mode a text that parses goes to tree mode, and one that does
    /// not stays in text mode with the new text; the history is kept either way.
    /// </summary>
    private ExternalResult ReloadFromDisk(NodePath? focus, bool discardsText)
    {
        bool hadHistory = _history?.CanUndo ?? false;
        ConfigDocument? document;
        try
        {
            document = File.Reload();
        }
        catch (CfgeditException ex)
        {
            Message = ex.Message;
            return new ExternalResult(ExternalOutcome.Failed, Quiet: false, DiscardedText: false, Focus: null, ex.Message);
        }

        NodePath? restored = null;
        if (document is null)
        {
            _text = File.Text;
            _newline = _text.Newline;
            _cursorOffset = 0;
        }
        else
        {
            restored = focus is not null && document.Resolve(focus) is not null ? focus : null;
            if (_history is null)
            {
                _history = new EditHistory(document);
            }
            else
            {
                _history.RecordExternal(document, focusBefore: focus, focusAfter: restored);
            }

            _text = null;
            _cursorOffset = 0;
        }

        Message = hadHistory ? "Reloaded: the file changed on disk" : null;
        return new ExternalResult(ExternalOutcome.Reloaded, Quiet: !hadHistory, discardsText, restored, Error: null);
    }

    /// <summary>Keeps the document over a changed file. The file's content on disk is accepted, and the document is left as it is.</summary>
    private ExternalResult KeepMine()
    {
        try
        {
            File.AcceptDiskState();
        }
        catch (CfgeditException ex)
        {
            Message = ex.Message;
            return new ExternalResult(ExternalOutcome.Failed, Quiet: false, DiscardedText: false, Focus: null, ex.Message);
        }

        return new ExternalResult(ExternalOutcome.KeptMine, Quiet: false, DiscardedText: false, Focus: null, Error: null);
    }

    /// <summary>Keeps the document after its file was deleted. The document becomes an unsaved new file, so the next save creates the file.</summary>
    private ExternalResult KeepDeleted()
    {
        File.MarkMissing();
        Message = "The file was deleted on disk";
        return new ExternalResult(ExternalOutcome.KeptDeleted, Quiet: false, DiscardedText: false, Focus: null, Error: null);
    }

    /// <summary>
    /// Saves the document, after checking for a change on disk. When the file changed since it was loaded or last saved, the user chooses:
    /// Overwrite saves over it; Reload replaces the document with the file (after <paramref name="confirmReload"/> agrees, since the
    /// undo history is lost); Cancel does nothing. Tree mode.
    /// </summary>
    /// <param name="askExternalChange">Asks Overwrite, Reload or Cancel. Only called when the file changed on disk.</param>
    /// <param name="confirmReload">Asks whether the reload may discard the undo history. Only called after Reload is chosen.</param>
    public SaveResult SaveFlow(Func<ExternalChangeChoice> askExternalChange, Func<bool> confirmReload)
    {
        return SaveWith(askExternalChange, confirmReload, Save);
    }

    /// <summary>
    /// Saves the text, in text mode. The text is reparsed first (as <see cref="Reparse"/>, which switches to tree mode on success, and then
    /// the document is saved). When it does not parse, <paramref name="confirmUnparsed"/> is asked with the parse error: Save writes the text
    /// as it is, and Cancel does nothing. The external-change check of <see cref="SaveFlow"/> applies to both.
    /// </summary>
    /// <param name="askExternalChange">Passed to the save check. Only called when the file changed on disk.</param>
    /// <param name="confirmReload">Passed to the save check.</param>
    /// <param name="confirmUnparsed">Asks whether to save text that does not parse. Its default button is Save.</param>
    public SaveResult TextSaveFlow(
        Func<ExternalChangeChoice> askExternalChange,
        Func<bool> confirmReload,
        Func<CfgeditException, bool> confirmUnparsed)
    {
        ArgumentNullException.ThrowIfNull(askExternalChange);
        ArgumentNullException.ThrowIfNull(confirmReload);
        ArgumentNullException.ThrowIfNull(confirmUnparsed);

        Reparse(out CfgeditException? error);
        if (error is null)
        {
            return SaveFlow(askExternalChange, confirmReload);
        }

        if (!confirmUnparsed(error))
        {
            return new SaveResult(SaveOutcome.Cancelled, null);
        }

        return SaveWith(askExternalChange, confirmReload, SaveText);
    }

    /// <summary>
    /// Closes the file, or switches to another one, when it is dirty: the user chooses Save, Discard or Cancel. A clean file closes
    /// without asking. Save goes through <see cref="SaveFlow"/>, or <see cref="TextSaveFlow"/> in text mode, so it checks for an external
    /// change too. A failed save keeps the file open, with the error in <see cref="CloseResult.Error"/>.
    /// </summary>
    /// <param name="askUnsaved">Asks Save, Discard or Cancel. Only called when the file is dirty.</param>
    /// <param name="askExternalChange">Passed to the save flow.</param>
    /// <param name="confirmReload">Passed to the save flow.</param>
    /// <param name="confirmUnparsed">
    /// Passed to <see cref="TextSaveFlow"/> in text mode. The default refuses (Cancel), so text that does not parse is never saved
    /// without the user agreeing.
    /// </param>
    public CloseResult CloseFlow(
        Func<UnsavedChoice> askUnsaved,
        Func<ExternalChangeChoice> askExternalChange,
        Func<bool> confirmReload,
        Func<CfgeditException, bool>? confirmUnparsed = null)
    {
        ArgumentNullException.ThrowIfNull(askUnsaved);
        if (!IsDirty)
        {
            return new CloseResult(CloseOutcome.Closed, null);
        }

        switch (askUnsaved())
        {
            case UnsavedChoice.Discard:
                return new CloseResult(CloseOutcome.Closed, null);
            case UnsavedChoice.Save:
                SaveResult save = Mode == EditMode.Tree
                    ? SaveFlow(askExternalChange, confirmReload)
                    : TextSaveFlow(askExternalChange, confirmReload, confirmUnparsed ?? (_ => false));
                return save.Outcome switch
                {
                    SaveOutcome.Saved => new CloseResult(CloseOutcome.Closed, null),
                    SaveOutcome.Reloaded => new CloseResult(CloseOutcome.Reloaded, null),
                    SaveOutcome.Failed => new CloseResult(CloseOutcome.Failed, save.Error),
                    _ => new CloseResult(CloseOutcome.Cancelled, null),
                };
            default:
                return new CloseResult(CloseOutcome.Cancelled, null);
        }
    }

    private SaveResult SaveWith(Func<ExternalChangeChoice> askExternalChange, Func<bool> confirmReload, Func<string?> write)
    {
        ArgumentNullException.ThrowIfNull(askExternalChange);
        ArgumentNullException.ThrowIfNull(confirmReload);

        bool changed;
        try
        {
            changed = File.ChangedOnDisk();
        }
        catch (CfgeditException ex)
        {
            Message = ex.Message;
            return new SaveResult(SaveOutcome.Failed, ex.Message);
        }

        if (changed)
        {
            ExternalChangeChoice choice = askExternalChange();
            if (choice == ExternalChangeChoice.Cancel)
            {
                return new SaveResult(SaveOutcome.Cancelled, null);
            }

            if (choice == ExternalChangeChoice.Reload)
            {
                if (!confirmReload())
                {
                    return new SaveResult(SaveOutcome.Cancelled, null);
                }

                string? reloadError = Reload();
                return reloadError is null
                    ? new SaveResult(SaveOutcome.Reloaded, null)
                    : new SaveResult(SaveOutcome.Failed, reloadError);
            }
        }

        string? error = write();
        return error is null ? new SaveResult(SaveOutcome.Saved, null) : new SaveResult(SaveOutcome.Failed, error);
    }

    /// <summary>The message of a parse error: <c>line:col: message</c> when it has a position, else the message.</summary>
    internal static string Describe(CfgeditException ex)
    {
        return ex.Line is int line
            ? $"{line}:{ex.Column ?? 1}: {ex.Message}"
            : ex.Message;
    }

    /// <summary>The deepest node whose span contains an offset, or null for the document itself.</summary>
    private static NodePath? FocusAt(ConfigDocument document, int offset)
    {
        NodePath? focus = null;
        DocNode node = document.Root;
        while (true)
        {
            DocNode? next = node.Children.FirstOrDefault(child => child.Start <= offset && offset < child.End);
            if (next is null)
            {
                return focus;
            }

            focus = next.Path;
            node = next;
        }
    }
}

/// <summary>Whether the file is shown as its tree, or as its text.</summary>
public enum EditMode
{
    /// <summary>The document is shown as a tree, and edits go through the history.</summary>
    Tree,

    /// <summary>The text is shown and typed into. The document is parsed from it on demand.</summary>
    Text,
}
