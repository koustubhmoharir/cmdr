using System.Text;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Mdedit.Tui;

/// <summary>
/// Opens files and runs the Terminal.Gui application: the viewer of a document, the new-file flow for a path that does not exist, or the
/// recent-files picker first. Ctrl+O in the viewer shows the picker over the document, and a file chosen there replaces it in the same
/// application. The terminal is released on every exit path.
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
            RunDocuments(new Visit(file), (visit, back) => RunDocument(app, visit, recent, back));
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
                RunDocuments(new Visit(file), (visit, back) => RunDocument(app, visit, recent, back));
            }
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// The new-file flow for a path that does not exist: asks "Create &lt;name&gt;?" (Cancel creates nothing), then opens the window on an
    /// unsaved empty document in edit mode. The path is recorded in the recent list after the window's first successful save.
    /// </summary>
    /// <param name="path">The path of the file to create.</param>
    /// <param name="loadRecent">Loads the recent list, or returns null when there is no store.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public static void RunNew(string path, Func<RecentFiles?> loadRecent)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(loadRecent);

        IApplication app = Application.Create();
        try
        {
            app.Init();
            NewFileFlow(app, path, loadRecent);
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// The new-file flow in a running application: the question of <see cref="AskNewFile"/>, then the window of the unsaved document. Nothing is
    /// created until the window's first save.
    /// </summary>
    /// <param name="app">The initialised application.</param>
    /// <param name="path">The path of the file to create.</param>
    /// <param name="loadRecent">Loads the recent list, or returns null when there is no store.</param>
    internal static void NewFileFlow(IApplication app, string path, Func<RecentFiles?> loadRecent)
    {
        if (!AskNewFile(app, path))
        {
            return;
        }

        var file = new OpenedFile(path, TextFile.CreateNew(path, SourceText.Create(string.Empty, Utf8WithoutBom, hasBom: false)));
        RunDocuments(
            new Visit(file, OnFirstSave: RecordOnFirstSave(path, loadRecent)),
            (visit, back) => RunDocument(app, visit, loadRecent(), back));
    }

    /// <summary>The question for a path that does not exist: "Create &lt;name&gt;?" with Create as the default. True for Create.</summary>
    /// <param name="app">The running application.</param>
    /// <param name="path">The path of the file to create.</param>
    internal static bool AskNewFile(IApplication app, string path) =>
        Confirm.Choose(app, "New file", $"Create {System.IO.Path.GetFileName(path)}?", "Create", "Cancel") == 0;

    /// <summary>
    /// True when the path names neither a file nor a folder. The command line starts the new-file flow for it instead of the viewer.
    /// </summary>
    /// <param name="path">The path given on the command line.</param>
    internal static bool Missing(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        return !File.Exists(full) && !Directory.Exists(full);
    }

    /// <summary>The action that records a new file in the recent list, for the window's first successful save.</summary>
    /// <param name="path">The file to record.</param>
    /// <param name="loadRecent">Loads the recent list, or returns null when there is no store.</param>
    internal static Action RecordOnFirstSave(string path, Func<RecentFiles?> loadRecent) => () => RecordRecent(loadRecent, path);

    // UTF-8 without a byte order mark: the encoding of a new file.
    private static Encoding Utf8WithoutBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Runs documents one after another: <paramref name="runDocument"/> runs the viewer of a document, with the back stack of the run, and
    /// returns the document to show next, or null. The loop ends with the first run that returns null. The stack is made for each run of
    /// the loop, so that the places a followed link leaves are there for the Back of the documents that follow.
    /// </summary>
    /// <param name="first">The first document.</param>
    /// <param name="runDocument">Runs one document and returns the document to show next, or null.</param>
    internal static void RunDocuments(Visit first, Func<Visit, BackStack, Visit?> runDocument)
    {
        var back = new BackStack();
        Visit? visit = first;
        while (visit is not null)
        {
            visit = runDocument(visit, back);
        }
    }

    /// <summary>
    /// One document of a run of documents (see <see cref="RunDocuments"/>): the file, the heading that a followed link names (or null), the place
    /// that a Back returns to (or null), and the action that runs after the first save of a new file (or null).
    /// </summary>
    /// <param name="File">The file to show.</param>
    /// <param name="Fragment">The fragment of the link that opened the file: the window starts at that heading.</param>
    /// <param name="Restore">The caret and top line that a Back returns to: the window starts there.</param>
    /// <param name="OnFirstSave">Runs after the first successful save of the file (see <see cref="MainWindow"/>).</param>
    internal sealed record Visit(OpenedFile File, string? Fragment = null, BackEntry? Restore = null, Action? OnFirstSave = null);

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

    // Shows the document's viewer, and the picker over it when Ctrl+O is pressed. Returns the document to show next, or null: the file chosen
    // with Ctrl+O, or a file followed from a link (with its heading and the place a Back returns to). When the file was deleted and the window
    // closed (see MainWindow.ReturnToPicker), the recent-files picker is shown as at startup instead, and the back stack is cleared.
    private static Visit? RunDocument(IApplication app, Visit visit, RecentFiles? recent, BackStack back)
    {
        bool returnToPicker;
        Visit? next;
        using (var window = new MainWindow(
            visit.File,
            () => ShowPicker(app, recent),
            onFirstSave: visit.OnFirstSave,
            back: back,
            fragment: visit.Fragment,
            restore: visit.Restore))
        {
            app.Run(window);
            returnToPicker = window.ReturnToPicker;
            next = window.SwitchTo is { } file ? new Visit(file, window.GoToFragment, window.RestoreTo) : null;
        }

        if (!returnToPicker)
        {
            return next;
        }

        back.Clear();
        return StartPicker(app, recent) is { } picked ? new Visit(picked) : null;
    }

    // The recent-files picker, as the application starts with it. Returns the file chosen, or null when the user quits it.
    private static OpenedFile? StartPicker(IApplication app, RecentFiles? recent)
    {
        using var picker = new RecentPicker<OpenedFile>(app, recent, TryLoad, "Recent files", MdeditHints.Picker, openFile: true, fileTypes: FileTypes, itemsName: "files", errorTitle: "Open file");
        app.Run(picker);
        return picker.Opened;
    }

    // The picker shown over a running document. Esc returns to the document; Ctrl+Q quits the application.
    private static (PickOutcome Outcome, OpenedFile? File) ShowPicker(IApplication app, RecentFiles? recent)
    {
        using var picker = new RecentPicker<OpenedFile>(app, recent, TryLoad, "Open file", MdeditHints.OpenPicker, openFile: true, fileTypes: FileTypes, itemsName: "files", errorTitle: "Open file");
        app.Run(picker);

        if (picker.Opened is { } file)
        {
            return (PickOutcome.Chosen, file);
        }

        return (picker.QuitRequested ? PickOutcome.Quit : PickOutcome.Back, null);
    }
}
