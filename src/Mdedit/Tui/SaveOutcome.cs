namespace Mdedit.Tui;

/// <summary>How a save flow ended.</summary>
internal enum SaveOutcome
{
    /// <summary>The text was written to the file.</summary>
    Saved,

    /// <summary>The user cancelled, so nothing was written.</summary>
    Cancelled,

    /// <summary>The user chose to reload, so the file was read again and the text is replaced by it; nothing was written.</summary>
    Reloaded,

    /// <summary>The save failed; the error says why.</summary>
    Failed,
}
