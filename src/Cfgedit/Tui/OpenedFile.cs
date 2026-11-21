using Cfgedit.Core.Files;
using Cfgedit.Core.Model;

namespace Cfgedit.Tui;

/// <summary>A file opened for the tree: its path, and the file with the document parsed from it.</summary>
/// <param name="Path">The file's path, as it was opened.</param>
/// <param name="File">The file and its document. It knows what the file held when it was read, for the save check.</param>
public sealed record OpenedFile(string Path, DocumentFile File)
{
    /// <summary>The document as it was read.</summary>
    public ConfigDocument Document => File.Document;
}
