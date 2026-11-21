using Cfgedit.Core.Model;

namespace Cfgedit.Tui;

/// <summary>A file opened for the tree: its path, and the document parsed from it.</summary>
/// <param name="Path">The file's path, as it was opened.</param>
/// <param name="Document">The parsed document.</param>
public sealed record OpenedFile(string Path, ConfigDocument Document);
