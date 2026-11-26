namespace Mdedit.Tui;

/// <summary>What the user answers when a document with unsaved changes is about to be closed or replaced.</summary>
internal enum UnsavedChoice
{
    /// <summary>Save first, then close.</summary>
    Save,

    /// <summary>Close without saving.</summary>
    Discard,

    /// <summary>Stay open.</summary>
    Cancel,
}
