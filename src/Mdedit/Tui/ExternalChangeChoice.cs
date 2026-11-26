namespace Mdedit.Tui;

/// <summary>What the user answers when the file changed on disk before a save.</summary>
internal enum ExternalChangeChoice
{
    /// <summary>Write the text over the changed file.</summary>
    Overwrite,

    /// <summary>Replace the text with the file on disk.</summary>
    Reload,

    /// <summary>Do nothing.</summary>
    Cancel,
}
