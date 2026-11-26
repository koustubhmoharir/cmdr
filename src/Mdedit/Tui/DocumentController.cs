using Shared.Core;

namespace Mdedit.Tui;

/// <summary>
/// The file decisions of the viewer and the editor, with no UI types: the save over a file that changed on disk, the unsaved-changes flow of a
/// quit or a switch, and the check for a change on disk. The window shows the questions and does the buffer's work; the answers come in as
/// delegates, so the flows are tested without dialogs. The default answer of each question is the action the user started, and the window sets it.
/// </summary>
/// <remarks>
/// <b>Dirty.</b> The document is dirty when its text differs from the text the file was last read or written with, and also while the file is not on
/// disk: a new file is dirty until its first save, as a document kept after its file was deleted is. The window passes the buffer's text in.
/// </remarks>
internal sealed class DocumentController
{
    /// <summary>Creates the controller of a file.</summary>
    /// <param name="file">The file, opened or new.</param>
    /// <exception cref="ArgumentNullException">The file is <c>null</c>.</exception>
    public DocumentController(TextFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        File = file;
    }

    /// <summary>The file this controller decides for.</summary>
    public TextFile File { get; }

    /// <summary>
    /// True when the file is not on disk, or when <paramref name="text"/> differs from the text the file was last read or written with.
    /// </summary>
    /// <param name="text">The document's current text.</param>
    public bool IsDirty(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return !File.Exists || text != File.Text.Text;
    }

    /// <summary>
    /// Saves the text, after checking for a change on disk. When the file changed since it was read or last written, the user chooses:
    /// Overwrite writes the text over it; Reload reads the file again (the window then replaces the text with the file's, as an undo step);
    /// Cancel does nothing.
    /// </summary>
    /// <param name="text">The document's current text, which is written with the file's encoding and line breaks.</param>
    /// <param name="askExternalChange">Asks Overwrite, Reload or Cancel. Only called when the file changed on disk.</param>
    /// <returns>How the save ended. A failure has its error, and the file is unchanged.</returns>
    public SaveResult SaveFlow(string text, Func<ExternalChangeChoice> askExternalChange)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(askExternalChange);

        try
        {
            if (File.ChangedOnDisk())
            {
                switch (askExternalChange())
                {
                    case ExternalChangeChoice.Cancel:
                        return new SaveResult(SaveOutcome.Cancelled, null);
                    case ExternalChangeChoice.Reload:
                        File.Reload();
                        return new SaveResult(SaveOutcome.Reloaded, null);
                }
            }

            File.Save(File.Text.WithText(text));
            return new SaveResult(SaveOutcome.Saved, null);
        }
        catch (TextFileException ex)
        {
            return new SaveResult(SaveOutcome.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Leaves the document, or switches to another file: a clean document is left without asking. A dirty one asks Save, Discard or Cancel;
    /// Save goes through <see cref="SaveFlow"/>, so it checks for a change on disk too, and a failed save keeps the document open.
    /// </summary>
    /// <param name="text">The document's current text.</param>
    /// <param name="askUnsaved">Asks Save, Discard or Cancel. Only called when the document is dirty.</param>
    /// <param name="askExternalChange">Passed to the save flow.</param>
    public CloseResult CloseFlow(string text, Func<UnsavedChoice> askUnsaved, Func<ExternalChangeChoice> askExternalChange)
    {
        ArgumentNullException.ThrowIfNull(askUnsaved);
        if (!IsDirty(text))
        {
            return new CloseResult(CloseOutcome.Closed, null);
        }

        switch (askUnsaved())
        {
            case UnsavedChoice.Discard:
                return new CloseResult(CloseOutcome.Closed, null);
            case UnsavedChoice.Save:
                SaveResult save = SaveFlow(text, askExternalChange);
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

    /// <summary>
    /// Checks the file for a change on disk, as the window's timer does. A change to a document with no unsaved edits reloads without asking.
    /// A change to a dirty document asks <paramref name="askReloadOverMine"/>: Reload reads the file again, and Keep mine leaves the document as it
    /// is and accepts the disk's content, so that the next save does not ask again. A deleted file asks <paramref name="askKeepDeleted"/>: Keep
    /// makes the document an unsaved new file, and Close changes nothing here (the window closes the document). A failed read is reported by
    /// <see cref="TextFile.PollDisk"/> once for each state of the file.
    /// </summary>
    /// <param name="isDirty">Whether the document has unsaved edits. Only called when the file changed.</param>
    /// <param name="askReloadOverMine">Asks Reload (true) or Keep mine (false). Its default button is Keep mine.</param>
    /// <param name="askKeepDeleted">Asks Keep (true) or Close (false). Its default button is Keep.</param>
    public ExternalResult CheckExternal(Func<bool> isDirty, Func<bool> askReloadOverMine, Func<bool> askKeepDeleted)
    {
        ArgumentNullException.ThrowIfNull(isDirty);
        ArgumentNullException.ThrowIfNull(askReloadOverMine);
        ArgumentNullException.ThrowIfNull(askKeepDeleted);

        try
        {
            switch (File.PollDisk())
            {
                case DiskChange.Deleted:
                    if (!askKeepDeleted())
                    {
                        return new ExternalResult(ExternalOutcome.Close, null);
                    }

                    File.MarkMissing();
                    return new ExternalResult(ExternalOutcome.KeptDeleted, null);

                case DiskChange.Changed:
                    if (isDirty() && !askReloadOverMine())
                    {
                        File.AcceptDiskState();
                        return new ExternalResult(ExternalOutcome.KeptMine, null);
                    }

                    File.Reload();
                    return new ExternalResult(ExternalOutcome.Reloaded, null);

                default:
                    return new ExternalResult(ExternalOutcome.None, null);
            }
        }
        catch (TextFileException ex)
        {
            return new ExternalResult(ExternalOutcome.Failed, ex.Message);
        }
    }
}
