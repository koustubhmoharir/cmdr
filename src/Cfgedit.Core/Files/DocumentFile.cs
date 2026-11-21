using System.Security.Cryptography;
using System.Text;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;
using Cfgedit.Core.Xml;
using Shared.Core;

namespace Cfgedit.Core.Files;

/// <summary>
/// A config file and the document that belongs to it. It knows what the file looked like when the document was loaded or last
/// saved, so that <see cref="ChangedOnDisk"/> can tell whether someone else changed it since. Every expected failure is a
/// <see cref="CfgeditException"/> with <see cref="Path"/> set. Documents are immutable: a save writes the text of the document
/// it is given, and the caller keeps the edit history.
/// </summary>
public sealed class DocumentFile
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // Null after CreateNew until the first save: there is then no file state to compare with.
    private Snapshot? _snapshot;

    private DocumentFile(string path, IConfigFormat format, bool exists, ConfigDocument document, Snapshot? snapshot)
    {
        Path = path;
        Format = format;
        Exists = exists;
        Document = document;
        _snapshot = snapshot;
    }

    /// <summary>The full path of the file.</summary>
    public string Path { get; }

    /// <summary>The format the file is read and written as.</summary>
    public IConfigFormat Format { get; }

    /// <summary>
    /// True when the file exists as far as this object knows: it was opened, or it has been saved. False for a new file until
    /// its first save.
    /// </summary>
    public bool Exists { get; private set; }

    /// <summary>The document as loaded, reloaded or last saved.</summary>
    public ConfigDocument Document { get; private set; }

    /// <summary>Opens and parses an existing file.</summary>
    /// <param name="path">The path of the file. A relative path is made full.</param>
    /// <param name="format">The format to parse the file as.</param>
    /// <exception cref="CfgeditException">
    /// The path is not valid, the file does not exist, it is a folder, it cannot be read, or its bytes or text are not a valid
    /// document. <see cref="CfgeditException.Path"/> is the full path.
    /// </exception>
    public static DocumentFile Open(string path, IConfigFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        string full = FullPathOf(path);
        (ConfigDocument document, Snapshot snapshot) = Load(full, format);
        return new DocumentFile(full, format, exists: true, document, snapshot);
    }

    /// <summary>
    /// Makes a new, unsaved document. The text is <c>{}</c> for the JSON family, and for XML the declaration and an empty root
    /// element of the given name. The text is UTF-8 without a byte order mark, with <see cref="Environment.NewLine"/> and a final
    /// newline. Nothing is written until <see cref="Save"/>.
    /// </summary>
    /// <param name="path">The path the file will have. A relative path is made full.</param>
    /// <param name="format">The format of the document.</param>
    /// <param name="xmlRootName">The root element's name for XML, which must be a valid XML name. Ignored for the JSON family.</param>
    /// <exception cref="CfgeditException">The path is not valid, or the XML root name is not a valid name.</exception>
    public static DocumentFile CreateNew(string path, IConfigFormat format, string? xmlRootName)
    {
        ArgumentNullException.ThrowIfNull(format);
        string full = FullPathOf(path);
        string text = InitialText(format, xmlRootName, full);
        ConfigDocument document = ConfigDocument.Parse(SourceText.Create(text, Utf8NoBom, hasBom: false), format);
        return new DocumentFile(full, format, exists: false, document, snapshot: null);
    }

    /// <summary>
    /// True when the file on disk is not the file this object last loaded or saved. A file that is missing when it was present,
    /// or present when it was missing (a new file, created by someone else), counts. For a file that was present, the identity,
    /// the length and the last write time are compared first. When they all match, the content hash is compared too.
    /// </summary>
    /// <exception cref="CfgeditException">The file exists but cannot be read.</exception>
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
        // bytes, leaves the content as it was. Config files are small, so hashing them on each check is cheap.
        FileStat now = Stat(Path);
        if (now.Length != _snapshot.Stat.Length)
        {
            return true;
        }

        byte[] bytes = ReadBytes(Path);
        return !SHA256.HashData(bytes).AsSpan().SequenceEqual(_snapshot.Sha256);
    }

    /// <summary>
    /// Writes the document's text to the file, atomically where the file system allows it (see <see cref="AtomicFile"/>). The
    /// parent folder must exist: cfgedit never creates folders. The written bytes are read back and must equal what was written.
    /// On success the snapshot and <see cref="Document"/> are those of the saved text.
    /// </summary>
    /// <param name="document">The document to save.</param>
    /// <exception cref="CfgeditException">
    /// The folder does not exist, the text cannot be encoded, the file cannot be written, or the saved file does not match what was
    /// written. On failure the file object is unchanged.
    /// </exception>
    public void Save(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string? folder = System.IO.Path.GetDirectoryName(Path);
        if (folder is null || !Directory.Exists(folder))
        {
            throw new CfgeditException($"the folder '{folder}' does not exist") { Path = Path };
        }

        byte[] bytes;
        try
        {
            bytes = document.Source.Encode();
        }
        catch (CfgeditException ex)
        {
            throw new CfgeditException(ex.Message, ex) { Path = Path };
        }

        try
        {
            AtomicFile.WriteExact(Path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CfgeditException($"the file '{Path}' cannot be saved: {ex.Message}", ex) { Path = Path };
        }

        (Snapshot snapshot, byte[] written) = Observe(Path);
        if (!written.AsSpan().SequenceEqual(bytes))
        {
            throw new CfgeditException($"the saved file does not match what was written: '{Path}'") { Path = Path };
        }

        _snapshot = snapshot;
        Exists = true;
        Document = document;
    }

    /// <summary>Reads the file again from disk, and makes the result the document and the snapshot.</summary>
    /// <returns>The reloaded document.</returns>
    /// <exception cref="CfgeditException">As for <see cref="Open"/>. On failure the file object is unchanged.</exception>
    public ConfigDocument Reload()
    {
        (ConfigDocument document, Snapshot snapshot) = Load(Path, Format);
        _snapshot = snapshot;
        Exists = true;
        Document = document;
        return document;
    }

    private static (ConfigDocument Document, Snapshot Snapshot) Load(string full, IConfigFormat format)
    {
        (Snapshot snapshot, byte[] bytes) = Observe(full);
        return (Decode(bytes, full, format), snapshot);
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
            throw new CfgeditException($"'{full}' is a folder, not a file") { Path = full };
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

    private static CfgeditException ReadFailure(string full, Exception ex)
    {
        return ex switch
        {
            FileNotFoundException or DirectoryNotFoundException => new CfgeditException($"the file '{full}' does not exist") { Path = full },
            UnauthorizedAccessException => new CfgeditException($"the file '{full}' cannot be read: access is denied", ex) { Path = full },
            _ => new CfgeditException($"the file '{full}' cannot be read: {ex.Message}", ex) { Path = full },
        };
    }

    private static ConfigDocument Decode(byte[] bytes, string full, IConfigFormat format)
    {
        try
        {
            return ConfigDocument.Load(bytes, full, format);
        }
        catch (CfgeditException ex) when (ex.Path is null)
        {
            throw new CfgeditException(ex.Message, ex) { Path = full, Line = ex.Line, Column = ex.Column };
        }
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
            throw new CfgeditException($"'{path}' is not a valid file path", ex) { Path = path };
        }
    }

    private static string InitialText(IConfigFormat format, string? xmlRootName, string full)
    {
        string newline = Environment.NewLine;
        if (format is not XmlFormat)
        {
            return "{}" + newline;
        }

        if (xmlRootName is null || !XmlSyntax.IsValidQName(xmlRootName))
        {
            throw new CfgeditException($"'{xmlRootName}' is not a valid XML element name") { Path = full };
        }

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + newline + "<" + xmlRootName + " />" + newline;
    }

    /// <summary>The file's state as the cheap checks see it.</summary>
    /// <param name="Identity">The file's identity, or null when it cannot be read.</param>
    /// <param name="LastWriteUtc">The last write time.</param>
    /// <param name="Length">The length in bytes, or -1 when the file is missing.</param>
    private sealed record FileStat(string? Identity, DateTime LastWriteUtc, long Length);

    /// <summary>What a loaded or saved file was: its state and the SHA-256 of its bytes.</summary>
    private sealed record Snapshot(FileStat Stat, byte[] Sha256);
}
