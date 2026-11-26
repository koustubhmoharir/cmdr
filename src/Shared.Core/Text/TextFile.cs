using System.Security.Cryptography;

namespace Shared.Core;

/// <summary>
/// A text file and what it is on disk. It knows what the file looked like when it was loaded or last saved, so that
/// <see cref="ChangedOnDisk"/> and <see cref="PollDisk"/> can tell whether someone else changed it since. It knows nothing of
/// formats: the caller supplies the decoder that turns the bytes into a <see cref="SourceText"/>. Every expected failure is a
/// <see cref="TextFileException"/> with <see cref="Path"/> set. On failure an operation leaves the object unchanged.
/// </summary>
public sealed class TextFile
{
    // The stat of a file that is missing, as Stat reports it.
    private static readonly FileStat MissingStat = new(null, default, -1);

    private readonly Func<byte[], string, SourceText> _decode;

    // Null after CreateNew until the first save: there is then no file state to compare with.
    private Snapshot? _snapshot;

    // The stat last seen by PollDisk, or taken by an open, a save, a reload or an accepted state. PollDisk compares it with the
    // file's stat to decide whether to read the content.
    private FileStat _seen;

    // True while the stat cannot be read (the path is a folder, say), so that the error is reported once, not on every poll.
    private bool _statUnreadable;

    private byte[] _bytes;

    private TextFile(
        string path, Func<byte[], string, SourceText> decode, bool exists, SourceText text, byte[] bytes, Snapshot? snapshot)
    {
        Path = path;
        _decode = decode;
        Exists = exists;
        Text = text;
        _bytes = bytes;
        _snapshot = snapshot;
        _seen = snapshot?.Stat ?? MissingStat;
    }

    /// <summary>The full path of the file.</summary>
    public string Path { get; }

    /// <summary>
    /// True when the file exists as far as this object knows: it was opened, or it has been saved. False for a new file until
    /// its first save.
    /// </summary>
    public bool Exists { get; private set; }

    /// <summary>The text as loaded, reloaded, accepted or last saved: the decoded source of <see cref="Bytes"/>.</summary>
    public SourceText Text { get; private set; }

    /// <summary>
    /// The bytes of <see cref="Text"/> as last read from or written to disk. For a new file that is not yet saved, they are
    /// the encoded initial text.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes => _bytes;

    /// <summary>
    /// Opens an existing file and decodes it. The file is read once: its state is recorded before the bytes are read, so a change
    /// made in between is seen as a change later, never missed.
    /// </summary>
    /// <param name="path">The path of the file. A relative path is made full.</param>
    /// <param name="decode">Decodes the file's bytes, given the full path for any error. Its <see cref="TextFileException"/>s are kept.</param>
    /// <exception cref="TextFileException">
    /// The path is not valid, the file does not exist, it is a folder, it cannot be read, or the decoder fails.
    /// <see cref="TextFileException.Path"/> is the full path.
    /// </exception>
    public static TextFile Open(string path, Func<byte[], string, SourceText> decode)
    {
        ArgumentNullException.ThrowIfNull(decode);
        string full = FullPathOf(path);
        (Snapshot snapshot, byte[] bytes, SourceText text) = Load(full, decode);
        return new TextFile(full, decode, exists: true, text, bytes, snapshot);
    }

    /// <summary>
    /// Makes a new, unsaved file with the given text, decoded with <see cref="SourceText.Decode"/> on reload. Nothing is written
    /// until <see cref="Save"/>.
    /// </summary>
    /// <param name="path">The path the file will have. A relative path is made full.</param>
    /// <param name="text">The initial text.</param>
    /// <exception cref="TextFileException">The path is not valid, or the text cannot be encoded.</exception>
    public static TextFile CreateNew(string path, SourceText text)
    {
        return CreateNew(path, text, static (bytes, fullPath) => SourceText.Decode(bytes, fullPath));
    }

    /// <summary>
    /// Makes a new, unsaved file with the given text, as <see cref="CreateNew(string, SourceText)"/> does, but decoded with the
    /// given decoder on reload, as <see cref="Open"/> decodes an existing file.
    /// </summary>
    /// <param name="path">The path the file will have. A relative path is made full.</param>
    /// <param name="text">The initial text.</param>
    /// <param name="decode">Decodes the file's bytes on reload, given the full path for any error.</param>
    /// <exception cref="TextFileException">The path is not valid, or the text cannot be encoded.</exception>
    public static TextFile CreateNew(string path, SourceText text, Func<byte[], string, SourceText> decode)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(decode);
        string full = FullPathOf(path);
        byte[] bytes = EncodeAt(text, full);
        return new TextFile(full, decode, exists: false, text, bytes, snapshot: null);
    }

    /// <summary>
    /// True when the file on disk is not the file this object last loaded or saved. A file that is missing when it was present,
    /// or present when it was missing (a new file, created by someone else), counts. For a file that was present, the length
    /// is compared first. When it matches, the content hash is compared too.
    /// </summary>
    /// <exception cref="TextFileException">The file exists but cannot be read.</exception>
    public bool ChangedOnDisk()
    {
        if (!File.Exists(Path))
        {
            return _snapshot is not null;
        }

        if (_snapshot is null)
        {
            return true; // a new file, and someone created it meanwhile
        }

        // Only the content decides. A different length is a change for certain; otherwise the hash tells. Neither the write
        // time nor the file identity is compared: a touch, or an editor that saves by replacing the file with the same
        // bytes, leaves the content as it was. The files edited are small, so hashing them on each check is cheap.
        FileStat now = Stat(Path);
        if (now.Length != _snapshot.Stat.Length)
        {
            return true;
        }

        byte[] bytes = ReadBytes(Path);
        return !SHA256.HashData(bytes).AsSpan().SequenceEqual(_snapshot.Sha256);
    }

    /// <summary>
    /// Checks the file for a change since it was last seen, cheaply. The file's stat (identity, length and last write time) is compared
    /// with the stat last seen; only when it differs is the content read and compared with the snapshot, so a touch or a rewrite of the
    /// same bytes is <see cref="DiskChange.Unchanged"/>. A file that was present and is now missing is <see cref="DiskChange.Deleted"/>.
    /// A file that appears where the file had none is <see cref="DiskChange.Changed"/>.
    /// </summary>
    /// <remarks>
    /// Each change is reported once: the stat last seen becomes the one just taken, so the next poll with the same stat reports
    /// <see cref="DiskChange.Unchanged"/>. The stat is taken before the content is read, so a change made in between is seen later.
    /// A file whose stat cannot be read is reported once, and then <see cref="DiskChange.Unchanged"/> until the stat can be read again.
    /// </remarks>
    /// <exception cref="TextFileException">The file exists but cannot be read, or its stat cannot be read (it is a folder, say).</exception>
    public DiskChange PollDisk()
    {
        FileStat now;
        try
        {
            now = Stat(Path);
        }
        catch (TextFileException)
        {
            if (_statUnreadable)
            {
                return DiskChange.Unchanged;
            }

            _statUnreadable = true;
            throw;
        }

        _statUnreadable = false;
        if (now == _seen)
        {
            return DiskChange.Unchanged;
        }

        _seen = now;
        if (now.Length < 0)
        {
            return _snapshot is null ? DiskChange.Unchanged : DiskChange.Deleted;
        }

        byte[] bytes = ReadBytes(Path);
        bool same = _snapshot is not null && SHA256.HashData(bytes).AsSpan().SequenceEqual(_snapshot.Sha256);
        return same ? DiskChange.Unchanged : DiskChange.Changed;
    }

    /// <summary>
    /// Accepts the file's content on disk as the one the text is kept over (Keep mine). The snapshot becomes the disk's current state,
    /// so <see cref="ChangedOnDisk"/> is false while the disk keeps that content, and true after the next change. <see cref="Text"/>
    /// and <see cref="Bytes"/> become the disk's too, because they are what the file holds.
    /// </summary>
    /// <exception cref="TextFileException">
    /// The file cannot be read or decoded, or it is missing (use <see cref="MarkMissing"/> then). On failure nothing changes.
    /// </exception>
    public void AcceptDiskState() => LoadFromDisk();

    /// <summary>
    /// Records that the file was deleted or moved away, and the text is kept (Keep after the deletion). The file becomes an unsaved new
    /// file, as <see cref="CreateNew(string, SourceText)"/> makes one: <see cref="Exists"/> is false and there is no snapshot, so the next
    /// save creates the file, and <see cref="ChangedOnDisk"/> is false while the file stays missing. Nothing is read or written.
    /// </summary>
    public void MarkMissing()
    {
        Exists = false;
        _snapshot = null;
        _seen = MissingStat;
    }

    /// <summary>
    /// Writes the text to the file, atomically where the file system allows it (see <see cref="AtomicFile"/>). The parent folder
    /// must exist: nothing creates folders. The written bytes are read back and must equal what was written. On success the
    /// snapshot, <see cref="Text"/>, <see cref="Bytes"/> and <see cref="Exists"/> are those of the saved text.
    /// </summary>
    /// <param name="text">The text to save, in its own encoding.</param>
    /// <exception cref="TextFileException">
    /// The folder does not exist, the text cannot be encoded, the file cannot be written, or the saved file does not match what was
    /// written. On failure the file object is unchanged.
    /// </exception>
    public void Save(SourceText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] bytes = EncodeAt(text, Path);
        WriteExact(bytes);
        Text = text;
        _bytes = bytes;
    }

    /// <summary>
    /// Reads the file again from disk with the decoder it was opened with, and makes the result the text, the bytes and the snapshot.
    /// </summary>
    /// <exception cref="TextFileException">As for <see cref="Open"/>. On failure the file object is unchanged.</exception>
    public void Reload() => LoadFromDisk();

    // Reload and AcceptDiskState do the same here; they differ in what an editor does with the result.
    private void LoadFromDisk()
    {
        (Snapshot snapshot, byte[] bytes, SourceText text) = Load(Path, _decode);
        SetSnapshot(snapshot);
        Exists = true;
        Text = text;
        _bytes = bytes;
    }

    private void WriteExact(byte[] bytes)
    {
        string? folder = System.IO.Path.GetDirectoryName(Path);
        if (folder is null || !Directory.Exists(folder))
        {
            throw new TextFileException($"the folder '{folder}' does not exist") { Path = Path };
        }

        try
        {
            AtomicFile.WriteExact(Path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TextFileException($"the file '{Path}' cannot be saved: {ex.Message}", ex) { Path = Path };
        }

        (Snapshot snapshot, byte[] written) = Observe(Path);
        if (!written.AsSpan().SequenceEqual(bytes))
        {
            throw new TextFileException($"the saved file does not match what was written: '{Path}'") { Path = Path };
        }

        SetSnapshot(snapshot);
        Exists = true;
    }

    /// <summary>Records what the file is now as the snapshot, and as the stat last seen by <see cref="PollDisk"/>.</summary>
    private void SetSnapshot(Snapshot snapshot)
    {
        _snapshot = snapshot;
        _seen = snapshot.Stat;
    }

    /// <summary>
    /// Reads and decodes a file. A decoding error throws, with the full path set when the decoder did not set one.
    /// </summary>
    private static (Snapshot Snapshot, byte[] Bytes, SourceText Text) Load(string full, Func<byte[], string, SourceText> decode)
    {
        (Snapshot snapshot, byte[] bytes) = Observe(full);
        try
        {
            return (snapshot, bytes, decode(bytes, full));
        }
        catch (TextFileException ex) when (ex.Path is null)
        {
            throw new TextFileException(ex.Message, ex) { Path = full, Line = ex.Line, Column = ex.Column };
        }
    }

    private static byte[] EncodeAt(SourceText text, string full)
    {
        try
        {
            return text.Encode();
        }
        catch (TextFileException ex)
        {
            throw new TextFileException(ex.Message, ex) { Path = full, Line = ex.Line, Column = ex.Column };
        }
    }

    /// <summary>
    /// Reads a file and records what it is. The state is taken before the bytes are read, so a change made in between is seen as
    /// a change later, never missed.
    /// </summary>
    private static (Snapshot Snapshot, byte[] Bytes) Observe(string full)
    {
        FileStat stat = Stat(full);
        byte[] bytes = ReadBytes(full);
        return (new Snapshot(stat, SHA256.HashData(bytes)), bytes);
    }

    private static FileStat Stat(string full)
    {
        if (Directory.Exists(full))
        {
            throw new TextFileException($"'{full}' is a folder, not a file") { Path = full };
        }

        try
        {
            string physical = PhysicalPath.Resolve(full);
            var info = new FileInfo(physical);
            return new FileStat(FileIdentity.TryGet(physical), info.LastWriteTimeUtc, info.Exists ? info.Length : -1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ReadFailure(full, ex);
        }
    }

    private static byte[] ReadBytes(string full)
    {
        try
        {
            return File.ReadAllBytes(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ReadFailure(full, ex);
        }
    }

    private static TextFileException ReadFailure(string full, Exception ex)
    {
        return ex switch
        {
            FileNotFoundException or DirectoryNotFoundException => new TextFileException($"the file '{full}' does not exist") { Path = full },
            UnauthorizedAccessException => new TextFileException($"the file '{full}' cannot be read: access is denied", ex) { Path = full },
            _ => new TextFileException($"the file '{full}' cannot be read: {ex.Message}", ex) { Path = full },
        };
    }

    private static string FullPathOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new TextFileException($"'{path}' is not a valid file path", ex) { Path = path };
        }
    }

    /// <summary>The file's state as the cheap checks see it.</summary>
    /// <param name="Identity">The file's identity, or null when it cannot be read.</param>
    /// <param name="LastWriteUtc">The last write time.</param>
    /// <param name="Length">The length in bytes, or -1 when the file is missing.</param>
    private sealed record FileStat(string? Identity, DateTime LastWriteUtc, long Length);

    /// <summary>What a loaded or saved file was: its state and the SHA-256 of its bytes.</summary>
    private sealed record Snapshot(FileStat Stat, byte[] Sha256);
}
