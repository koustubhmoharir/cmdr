using System.Text.Json;

namespace Cmdr.Core.Config;

/// <summary>
/// The list of recently opened configuration files, most recent first, kept in the user's profile
/// (see <see cref="DefaultStorePath"/>). Has no user interface.
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
    /// The store location: <c>&lt;LocalApplicationData&gt;/cmdr/recent.json</c>. Null when the local application data
    /// folder is empty or unknown. (Windows: <c>%LOCALAPPDATA%\cmdr\recent.json</c>; Linux: <c>$XDG_DATA_HOME</c> or
    /// <c>~/.local/share/cmdr/recent.json</c>.)
    /// </summary>
    public static string? DefaultStorePath
    {
        get
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "cmdr", "recent.json");
        }
    }

    /// <summary>The store file this list is read from and saved to.</summary>
    public string StorePath { get; }

    /// <summary>The recently opened files, most recent first.</summary>
    public IReadOnlyList<string> Files => _files.AsReadOnly();

    /// <summary>
    /// Reads the store. Never throws for a missing, unreadable or malformed file, or a JSON null: those yield an empty list.
    /// Entries that are null, blank or not fully qualified are dropped; duplicates (compared as <see cref="ConfigStore"/> compares
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
            StringComparer comparer = ConfigStore.PathComparer;
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
        StringComparer comparer = ConfigStore.PathComparer;
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
        StringComparer comparer = ConfigStore.PathComparer;
        return _files.RemoveAll(entry => comparer.Equals(entry, fullPath)) > 0;
    }

    /// <summary>
    /// Saves the list as indented UTF-8 JSON (no BOM, trailing newline), replacing the store atomically.
    /// Creates the parent directory if needed.
    /// </summary>
    /// <returns>True when saved; false instead of throwing on an I/O or access failure.</returns>
    public bool TrySave()
    {
        try
        {
            string directory = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(directory);

            var data = new RecentFilesData { Files = new List<string?>(_files) };
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(data, RecentFilesJsonContext.Default.RecentFilesData);

            string tempPath = Path.Combine(directory, "." + Path.GetFileName(StorePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(json);
                    stream.WriteByte((byte)'\n');
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, StorePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

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
