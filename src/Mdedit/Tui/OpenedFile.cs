using Shared.Core;

namespace Mdedit.Tui;

/// <summary>A file opened for viewing: the path it was opened with, and the file with its text decoded.</summary>
/// <param name="Path">The file's path, as it was opened.</param>
/// <param name="File">The file and its text. It knows what the file held when it was read, for later checks.</param>
public sealed record OpenedFile(string Path, TextFile File);
