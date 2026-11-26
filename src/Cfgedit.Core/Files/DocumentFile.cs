using System.Text;
using Cfgedit.Core.Model;
using Cfgedit.Core.Xml;
using Shared.Core;

namespace Cfgedit.Core.Files;

/// <summary>
/// A config file and the document that belongs to it. The file's bytes, its snapshot and its disk checks are a <see cref="TextFile"/>;
/// this class adds the format, the parse and the document. It knows what the file looked like when the document was loaded or last
/// saved, so that <see cref="ChangedOnDisk"/> can tell whether someone else changed it since. Every expected failure is a
/// <see cref="CfgeditException"/> with <see cref="Path"/> set, except a parse error, which is kept in <see cref="ParseError"/>
/// so that the text can still be shown and fixed. Documents are immutable: a save writes the text of the document it is given,
/// and the caller keeps the edit history.
/// </summary>
public sealed class DocumentFile
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly TextFile _file;

    private DocumentFile(IConfigFormat format, TextFile file, ConfigDocument? document, CfgeditException? parseError)
    {
        Format = format;
        _file = file;
        Document = document;
        ParseError = parseError;
    }

    /// <summary>The full path of the file.</summary>
    public string Path => _file.Path;

    /// <summary>The format the file is read and written as.</summary>
    public IConfigFormat Format { get; }

    /// <summary>
    /// True when the file exists as far as this object knows: it was opened, or it has been saved. False for a new file until
    /// its first save.
    /// </summary>
    public bool Exists => _file.Exists;

    /// <summary>
    /// The document as loaded, reloaded or last saved. Null when the text does not parse, in which case <see cref="ParseError"/>
    /// says why.
    /// </summary>
    public ConfigDocument? Document { get; private set; }

    /// <summary>The text as loaded, reloaded or last saved: the decoded source, which is the file's text whether or not it parses.</summary>
    public SourceText Text => _file.Text;

    /// <summary>The parse error of <see cref="Text"/> as loaded, reloaded or last saved, or null when it parses.</summary>
    public CfgeditException? ParseError { get; private set; }

    /// <summary>
    /// Opens an existing file. A file whose text does not parse opens too: <see cref="Document"/> is then null and
    /// <see cref="ParseError"/> is set.
    /// </summary>
    /// <param name="path">The path of the file. A relative path is made full.</param>
    /// <param name="format">The format to parse the file as.</param>
    /// <exception cref="CfgeditException">
    /// The path is not valid, the file does not exist, it is a folder, it cannot be read, or its bytes cannot be decoded.
    /// <see cref="CfgeditException.Path"/> is the full path.
    /// </exception>
    public static DocumentFile Open(string path, IConfigFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        TextFile file = CfgeditText.Run(() => TextFile.Open(path, (bytes, full) => DecodeText(bytes, full, format)));
        (ConfigDocument? document, CfgeditException? parseError) = ParseFile(file, format);
        return new DocumentFile(format, file, document, parseError);
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
        TextFile file = CfgeditText.Run(() => TextFile.CreateNew(full, document.Source, (bytes, p) => DecodeText(bytes, p, format)));
        return new DocumentFile(format, file, document, parseError: null);
    }

    /// <summary>
    /// True when the file on disk is not the file this object last loaded or saved. A file that is missing when it was present,
    /// or present when it was missing (a new file, created by someone else), counts. For a file that was present, the length
    /// is compared first. When it matches, the content hash is compared too.
    /// </summary>
    /// <exception cref="CfgeditException">The file exists but cannot be read.</exception>
    public bool ChangedOnDisk() => CfgeditText.Run(() => _file.ChangedOnDisk());

    /// <summary>
    /// Checks the file for a change since it was last seen, cheaply. The file's stat (identity, length and last write time) is compared
    /// with the stat last seen; only when it differs is the content read and compared with the snapshot, so a touch or a rewrite of the
    /// same bytes is <see cref="DiskChange.Unchanged"/>. A file that was present and is now missing is <see cref="DiskChange.Deleted"/>.
    /// A file that appears where the document had none is <see cref="DiskChange.Changed"/>.
    /// </summary>
    /// <remarks>
    /// Each change is reported once: the stat last seen becomes the one just taken, so the next poll with the same stat reports
    /// <see cref="DiskChange.Unchanged"/>. The stat is taken before the content is read, so a change made in between is seen later.
    /// A file whose stat cannot be read is reported once, and then <see cref="DiskChange.Unchanged"/> until the stat can be read again.
    /// </remarks>
    /// <exception cref="CfgeditException">The file exists but cannot be read, or its stat cannot be read (it is a folder, say).</exception>
    public DiskChange PollDisk() => CfgeditText.Run(() => _file.PollDisk());

    /// <summary>
    /// Accepts the file's content on disk as the one the document is kept over (Keep mine). The snapshot becomes the disk's current state,
    /// so <see cref="ChangedOnDisk"/> is false while the disk keeps that content, and true after the next change. <see cref="Text"/>,
    /// <see cref="Document"/> and <see cref="ParseError"/> become the disk's too, because they are what the file holds: an editor that
    /// compares its document with <see cref="Text"/> then calls it unsaved even after every edit is undone, since the disk has other content.
    /// </summary>
    /// <exception cref="CfgeditException">
    /// The file cannot be read or decoded, or it is missing (use <see cref="MarkMissing"/> then). On failure nothing changes.
    /// </exception>
    public void AcceptDiskState()
    {
        CfgeditText.Run(() => _file.AcceptDiskState());
        (Document, ParseError) = ParseFile(_file, Format);
    }

    /// <summary>
    /// Records that the file was deleted or moved away, and the document is kept (Keep after the deletion). The document becomes an unsaved
    /// new file, as <see cref="CreateNew"/> makes one: <see cref="Exists"/> is false and there is no snapshot, so the next save creates the
    /// file, and <see cref="ChangedOnDisk"/> is false while the file stays missing. Nothing is read or written.
    /// </summary>
    public void MarkMissing() => _file.MarkMissing();

    /// <summary>
    /// Writes the document's text to the file, atomically where the file system allows it (see <see cref="AtomicFile"/>). The
    /// parent folder must exist: cfgedit never creates folders. The written bytes are read back and must equal what was written.
    /// On success the snapshot, <see cref="Text"/> and <see cref="Document"/> are those of the saved text.
    /// </summary>
    /// <param name="document">The document to save.</param>
    /// <exception cref="CfgeditException">
    /// The folder does not exist, the text cannot be encoded, the file cannot be written, or the saved file does not match what was
    /// written. On failure the file object is unchanged.
    /// </exception>
    public void Save(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CfgeditText.Run(() => _file.Save(document.Source));
        Document = document;
        ParseError = null;
    }

    /// <summary>
    /// Writes raw text to the file, exactly as <see cref="Save"/> writes a document, whether or not the text parses. The saved
    /// text becomes <see cref="Text"/>, and <see cref="Document"/> and <see cref="ParseError"/> say how it parses.
    /// </summary>
    /// <param name="text">The text to save, in the file's encoding.</param>
    /// <exception cref="CfgeditException">As for <see cref="Save"/>. On failure the file object is unchanged.</exception>
    public void SaveText(SourceText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        CfgeditText.Run(() => _file.Save(text));
        (Document, ParseError) = ParseFile(_file, Format);
    }

    /// <summary>
    /// Parses edited text as <see cref="Open"/> parses the file's bytes: the text is encoded as the file's encoding and parsed through
    /// those bytes. Nothing is changed in this object; the caller decides what to do with the document.
    /// </summary>
    /// <param name="text">The text to parse, in the file's encoding.</param>
    /// <returns>The document.</returns>
    /// <exception cref="CfgeditException">The text cannot be encoded, or it does not parse. Path, Line and Column say where.</exception>
    public ConfigDocument ParseText(SourceText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ParseBytes(EncodeFor(text), Path, Format);
    }

    /// <summary>
    /// Reads the file again from disk, and makes the result the text, the snapshot and the document. A text that does not parse is
    /// kept as <see cref="ParseError"/>, as <see cref="Open"/> keeps it.
    /// </summary>
    /// <returns>The reloaded document, or null when the text does not parse.</returns>
    /// <exception cref="CfgeditException">As for <see cref="Open"/> apart from parse errors. On failure the file object is unchanged.</exception>
    public ConfigDocument? Reload()
    {
        CfgeditText.Run(() => _file.Reload());
        (Document, ParseError) = ParseFile(_file, Format);
        return Document;
    }

    /// <summary>
    /// Parses the file's bytes. A parse error is returned, not thrown, so that the text can be kept and fixed.
    /// </summary>
    private static (ConfigDocument? Document, CfgeditException? ParseError) ParseFile(TextFile file, IConfigFormat format)
    {
        try
        {
            return (ParseBytes(file.Bytes.Span, file.Path, format), null);
        }
        catch (CfgeditException ex)
        {
            return (null, ex);
        }
    }

    private static ConfigDocument ParseBytes(ReadOnlySpan<byte> bytes, string full, IConfigFormat format)
    {
        try
        {
            return ConfigDocument.Load(bytes, full, format);
        }
        catch (CfgeditException ex)
        {
            throw Located(ex, full);
        }
    }

    private byte[] EncodeFor(SourceText text)
    {
        try
        {
            return text.Encode();
        }
        catch (TextFileException ex)
        {
            throw new CfgeditException(ex.Message, ex) { Path = Path, Line = ex.Line, Column = ex.Column };
        }
    }

    private static SourceText DecodeText(byte[] bytes, string full, IConfigFormat format)
    {
        return format is XmlFormat
            ? XmlDecoding.Decode(bytes, full)
            : SourceText.Decode(bytes, full);
    }

    private static CfgeditException Located(CfgeditException ex, string full)
    {
        return ex.Path is null
            ? new CfgeditException(ex.Message, ex) { Path = full, Line = ex.Line, Column = ex.Column }
            : ex;
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
        if (format is EnvFormat or TomlFormat or YamlFormat)
        {
            // An empty .env, TOML or YAML file is a valid document: its root row is still there to add to.
            return string.Empty;
        }

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
}
