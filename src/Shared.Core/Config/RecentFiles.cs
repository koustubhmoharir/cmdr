using System.Text.Json;

namespace Shared.Core;

/// <summary>
/// The list of recently opened configuration files, most recent first, kept in the user's profile
/// (see <see cref="DefaultStorePathFor"/>). Has no user interface.
/// </summary>
public sealed class RecentFiles
{
    /// <summary>The maximum number of entries kept.</summary>
    public const int MaxEntries = 20;

    private readonly List<string> _files;

    private RecentFiles(string storePath, List<string> files)
    {
        StorePath = storePath;
        _files = files;
    }

    /// <summary>
    /// The store location for an application: <c>&lt;LocalApplicationData&gt;/&lt;appName&gt;/recent.json</c>. Null when the local
    /// application data folder is empty or unknown. (Windows: <c>%LOCALAPPDATA%\&lt;appName&gt;\recent.json</c>; Linux:
    /// <c>$XDG_DATA_HOME</c> or <c>~/.local/share/&lt;appName&gt;/recent.json</c>.)
    /// </summary>
    /// <param name="appName">The application's folder name in the local application data folder. Not empty.</param>
    public static string? DefaultStorePathFor(string appName)
    {
        ArgumentException.ThrowIfNullOrEmpty(appName);

        string folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, appName, "recent.json");
    }

    /// <summary>The store file this list is read from and saved to.</summary>
    public string StorePath { get; }

    /// <summary>The recently opened files, most recent first.</summary>
    public IReadOnlyList<string> Files => _files.AsReadOnly();

    /// <summary>
    /// Reads the store. Never throws for a missing, unreadable or malformed file, or a JSON null: those yield an empty list.
    /// Entries that are null, blank or not fully qualified are dropped; duplicates (compared as <see cref="PathComparison"/> compares
    /// paths) keep the first occurrence; the list is truncated to <see cref="MaxEntries"/>.
    /// </summary>
    /// <param name="storePath">The store file. It need not exist.</param>
    public static RecentFiles Load(string storePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(storePath);

        string fullPath = Path.GetFullPath(storePath);
        var files = new List<string>();
        RecentFilesData? data = ReadData(fullPath);

        if (data?.Files is not null)
        {
            StringComparer comparer = PathComparison.Comparer;
            var seen = new HashSet<string>(comparer);
            foreach (string? entry in data.Files)
            {
                if (files.Count >= MaxEntries)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(entry) || !Path.IsPathFullyQualified(entry))
                {
                    continue;
                }

                try
                {
                    string normalized = Path.GetFullPath(entry);
                    if (seen.Add(normalized))
                    {
                        files.Add(normalized);
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // A malformed entry should not prevent other recent files from loading.
                }
            }
        }

        return new RecentFiles(fullPath, files);
    }

    /// <summary>
    /// Moves or inserts <paramref name="path"/> (as its full path) at the front. An existing equal entry is removed first, and the
    /// list is truncated to <see cref="MaxEntries"/>. Does not save.
    /// </summary>
    public void Add(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string fullPath = Path.GetFullPath(path);
        StringComparer comparer = PathComparison.Comparer;
        _files.RemoveAll(entry => comparer.Equals(entry, fullPath));
        _files.Insert(0, fullPath);

        if (_files.Count > MaxEntries)
        {
            _files.RemoveRange(MaxEntries, _files.Count - MaxEntries);
        }
    }

    /// <summary>
    /// Removes the entry equal to the full path of <paramref name="path"/>.
    /// Returns whether an entry was removed. Does not save.
    /// </summary>
    public bool Remove(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string fullPath = Path.GetFullPath(path);
        StringComparer comparer = PathComparison.Comparer;
        return _files.RemoveAll(entry => comparer.Equals(entry, fullPath)) > 0;
    }

    /// <summary>
    /// Saves the list as indented UTF-8 JSON (no BOM, trailing newline), replacing the store atomically.
    /// Creates the parent directory if needed. An existing store keeps its Unix permissions, and a store path that is a symbolic
    /// link keeps the link and updates its target. A dangling link is not saved.
    /// </summary>
    /// <returns>True when saved; false instead of throwing on an I/O or access failure (including a dangling link).</returns>
    public bool TrySave()
    {
        try
        {
            var data = new RecentFilesData { Files = new List<string?>(_files) };
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(data, RecentFilesJsonContext.Default.RecentFilesData);
            AtomicFile.Write(StorePath, json);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Returns null for a missing or unreadable file, malformed JSON, or a JSON null document.
    private static RecentFilesData? ReadData(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            {
                bytes = bytes[3..];
            }
            return JsonSerializer.Deserialize(bytes, RecentFilesJsonContext.Default.RecentFilesData);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// The on-disk shape: { "files": [ ... ] }. Entries are nullable so that a null entry in the file can be read and dropped.
internal sealed class RecentFilesData
{
    public List<string?>? Files { get; set; }
}
