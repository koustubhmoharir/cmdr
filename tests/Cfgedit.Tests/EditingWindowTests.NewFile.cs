using System.Text;
using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Shared.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cfgedit.Tests;

/// <summary>
/// The new-file flow: the Create prompt, the format choice, the XML root name, and the first save that creates the file, with the recent
/// list recorded only then. Temporary folders and a temporary recent store are used.
/// </summary>
public sealed partial class EditingWindowTests
{
    // ----- The questions -----

    [Fact]
    public void Cancelling_the_create_prompt_creates_nothing_and_asks_nothing_more()
    {
        string path = NewPath("new.json");
        NewFileChoice? choice = null;
        bool asked = false;

        RunApp(
            80,
            24,
            app =>
            {
                using var create = new DialogWatch(app, "the create prompt", IsDialogTitled("New file"), _ => asked = true, "Cancel");
                choice = CfgeditHost.AskNewFile(app, path, null);
                create.Finish();
            });

        Assert.True(asked);
        Assert.Null(choice);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Creating_a_JSON_file_gives_the_jsonc_format_from_the_extension_and_still_creates_nothing()
    {
        string path = NewPath("new.json");
        NewFileChoice? choice = null;

        RunApp(
            80,
            24,
            app =>
            {
                using var create = new DialogWatch(app, "the create prompt", IsDialogTitled("New file"), null, "Create");
                choice = CfgeditHost.AskNewFile(app, path, null);
                create.Finish();
            });

        Assert.NotNull(choice);
        Assert.Equal("jsonc", choice!.Format.Name);
        Assert.Null(choice.RootName);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_file_with_an_unknown_extension_asks_for_its_format_and_XML_asks_for_the_root_name_until_it_is_valid()
    {
        string path = NewPath("app.conf");
        NewFileChoice? choice = null;
        string? invalidError = null;
        string? formatTitle = null;

        RunApp(
            80,
            24,
            app =>
            {
                using var create = new DialogWatch(app, "the create prompt", IsDialogTitled("New file"), null, "Create");
                using var format = new DialogWatch(
                    app,
                    "the format choice",
                    v => v is ChoiceDialog,
                    v =>
                    {
                        formatTitle = ((ChoiceDialog)v).Title;
                        PickItem(3)(v);
                    },
                    null);
                using var root = new DialogWatch(
                    app,
                    "the root name dialog",
                    v => v is TextPromptDialog,
                    v =>
                    {
                        var prompt = (TextPromptDialog)v;
                        prompt.Field.Text = "1bad";
                        PressButton(v, "OK");
                        invalidError = prompt.ErrorText;
                        prompt.Field.Text = "config";
                    },
                    "OK");
                choice = CfgeditHost.AskNewFile(app, path, null);
                create.Finish();
                format.Finish();
                root.Finish();
            });

        Assert.Equal("Format of app.conf", formatTitle);
        Assert.Contains("'1bad' is not a valid XML name", invalidError, StringComparison.Ordinal);
        Assert.NotNull(choice);
        Assert.Equal("xml", choice!.Format.Name);
        Assert.Equal("config", choice.RootName);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Cancelling_the_format_choice_creates_nothing()
    {
        string path = NewPath("app.conf");
        NewFileChoice? choice = new(Formats.FromName("json")!, null);

        RunApp(
            80,
            24,
            app =>
            {
                using var create = new DialogWatch(app, "the create prompt", IsDialogTitled("New file"), null, "Create");
                using var format = new DialogWatch(app, "the format choice", v => v is ChoiceDialog, v => v.NewKeyDownEvent(Key.Esc), null);
                choice = CfgeditHost.AskNewFile(app, path, null);
                create.Finish();
                format.Finish();
            });

        Assert.Null(choice);
        Assert.False(File.Exists(path));
    }

    // ----- The window and the first save -----

    [Fact]
    public void The_new_document_is_dirty_and_the_first_save_writes_the_exact_JSON_bytes()
    {
        string path = NewPath("new.json");
        var file = new OpenedFile(path, DocumentFile.CreateNew(path, Formats.FromName("json")!, null));

        Session session = RunWith(file, 80, 24, null, (_, w) => Keys(w, Key.S.WithCtrl));

        // The title carries the star until the first save, and the file is written with the exact bytes of the initial text.
        Assert.Contains("new.json* [json]", session.Screens[0].Text(0), StringComparison.Ordinal);
        Assert.Contains("new.json [json]", session.Screens[^1].Text(0), StringComparison.Ordinal);
        Assert.Equal(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes("{}" + Environment.NewLine), File.ReadAllBytes(path));
        Assert.Equal("Saved", session.Screens[^1].Text(22));
    }

    [Fact]
    public void The_first_save_writes_the_XML_declaration_and_the_root_element_with_a_final_newline()
    {
        string path = NewPath("app.config");
        var file = new OpenedFile(path, DocumentFile.CreateNew(path, Formats.FromName("xml")!, "config"));

        RunWith(file, 80, 24, null, (_, w) => Keys(w, Key.S.WithCtrl));

        string newline = Environment.NewLine;
        string expected = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + newline + "<config />" + newline;
        Assert.Equal(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(expected), File.ReadAllBytes(path));
    }

    [Fact]
    public void The_recent_list_gets_the_file_only_after_its_first_save()
    {
        string folder = NewFolder();
        string path = Path.Combine(folder, "new.json");
        string store = Path.Combine(folder, "recent.json");
        var file = new OpenedFile(path, DocumentFile.CreateNew(path, Formats.FromName("json")!, null));
        Action recordOnSave = CfgeditHost.RecordOnFirstSave(path, () => RecentFiles.Load(store));

        RunWith(
            file,
            80,
            24,
            recordOnSave,
            (_, _) => Assert.Empty(RecentFiles.Load(store).Files),
            (_, w) => Keys(w, Key.S.WithCtrl),
            (_, _) => Assert.Equal(new[] { Path.GetFullPath(path) }, RecentFiles.Load(store).Files));
    }

    [Fact]
    public void A_save_that_fails_for_a_missing_folder_keeps_the_file_unwritten_and_unrecorded()
    {
        string folder = NewFolder();
        string path = Path.Combine(folder, "missing", "new.json");
        string store = Path.Combine(folder, "recent.json");
        var file = new OpenedFile(path, DocumentFile.CreateNew(path, Formats.FromName("json")!, null));

        Session session = RunWith(
            file,
            80,
            24,
            CfgeditHost.RecordOnFirstSave(path, () => RecentFiles.Load(store)),
            (_, w) => Keys(w, Key.S.WithCtrl));

        Assert.False(File.Exists(path));
        Assert.Empty(RecentFiles.Load(store).Files);
        Assert.Contains("does not exist", session.Screens[^1].Text(22), StringComparison.Ordinal);
    }

    // ----- Helpers -----

    private string NewFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid():N}"[..16]);
        Directory.CreateDirectory(dir);
        _folders.Add(dir);
        return dir;
    }

    private string NewPath(string name) => Path.Combine(NewFolder(), name);

    // Runs a window over a file that may be unsaved, with the first-save action. The steps run in order, as in Run.
    private static Session RunWith(
        OpenedFile file,
        int width,
        int height,
        Action? onFirstSave,
        params Action<IApplication, MainWindow>[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);
        using var window = new MainWindow(file, () => (PickOutcome.Back, null), null, onFirstSave);
        List<TerminalScreen> screens = TerminalHarness.RunWindow(
            app,
            window,
            width,
            height,
            steps.Select(step => (Action)(() => step(app, window))).ToArray());
        return new Session(screens, window);
    }

    // Runs a blank main window, so that the steps can open dialogs on the running application.
    private static void RunApp(int width, int height, Action<IApplication> step)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);
        using var window = new Window();
        TerminalHarness.RunWindow(app, window, width, height, () => step(app));
    }
}
