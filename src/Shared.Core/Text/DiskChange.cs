namespace Shared.Core;

/// <summary>What <see cref="TextFile.PollDisk"/> found on disk.</summary>
public enum DiskChange
{
    /// <summary>The content is the one the document has, or the stat changed but the content did not (a touch, or the same bytes written again).</summary>
    Unchanged,

    /// <summary>The content differs from the snapshot, or the file appeared where the document had none.</summary>
    Changed,

    /// <summary>The file was there when the document last saw it, and it is missing now.</summary>
    Deleted,
}
