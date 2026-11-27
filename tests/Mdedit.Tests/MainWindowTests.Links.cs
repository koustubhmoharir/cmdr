using System.Text;
using Mdedit.Tui;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The links of the window: the message line shows the caret's link URL, Tab and Shift+Tab move between links with their messages, Ctrl+Enter and a
/// Ctrl+click follow a link (an anchor goes to its heading, an external URL is copied, a file opens in a new window after the unsaved-changes
/// flow), and Alt+Left goes back to the file a followed link left, at its caret and top line. Dialogs are answered by <see cref="DialogWatch"/>.
/// </summary>
public sealed partial class MainWindowTests
{
    // Four links: two anchors, an external URL and a mailto, and the section that hides the third is folded in some tests.
    private const string Linked = "# Guide\n\nSee [one](https://a.example) and [two](#guide).\n\n## Section\n\nMore [three](mailto:x@y.z) here.\n\n# Last\n\nEnd [four](#section).\n";

    // The file of a file link, with a heading that a fragment names.
    private const string Other = "# Other\n\nIntro.\n\n## Part\n\nPart text.\n";

    // A tall file whose link is far below the first screen, so that a Back can restore a top line away from the caret.
    private static readonly string TallNotes = string.Concat(Enumerable.Repeat("Filler line.\n\n", 15)) + "See [other](other.md#part) here.\n\n" + string.Concat(Enumerable.Repeat("More filler.\n\n", 15));

    [Fact]
    public void The_message_line_shows_the_URL_of_the_link_under_the_caret_and_the_heading_path_off_it()
    {
        OpenedFile file = Opened("notes.md", Linked);

        List<TerminalScreen> screens = RunLinkedWith(
            file,
            steps:
            [
                (_, window) => window.View.PlaceCaret(Index(Linked, "one"), null),
                (_, window) => window.View.PlaceCaret(Index(Linked, "Section"), null),
            ]).Screens;

        Assert.StartsWith("→ https://a.example", screens[1].Text(10), StringComparison.Ordinal);
        Assert.DoesNotContain("→", screens[2].Text(10), StringComparison.Ordinal);
        Assert.StartsWith("Guide", screens[2].Text(10), StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_moves_to_the_next_link_and_shows_its_URL()
    {
        OpenedFile file = Opened("notes.md", Linked);

        List<TerminalScreen> screens = RunLinked(file, (_, window) => window.NewKeyDownEvent(Key.Tab)).Screens;

        Assert.StartsWith("→ https://a.example", screens[^1].Text(10), StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_after_the_last_link_wraps_to_the_first_with_its_message()
    {
        OpenedFile file = Opened("notes.md", Linked);

        Session session = RunLinked(
            file,
            (_, window) => window.View.PlaceCaret(Index(Linked, "four"), null),
            (_, window) => window.NewKeyDownEvent(Key.Tab));
        List<TerminalScreen> screens = session.Screens;
        Assert.Equal(Index(Linked, "one"), session.Window.View.CursorOffset);

        Assert.Equal("Wrapped to the first link", screens[^1].Text(10));
    }

    [Fact]
    public void Shift_Tab_before_the_first_link_wraps_to_the_last_with_its_message()
    {
        OpenedFile file = Opened("notes.md", Linked);

        Session session = RunLinked(file, (_, window) => window.NewKeyDownEvent(Key.Tab.WithShift));

        Assert.Equal("Wrapped to the last link", session.Screens[^1].Text(10));
        Assert.Equal(Index(Linked, "four"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void Tab_in_a_document_with_no_link_says_No_links()
    {
        OpenedFile file = Opened("notes.md", Small);

        List<TerminalScreen> screens = RunLinked(file, (_, window) => window.NewKeyDownEvent(Key.Tab)).Screens;

        Assert.Equal("No links", screens[^1].Text(10));
    }

    [Fact]
    public void Ctrl_Enter_on_an_anchor_link_moves_the_caret_to_the_heading()
    {
        OpenedFile file = Opened("notes.md", Linked);

        Session session = RunLinked(
            file,
            (_, window) => window.View.PlaceCaret(Index(Linked, "two"), null),
            (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal(Index(Linked, "Guide"), session.Window.View.CursorOffset);
        Assert.False(session.Window.Stopped);
    }

    [Fact]
    public void Ctrl_Enter_on_an_anchor_link_unfolds_the_section_that_hides_the_heading()
    {
        OpenedFile file = Opened("notes.md", Linked);

        Session session = RunLinked(
            file,
            (_, window) => window.View.PlaceCaret(Index(Linked, "Guide"), null),
            (_, window) => window.View.HandleKey(RenderedView.ToggleFoldKey),
            (_, window) => window.View.PlaceCaret(Index(Linked, "four"), null),
            (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal(Index(Linked, "Section"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void An_anchor_to_a_heading_that_does_not_exist_says_so_in_the_error_style()
    {
        OpenedFile file = Opened("notes.md", "# Top\n\nSee [lost](#nowhere).\n");

        Session session = RunLinked(
            file,
            (_, window) => window.View.PlaceCaret(Index("# Top\n\nSee [lost](#nowhere).\n", "lost"), null),
            (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal("No heading #nowhere", session.Screens[^1].Text(10));
        Assert.True(session.Window.Message.HasMessage);
    }

    [Fact]
    public void Ctrl_Enter_on_an_external_link_copies_its_URL_with_OSC_52_and_says_so()
    {
        const string text = "See [site](https://example.com/a).\n";
        OpenedFile file = Opened("notes.md", text);
        var written = new List<string>();

        Session session = RunLinked(
            file,
            (_, window) =>
            {
                window.WriteRaw = written.Add;
                window.View.PlaceCaret(Index(text, "site"), null);
            },
            (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal([Osc52.Build("https://example.com/a")], written);
        Assert.Equal("Copied link: https://example.com/a", session.Screens[^1].Text(10));
        Assert.False(session.Window.Stopped);
    }

    [Fact]
    public void Ctrl_Enter_on_a_link_with_no_destination_says_Empty_link_and_copies_nothing()
    {
        const string text = "See [nothing]().\n";
        OpenedFile file = Opened("notes.md", text);
        var written = new List<string>();

        Session session = RunLinked(
            file,
            (_, window) =>
            {
                window.WriteRaw = written.Add;
                window.View.PlaceCaret(Index(text, "nothing"), null);
            },
            (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Empty(written);
        Assert.Equal("Empty link", session.Screens[^1].Text(10));
    }

    [Fact]
    public void Ctrl_click_on_an_anchor_link_follows_it_and_a_plain_click_only_places_the_caret()
    {
        OpenedFile file = Opened("notes.md", Linked);

        Session session = RunLinked(
            file,
            (_, window) => window.View.PlaceCaret(Index(Linked, "two"), null),
            (_, window) => window.View.HandleMouse(CaretClick(window.View, MouseFlags.None)),
            (_, window) => window.View.PlaceCaret(Index(Linked, "two"), null),
            (_, window) => window.View.HandleMouse(CaretClick(window.View, MouseFlags.Ctrl)));

        Assert.Equal(Index(Linked, "Guide"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void A_plain_click_on_a_link_leaves_the_caret_on_the_link()
    {
        OpenedFile file = Opened("notes.md", Linked);

        Session session = RunLinked(
            file,
            (_, window) => window.View.PlaceCaret(Index(Linked, "two"), null),
            (_, window) => window.View.HandleMouse(CaretClick(window.View, MouseFlags.None)));

        Assert.Equal(Index(Linked, "two"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void A_file_link_to_a_file_that_does_not_exist_says_not_found_and_the_window_stays()
    {
        string dir = NewFolder();
        string notes = Write(dir, "notes.md", "See [gone](gone.md).\n");
        string missing = Path.Combine(dir, "gone.md");

        Session session = RunLinked(
            MdeditHost.Load(notes),
            (_, window) => window.View.PlaceCaret(Index("See [gone](gone.md).\n", "gone"), null),
            (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal("Not found: gone.md", session.Screens[^1].Text(10));
        Assert.False(File.Exists(missing));
        Assert.False(session.Window.Stopped);
        Assert.Null(session.Window.SwitchTo);
    }

    [Fact]
    public void A_file_link_with_unsaved_changes_asks_and_Cancel_keeps_the_document()
    {
        const string text = "See [other](other.md) here.\n";
        string dir = NewFolder();
        string notesPath = Write(dir, "notes.md", text);
        Write(dir, "other.md", Other);
        var back = new BackStack();
        string? question = null;

        Session session = RunLinkedWith(
            MdeditHost.Load(notesPath),
            back: back,
            steps:
            [
                (app, window) =>
                {
                    using var prompt = new DialogWatch(app, "the unsaved question", IsDialogTitled("Open"), v => question = MessageOf(v), "Cancel");
                    window.NewKeyDownEvent(Key.T.WithCtrl);
                    Type(window, "x");
                    window.NewKeyDownEvent(Key.T.WithCtrl);
                    window.View.PlaceCaret(Index(text, "other"), null);
                    window.NewKeyDownEvent(Key.Enter.WithCtrl);
                    prompt.Finish();
                },
            ]);

        Assert.Contains("Save changes to notes.md", question, StringComparison.Ordinal);
        Assert.False(session.Window.Stopped);
        Assert.Null(session.Window.SwitchTo);
        Assert.Equal(0, back.Count);
        Assert.Contains("notes.md *", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_link_opens_the_file_in_a_new_window_and_pushes_the_place_it_left()
    {
        string dir = NewFolder();
        string notesPath = Write(dir, "notes.md", "See [other](other.md#part) here.\n");
        Write(dir, "other.md", Other);
        var back = new BackStack();
        const string text = "See [other](other.md#part) here.\n";

        Session session = RunLinkedWith(
            MdeditHost.Load(notesPath),
            back: back,
            steps: [(_, window) => window.View.PlaceCaret(Index(text, "other"), null), (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl)]);

        Assert.True(session.Window.Stopped);
        Assert.Equal(Path.Combine(dir, "other.md"), session.Window.SwitchTo!.Path);
        Assert.Equal("part", session.Window.GoToFragment);
        Assert.Null(session.Window.RestoreTo);
        Assert.Equal(new BackEntry(Path.GetFullPath(notesPath), Index(text, "other"), 0), back.Peek());
    }

    [Fact]
    public void The_fragment_of_a_file_link_puts_the_new_window_on_its_heading()
    {
        const string text = "See [other](other.md#part) here.\n";
        string dir = NewFolder();
        string notesPath = Write(dir, "notes.md", text);
        Write(dir, "other.md", Other);

        Session follow = RunLinkedWith(
            MdeditHost.Load(notesPath),
            back: new BackStack(),
            steps: [(_, window) => window.View.PlaceCaret(Index(text, "other"), null), (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl)]);
        Session next = RunLinkedWith(follow.Window.SwitchTo!, fragment: follow.Window.GoToFragment);

        Assert.Equal(Index(Other, "Part"), next.Window.View.CursorOffset);
    }

    [Fact]
    public void A_fragment_that_names_no_heading_in_the_new_window_says_so()
    {
        string dir = NewFolder();
        string otherPath = Write(dir, "other.md", Other);

        Session next = RunLinkedWith(MdeditHost.Load(otherPath), fragment: "nowhere");

        Assert.True(next.Window.Message.HasMessage);
        Assert.Equal(MainWindow.NoHeading("nowhere"), next.Screens[^1].Text(10));
    }

    [Fact]
    public void Alt_Left_goes_back_to_the_file_at_the_caret_and_top_line_the_link_left()
    {
        string dir = NewFolder();
        string notesPath = Write(dir, "notes.md", TallNotes);
        Write(dir, "other.md", Other);
        var back = new BackStack();
        int link = Index(TallNotes, "other");

        // Follow the link, with the view scrolled down a wheel step so that the top line is not the caret's line.
        Session first = RunLinkedWith(
            MdeditHost.Load(notesPath),
            back: back,
            steps:
            [
                (_, window) => window.View.PlaceCaret(link, null),
                (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown }),
                (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl),
            ]);
        BackEntry entry = back.Peek()!;
        Assert.NotEqual(entry.CaretOffset, entry.TopOffset);

        Session second = RunLinkedWith(
            first.Window.SwitchTo!,
            back: back,
            steps: [(_, window) => window.NewKeyDownEvent(Key.CursorLeft.WithAlt)]);

        Assert.True(second.Window.Stopped);
        Assert.Equal(Path.GetFullPath(notesPath), second.Window.SwitchTo!.Path);
        Assert.Same(entry, second.Window.RestoreTo);
        Assert.Equal(0, back.Count);

        Session third = RunLinkedWith(second.Window.SwitchTo!, restore: second.Window.RestoreTo);

        Assert.Equal(entry.CaretOffset, third.Window.View.CursorOffset);
        Assert.Equal(entry.TopOffset, third.Window.View.TopOffset);
    }

    [Fact]
    public void Alt_Left_with_no_file_to_go_back_to_says_No_previous_file()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = RunLinkedWith(file, back: new BackStack(), steps: [(_, window) => window.NewKeyDownEvent(Key.CursorLeft.WithAlt)]);

        Assert.False(session.Window.Stopped);
        Assert.Equal("No previous file", session.Screens[^1].Text(10));
    }

    [Fact]
    public void Alt_Left_to_a_file_that_no_longer_loads_says_so_and_drops_the_entry()
    {
        string dir = NewFolder();
        string bad = Write(dir, "bad.md", "# Broken\n");
        File.WriteAllBytes(bad, [(byte)'a', 0xFF]);
        var back = new BackStack();
        back.Push(new BackEntry(bad, 0, 0));
        OpenedFile file = Opened("notes.md", Small);

        Session session = RunLinkedWith(file, back: back, width: 120, steps: [(_, window) => window.NewKeyDownEvent(Key.CursorLeft.WithAlt)]);

        Assert.False(session.Window.Stopped);
        Assert.Equal(0, back.Count);
        Assert.Contains($"{Path.GetFullPath(bad)}:1:1: not valid UTF-8", session.Screens[^1].Text(10), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_link_to_a_file_that_does_not_load_keeps_the_window_and_pushes_nothing()
    {
        string dir = NewFolder();
        string notesPath = Write(dir, "notes.md", "See [bad](bad.md).\n");
        File.WriteAllBytes(Path.Combine(dir, "bad.md"), [(byte)'a', 0xFF]);
        var back = new BackStack();

        Session session = RunLinkedWith(
            MdeditHost.Load(notesPath),
            back: back,
            width: 120,
            steps: [(_, window) => window.View.PlaceCaret(Index("See [bad](bad.md).\n", "bad"), null), (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl)]);

        Assert.False(session.Window.Stopped);
        Assert.Null(session.Window.SwitchTo);
        Assert.Equal(0, back.Count);
        Assert.Contains($"{Path.Combine(dir, "bad.md")}:1:1: not valid UTF-8", session.Screens[^1].Text(10), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_O_with_a_file_chosen_clears_the_back_stack()
    {
        OpenedFile file = Opened("notes.md", Small);
        OpenedFile second = Opened("second.md", Long);
        var back = new BackStack();
        back.Push(new BackEntry(file.Path, 0, 0));

        Session session = RunLinkedWith(
            file,
            pick: () => (PickOutcome.Chosen, second),
            back: back,
            steps: [(_, window) => window.NewKeyDownEvent(Key.O.WithCtrl)]);

        Assert.Same(second, session.Window.SwitchTo);
        Assert.Equal(0, back.Count);
    }

    // The window of a link test at 40 by 12, with the steps of the test.
    private static Session RunLinked(OpenedFile file, params Action<IApplication, MainWindow>[] steps) => RunLinkedWith(file, steps: steps);

    // The same window, with the picker, the back stack, the fragment and the restore point that the test gives it.
    private static Session RunLinkedWith(
        OpenedFile file,
        Func<(PickOutcome Outcome, OpenedFile? File)>? pick = null,
        BackStack? back = null,
        string? fragment = null,
        BackEntry? restore = null,
        int width = 40,
        Action<IApplication, MainWindow>[]? steps = null)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, 12);
        using var window = new MainWindow(
            file,
            pick ?? (() => (PickOutcome.Back, null)),
            (_, _) => () => { },
            MainWindow.LargeFileBytes,
            onFirstSave: null,
            loadFile: null,
            back: back,
            fragment: fragment,
            restore: restore);
        List<TerminalScreen> screens = TerminalHarness.RunWindow(
            app,
            window,
            width,
            12,
            (steps ?? []).Select(step => (Action)(() => step(app, window))).ToArray());
        return new Session(screens, window);
    }

    // A file written into a folder of the test, as UTF-8 without a byte order mark; returns its path.
    private static string Write(string dir, string name, string text)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    // A press at the caret's cell, as a click on it would be, with the modifiers given.
    private static Mouse CaretClick(RenderedView view, MouseFlags modifiers) =>
        new() { Flags = MouseFlags.LeftButtonPressed | modifiers, Position = new Point(view.CursorColumn, view.CursorLine - view.TopLine) };
}
