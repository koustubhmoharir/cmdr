using Cfgedit.Core;
using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Cfgedit.Tui;

/// <summary>
/// Opens files and runs the Terminal.Gui application: the main window of a document, or the recent-files picker first. Ctrl+O in the
/// main window shows the picker over the document, and a file chosen there replaces it in the same application. The terminal is
/// released on every exit path.
/// </summary>
internal static class CfgeditHost
{
    /// <summary>
    /// The file types of the pickers' Open file dialog: every extension cfgedit recognises, then any file, since a file of
    /// another extension opens too when its first character tells the format.
    /// </summary>
    internal static readonly IReadOnlyList<IAllowedType> FileTypes =
        [new AllowedType("Config files", [.. Formats.KnownExtensions]), new AllowedTypeAny()];

    /// <summary>
    /// Runs the main window of a document until the user quits. Ctrl+O can switch to another file from <paramref name="recent"/>.
    /// </summary>
    /// <param name="file">The file to show first, with its document.</param>
    /// <param name="recent">The recent list that the picker uses, or null when there is no store location.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public static void Run(OpenedFile file, RecentFiles? recent)
    {
        ArgumentNullException.ThrowIfNull(file);

        IApplication app = Application.Create();
        try
        {
            app.Init();
            RunDocuments(file, next => RunDocument(app, next, recent));
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// Shows the recent-files picker, then the main window of the file chosen there. Returns without a window when the user quits the
    /// picker. The terminal is restored before this returns.
    /// </summary>
    /// <param name="recent">The recent list, or null when there is no store location.</param>
    public static void RunPicker(RecentFiles? recent)
    {
        IApplication app = Application.Create();
        try
        {
            app.Init();

            OpenedFile? file;
            using (var picker = new RecentPicker<OpenedFile>(app, recent, TryLoad, "Recent files", CfgeditHints.Picker, openFile: true, fileTypes: FileTypes))
            {
                app.Run(picker);
                file = picker.Opened;
            }

            if (file is not null)
            {
                RunDocuments(file, next => RunDocument(app, next, recent));
            }
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// Runs documents one after another: <paramref name="runDocument"/> runs the main window of a document and returns the file chosen
    /// while it ran, or null. The loop ends with the first run that returns null.
    /// </summary>
    /// <param name="first">The first document.</param>
    /// <param name="runDocument">Runs one document and returns the file to open next, or null.</param>
    internal static void RunDocuments(OpenedFile first, Func<OpenedFile, OpenedFile?> runDocument)
    {
        OpenedFile? file = first;
        while (file is not null)
        {
            file = runDocument(file);
        }
    }

    /// <summary>
    /// Loads a file for a picker: the file, or the message to show when it does not load. The format is found from the extension, then
    /// by sniffing, as on the command line; the picker cannot pass <c>--format</c>.
    /// </summary>
    /// <param name="path">The file to load.</param>
    public static (OpenedFile? Value, string? Error) TryLoad(string path)
    {
        try
        {
            return (Load(path, format: null), null);
        }
        catch (CfgeditException ex)
        {
            return (null, Describe(ex));
        }
    }

    /// <summary>
    /// Reads and parses a file. The format is <paramref name="format"/> when given, else the extension's, else the sniffed one.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="format">The format named on the command line, or null.</param>
    /// <exception cref="CfgeditException">The file cannot be read, the format cannot be found, or the text does not parse.</exception>
    internal static OpenedFile Load(string path, IConfigFormat? format)
    {
        // The bytes are read here for the messages about the file (missing, a folder, unreadable) and for sniffing. The document is then
        // opened with DocumentFile, which keeps the snapshot the save check needs.
        byte[] bytes = ReadBytes(path);
        IConfigFormat resolved = format ?? Formats.ForPath(path) ?? Sniff(bytes, path);
        try
        {
            return new OpenedFile(path, DocumentFile.Open(path, resolved));
        }
        catch (CfgeditException ex) when (ex.Path is null)
        {
            throw new CfgeditException(ex.Message) { Path = path, Line = ex.Line, Column = ex.Column };
        }
    }

    /// <summary>
    /// The message for a load error, without the program's name: <c>path:line:col: message</c> when the error has a position,
    /// <c>path: message</c> when it has a file only, and the message alone otherwise.
    /// </summary>
    internal static string Describe(CfgeditException ex)
    {
        if (ex.Path is null)
        {
            return ex.Message;
        }

        if (ex.Line is int line)
        {
            string position = ex.Column is int column ? $"{line}:{column}" : $"{line}";
            return $"{ex.Path}:{position}: {ex.Message}";
        }

        return $"{ex.Path}: {ex.Message}";
    }

    // The file's bytes. A missing file, a directory and an unreadable file are each a message that names the problem.
    private static byte[] ReadBytes(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                throw new CfgeditException("is a directory") { Path = path };
            }

            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CfgeditException("the file does not exist", ex) { Path = path };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new CfgeditException("cannot read the file: " + ex.Message, ex) { Path = path };
        }
    }

    // The format of a file with no known extension: XML when its first non-whitespace character is <, JSON5 when it is { or [. The
    // error names the file in its message and has no path, so the command line prints it as it is.
    private static IConfigFormat Sniff(byte[] bytes, string path)
    {
        string text = SourceText.Decode(bytes, path).Text;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            switch (c)
            {
                case '<':
                    return Formats.FromName("xml")!;
                case '{' or '[':
                    return Formats.FromName("json5")!;
            }

            break;
        }

        throw new CfgeditException($"cannot tell the format of {path}; use --format");
    }

    // Shows the document's window, and the picker over it when Ctrl+O is pressed. Returns the file chosen with Ctrl+O, or null.
    private static OpenedFile? RunDocument(IApplication app, OpenedFile file, RecentFiles? recent)
    {
        using var window = new MainWindow(file, () => ShowPicker(app, recent));
        app.Run(window);
        return window.SwitchTo;
    }

    // The picker shown over a running document. Esc returns to the document; Ctrl+Q quits the application.
    private static (PickOutcome Outcome, OpenedFile? File) ShowPicker(IApplication app, RecentFiles? recent)
    {
        using var picker = new RecentPicker<OpenedFile>(app, recent, TryLoad, "Open file", CfgeditHints.OpenPicker, openFile: true, fileTypes: FileTypes);
        app.Run(picker);

        if (picker.Opened is { } file)
        {
            return (PickOutcome.Chosen, file);
        }

        return (picker.QuitRequested ? PickOutcome.Quit : PickOutcome.Back, null);
    }
}
