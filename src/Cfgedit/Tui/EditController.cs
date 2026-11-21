using Cfgedit.Core;
using Cfgedit.Core.Files;
using Cfgedit.Core.Model;

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

/// <summary>
/// The state of one open file, with no UI types: its <see cref="DocumentFile"/>, its edit history, and the last message. The UI calls
/// these methods and shows the results. Each confirmation is a delegate, so the flows can be tested without dialogs. The default
/// button of each confirmation is the action the user started, and the UI sets it.
/// </summary>
public sealed class EditController
{
    private readonly EditHistory _history;

    /// <summary>Starts the state for a file. The history starts at the file's document.</summary>
    /// <param name="file">The file, with the document loaded from it or created for it.</param>
    public EditController(DocumentFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        File = file;
        _history = new EditHistory(file.Document);
    }

    /// <summary>The file this state belongs to.</summary>
    public DocumentFile File { get; }

    /// <summary>The current document: the file's document with every edit applied since it was loaded or saved.</summary>
    public ConfigDocument Document => _history.Current;

    /// <summary>True when an edit can be undone.</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>True when an undone edit can be redone.</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>
    /// True when the document differs from the saved text, or when the file has never been saved. A new file is dirty until its
    /// first save.
    /// </summary>
    public bool IsDirty => !File.Exists || _history.IsDirty;

    /// <summary>The last error or information message, such as "Saved" or "Nothing to undo". Null when there is none.</summary>
    public string? Message { get; private set; }

    /// <summary>Applies an edit to the current document, as one undo step.</summary>
    /// <param name="op">The edit.</param>
    /// <param name="error">The error when the edit is refused, otherwise null. The document is then unchanged.</param>
    /// <returns>The result of the edit, or null when it was refused.</returns>
    public EditResult? Apply(EditOp op, out string? error)
    {
        ArgumentNullException.ThrowIfNull(op);
        try
        {
            EditResult result = _history.Apply(op);
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
        if (!_history.CanUndo)
        {
            Message = "Nothing to undo";
            return null;
        }

        Message = null;
        return _history.Undo();
    }

    /// <summary>Redoes the last undone edit.</summary>
    /// <returns>The path to focus after the redo, or null when there is nothing to redo (the message then says so).</returns>
    public NodePath? Redo()
    {
        if (!_history.CanRedo)
        {
            Message = "Nothing to redo";
            return null;
        }

        Message = null;
        return _history.Redo();
    }

    /// <summary>Writes the current document to the file, without checking for an external change.</summary>
    /// <returns>The error when the save failed, otherwise null.</returns>
    public string? Save()
    {
        try
        {
            File.Save(_history.Current);
            _history.MarkSaved();
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
    /// Replaces the document with the file on disk. The undo and redo history is cleared, and the document is clean.
    /// </summary>
    /// <returns>The error when the file cannot be read, otherwise null.</returns>
    public string? Reload()
    {
        try
        {
            _history.Reset(File.Reload());
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
    /// Saves, after checking for a change on disk. When the file changed since it was loaded or last saved, the user chooses:
    /// Overwrite saves over it; Reload replaces the document with the file (after <paramref name="confirmReload"/> agrees, since the
    /// undo history is lost); Cancel does nothing.
    /// </summary>
    /// <param name="askExternalChange">Asks Overwrite, Reload or Cancel. Only called when the file changed on disk.</param>
    /// <param name="confirmReload">Asks whether the reload may discard the undo history. Only called after Reload is chosen.</param>
    public SaveResult SaveFlow(Func<ExternalChangeChoice> askExternalChange, Func<bool> confirmReload)
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

        string? error = Save();
        return error is null ? new SaveResult(SaveOutcome.Saved, null) : new SaveResult(SaveOutcome.Failed, error);
    }

    /// <summary>
    /// Closes the file, or switches to another one, when it is dirty: the user chooses Save, Discard or Cancel. A clean file closes
    /// without asking. Save goes through <see cref="SaveFlow"/>, so it checks for an external change too. A failed save keeps the file
    /// open, with the error in <see cref="CloseResult.Error"/>.
    /// </summary>
    /// <param name="askUnsaved">Asks Save, Discard or Cancel. Only called when the file is dirty.</param>
    /// <param name="askExternalChange">Passed to <see cref="SaveFlow"/>.</param>
    /// <param name="confirmReload">Passed to <see cref="SaveFlow"/>.</param>
    public CloseResult CloseFlow(Func<UnsavedChoice> askUnsaved, Func<ExternalChangeChoice> askExternalChange, Func<bool> confirmReload)
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
                SaveResult save = SaveFlow(askExternalChange, confirmReload);
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
}
