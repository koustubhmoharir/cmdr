using Shared.Core;

namespace Logvu.Core.Config;

/// <summary>Finds the member files of a configured set. See the "File set" rules of the logvu architecture notes.</summary>
public static class FileSet
{
    /// <summary>The extensions of compressed files, which are never members of a set.</summary>
    private static readonly HashSet<string> CompressedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gz", ".zip", ".bz2", ".xz", ".zst", ".7z", ".br", ".lz4",
    };

    /// <summary>
    /// Lists the member files of a configured set: regular files directly inside <see cref="LogvuConfig.LogsDirectory"/> whose basename
    /// starts with <see cref="LogvuConfig.FilePrefix"/> and matches <see cref="LogvuConfig.FileNamePattern"/>, excluding compressed files.
    /// A symbolic link is a member when it resolves to a regular file; a broken link, or one that resolves to a directory, is not.
    /// </summary>
    /// <returns>
    /// The full paths of the members, ordered by basename (ordinal). Empty when the directory does not exist: the set is then a live
    /// view that waits for its first file.
    /// </returns>
    /// <exception cref="FileSetException">The directory exists but cannot be read.</exception>
    public static IReadOnlyList<string> Enumerate(LogvuConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        List<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(config.LogsDirectory).EnumerateFileSystemInfos().ToList();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileSetException($"Cannot read directory '{config.LogsDirectory}': {ex.Message}", ex);
        }

        var names = new List<string>();
        foreach (FileSystemInfo entry in entries)
        {
            if (IsMemberName(entry.Name, config) && IsRegularFile(entry))
            {
                names.Add(entry.Name);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names.Select(name => Path.Combine(config.LogsDirectory, name)).ToList();
    }

    private static bool IsMemberName(string name, LogvuConfig config)
    {
        string prefix = config.FilePrefix;
        if (name.Length < prefix.Length || !PathComparison.Comparer.Equals(name[..prefix.Length], prefix))
        {
            return false;
        }

        if (CompressedExtensions.Contains(Path.GetExtension(name)))
        {
            return false;
        }

        return LogvuConfig.TryMatch(config.FileNamePattern, name);
    }

    private static bool IsRegularFile(FileSystemInfo entry)
    {
        if (entry.LinkTarget is null)
        {
            return entry is FileInfo && !entry.Attributes.HasFlag(FileAttributes.Directory);
        }

        try
        {
            // A link to a missing target resolves to null, and a link loop throws; neither is a member.
            return entry.ResolveLinkTarget(returnFinalTarget: true) is FileInfo { Exists: true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
