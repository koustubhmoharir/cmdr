using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Mdedit.Tui;

/// <summary>
/// Opens files and runs the Terminal.Gui application: the viewer of a document, or the recent-files picker first. Ctrl+O in the viewer
/// shows the picker over the document, and a file chosen there replaces it in the same application. The terminal is released on every
/// exit path.
/// </summary>
internal static class MdeditHost
{
    /// <summary>
    /// The file types of the pickers' Open file dialog: Markdown files first, then any file, since a file of another extension is
    /// still Markdown text.
    /// </summary>
    internal static readonly IReadOnlyList<IAllowedType> FileTypes =
        [new AllowedType("Markdown files", [".md", ".markdown", ".mdown", ".mkd"]), new AllowedTypeAny()];

    /// <summary>
    /// Runs the viewer of a document until the user quits. Ctrl+O can switch to another file from <paramref name="recent"/>.
    /// </summary>
    /// <param name="file">The file to show first.</param>
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
    /// Shows the recent-files picker, then the viewer of the file chosen there. Returns without a window when the user quits the
    /// picker. The terminal is restored before this returns.
    /// </summary>
    /// <param name="recent">The recent list, or null when there is no store location.</param>
    public static void RunPicker(RecentFiles? recent)
    {
        IApplication app = Application.Create();
        try
        {
            app.Init();

            if (StartPicker(app, recent) is { } file)
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
    /// Runs documents one after another: <paramref name="runDocument"/> runs the viewer of a document and returns the file chosen
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
    /// Loads a file for a picker: the file, or the message to show when it does not load.
    /// </summary>
    /// <param name="path">The file to load.</param>
    public static (OpenedFile? Value, string? Error) TryLoad(string path)
    {
        try
        {
            return (Load(path), null);
        }
        catch (TextFileException ex)
        {
            return (null, Describe(ex));
        }
    }

    /// <summary>
    /// Reads and decodes a file. A missing file, a folder and a file that is not valid UTF-8 (or cannot be read) are reported with a
    /// message that names the problem and the path.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <exception cref="TextFileException">The file does not exist, is a folder, cannot be read, or does not decode.</exception>
    internal static OpenedFile Load(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        if (Directory.Exists(full))
        {
            throw new TextFileException("is a directory") { Path = full };
        }

        if (!File.Exists(full))
        {
            throw new TextFileException("the file does not exist") { Path = full };
        }

        return new OpenedFile(path, TextFile.Open(path, (bytes, fullPath) => SourceText.Decode(bytes, fullPath)));
    }

    /// <summary>
    /// The message for a load error, without the program's name: <c>path:line:col: message</c> when the error has a position,
    /// <c>path: message</c> when it has a file only, and the message alone otherwise.
    /// </summary>
    /// <param name="ex">The error.</param>
    internal static string Describe(TextFileException ex)
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

    /// <summary>
    /// Moves a file to the front of the recent list and saves the list. Without a store location nothing is recorded. The list is loaded
    /// fresh, so that a file recorded during a session does not lose the others.
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

    // Shows the document's viewer, and the picker over it when Ctrl+O is pressed. Returns the file chosen with Ctrl+O, or null.
    private static OpenedFile? RunDocument(IApplication app, OpenedFile file, RecentFiles? recent)
    {
        OpenedFile? next;
        using (var window = new MainWindow(file, () => ShowPicker(app, recent)))
        {
            app.Run(window);
            next = window.SwitchTo;
        }

        return next;
    }

    // The recent-files picker, as the application starts with it. Returns the file chosen, or null when the user quits it.
    private static OpenedFile? StartPicker(IApplication app, RecentFiles? recent)
    {
        using var picker = new RecentPicker<OpenedFile>(app, recent, TryLoad, "Recent files", MdeditHints.Picker, openFile: true, fileTypes: FileTypes);
        app.Run(picker);
        return picker.Opened;
    }

    // The picker shown over a running document. Esc returns to the document; Ctrl+Q quits the application.
    private static (PickOutcome Outcome, OpenedFile? File) ShowPicker(IApplication app, RecentFiles? recent)
    {
        using var picker = new RecentPicker<OpenedFile>(app, recent, TryLoad, "Open file", MdeditHints.OpenPicker, openFile: true, fileTypes: FileTypes);
        app.Run(picker);

        if (picker.Opened is { } file)
        {
            return (PickOutcome.Chosen, file);
        }

        return (picker.QuitRequested ? PickOutcome.Quit : PickOutcome.Back, null);
    }
}
