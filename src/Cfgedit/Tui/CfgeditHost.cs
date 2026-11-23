using Cfgedit.Core;
using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;
using Cfgedit.Core.Xml;
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

    /// <summary>The formats the choice dialog offers when a new file's extension names none, in the order shown.</summary>
    internal static readonly IReadOnlyList<string> FormatNames = ["json", "jsonc", "json5", "xml", "env", "toml"];

    /// <summary>The labels the format choice shows, in the order of <see cref="FormatNames"/>: .env and .toml are labelled by their extensions.</summary>
    internal static IReadOnlyList<string> FormatLabels { get; } = FormatNames.Select(name => name switch
    {
        "env" => ".env",
        "toml" => ".toml",
        _ => name,
    }).ToList();

    /// <summary>
    /// The new-file flow for a path that does not exist. Asks "Create &lt;name&gt;?" (Cancel creates nothing), then the format when neither
    /// <paramref name="format"/> nor the extension gives one, then the root element's name for XML. The window opens on an unsaved
    /// document, and the path is recorded in the recent list after its first successful save.
    /// </summary>
    /// <param name="path">The path of the file to create.</param>
    /// <param name="format">The format named on the command line, or null.</param>
    /// <param name="loadRecent">Loads the recent list, or returns null when there is no store.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="CfgeditException">The new document cannot be made from the path.</exception>
    public static void RunNew(string path, IConfigFormat? format, Func<RecentFiles?> loadRecent)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(loadRecent);

        IApplication app = Application.Create();
        try
        {
            app.Init();
            NewFileFlow(app, path, format, loadRecent);
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// The new-file flow in a running application: the questions of <see cref="AskNewFile"/>, then the window of the unsaved document.
    /// Nothing is created until the window's first save.
    /// </summary>
    /// <param name="app">The initialised application.</param>
    /// <param name="path">The path of the file to create.</param>
    /// <param name="format">The format named on the command line, or null.</param>
    /// <param name="loadRecent">Loads the recent list, or returns null when there is no store.</param>
    internal static void NewFileFlow(IApplication app, string path, IConfigFormat? format, Func<RecentFiles?> loadRecent)
    {
        NewFileChoice? choice = AskNewFile(app, path, format);
        if (choice is null)
        {
            return;
        }

        var file = new OpenedFile(path, DocumentFile.CreateNew(path, choice.Format, choice.RootName));
        OpenedFile? next = RunDocument(app, file, loadRecent(), RecordOnFirstSave(path, loadRecent));
        if (next is not null)
        {
            RunDocuments(next, following => RunDocument(app, following, loadRecent()));
        }
    }

    /// <summary>
    /// The questions of a new file: "Create &lt;name&gt;?" with Cancel and Create (Create is the default), then the format when neither
    /// <paramref name="format"/> nor the extension gives one (a choice of json, jsonc, json5, xml, .env and .toml), then the root element's name for
    /// XML (validated with <see cref="XmlSyntax.IsValidQName"/>). Returns null when any question is cancelled.
    /// </summary>
    /// <param name="app">The running application.</param>
    /// <param name="path">The path of the file to create.</param>
    /// <param name="format">The format named on the command line, or null.</param>
    internal static NewFileChoice? AskNewFile(IApplication app, string path, IConfigFormat? format)
    {
        if (Confirm.Choose(app, "New file", $"Create {Path.GetFileName(path)}?", "Create", "Cancel") != 0)
        {
            return null;
        }

        IConfigFormat? resolved = format ?? Formats.ForPath(path);
        if (resolved is null)
        {
            if (ChoiceDialog.Ask(app, "Format of " + Path.GetFileName(path), FormatLabels, 0) is not { } picked)
            {
                return null;
            }

            resolved = Formats.FromName(FormatNames[picked])!;
        }

        if (resolved is YamlFormat)
        {
            // See RefuseUnfinished: the tree cannot show YAML yet.
            Confirm.Choose(app, "New file", "YAML editing is not finished yet", "OK");
            return null;
        }

        if (resolved is not XmlFormat)
        {
            return new NewFileChoice(resolved, null);
        }

        string? rootName = null;
        bool named = TextPromptDialog.Ask(app, "New XML file", "Root", string.Empty, name =>
        {
            if (!XmlSyntax.IsValidQName(name))
            {
                return $"'{name}' is not a valid XML name";
            }

            rootName = name;
            return null;
        });
        return named && rootName is not null ? new NewFileChoice(resolved, rootName) : null;
    }

    /// <summary>The action that records a new file in the recent list, for the window's first successful save.</summary>
    internal static Action RecordOnFirstSave(string path, Func<RecentFiles?> loadRecent) => () => RecordRecent(loadRecent, path);

    /// <summary>
    /// Moves a file to the front of the recent list and saves the list. Without a store location nothing is recorded; a failed save is
    /// ignored. The list is loaded fresh, so that a file recorded during a session does not lose the others.
    /// </summary>
    /// <param name="loadRecent">Loads the recent list, or returns null when there is no store.</param>
    /// <param name="path">The file to record.</param>
    /// <returns>The list that was recorded into, or null when there is no store.</returns>
    internal static RecentFiles? RecordRecent(Func<RecentFiles?> loadRecent, string path)
    {
        RecentFiles? recent = loadRecent();
        if (recent is null)
        {
            return null;
        }

        recent.Add(path);
        recent.TrySave();
        return recent;
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
        RefuseUnfinished(resolved, path);
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
    /// Refuses a format whose TUI support is not finished (YAML, until its rows land): Core parses and checks it, but the tree cannot
    /// show its rows yet. Remove this when the TUI handles YAML.
    /// </summary>
    /// <exception cref="CfgeditException">The format is not supported by the TUI yet.</exception>
    private static void RefuseUnfinished(IConfigFormat format, string path)
    {
        if (format is YamlFormat)
        {
            throw new CfgeditException("YAML editing is not finished yet") { Path = path };
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
    private static OpenedFile? RunDocument(IApplication app, OpenedFile file, RecentFiles? recent, Action? onFirstSave = null)
    {
        using var window = new MainWindow(file, () => ShowPicker(app, recent), onFirstSave: onFirstSave);
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
