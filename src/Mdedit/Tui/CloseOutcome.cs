namespace Mdedit.Tui;

/// <summary>How a quit or switch flow ended.</summary>
internal enum CloseOutcome
{
    /// <summary>The document may be left.</summary>
    Closed,

    /// <summary>The user chose to stay open.</summary>
    Cancelled,

    /// <summary>The user chose to reload during the save, so the document stays open with the file's text.</summary>
    Reloaded,

    /// <summary>The save failed, so the document stays open; the error says why.</summary>
    Failed,
}
