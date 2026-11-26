namespace Mdedit.Tui;

/// <summary>How a check for a change on disk ended.</summary>
internal enum ExternalOutcome
{
    /// <summary>Nothing changed on disk since the last check.</summary>
    None,

    /// <summary>The file changed and the document was reloaded from it. The reload is an undo step in the window.</summary>
    Reloaded,

    /// <summary>The file changed and the user kept the document. The file's content on disk is accepted, so the next save does not ask.</summary>
    KeptMine,

    /// <summary>The file was deleted and the user kept the document, which is now an unsaved new file.</summary>
    KeptDeleted,

    /// <summary>The file was deleted and the user chose to close the document. Nothing else changed.</summary>
    Close,

    /// <summary>The check or the reload failed; the error says why. Reported once for each state of the file.</summary>
    Failed,
}
