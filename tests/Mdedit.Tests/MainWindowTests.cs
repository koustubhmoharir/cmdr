using System.Text;
using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The main window as drawn: the frame titled with the file name, the rendered lines, the message line with the heading path and the
/// scroll position, the key hints, and the keys that quit, switch and are swallowed.
/// </summary>
[Collection("Terminal GUI")]
public sealed partial class MainWindowTests : IDisposable
{
    private const string Small = "# Install\n\nIntro text.\n\n## Linux\n\n- step one\n- step two\n";

    // A longer document, so that the view scrolls: the rows of its headings are 0, 4 (Linux) and 9 (Windows) at width 40.
    private const string Long = "# Install\n\nIntro text.\n\n## Linux\n\n- step one\n- step two\n\n## Windows\n\n- item a\n- item b\n- item c\n- item d\n- item e\n- item f\n";

    private readonly List<string> _folders = [];

    [Fact]
    public void At_40_by_12_the_frame_is_titled_with_the_file_name_and_holds_the_rendered_lines()
    {
        OpenedFile file = Opened("notes.md", Small);

        TerminalScreen screen = Run(file, 40, 12).Screens[0];

        Assert.Contains("notes.md", screen.Text(0), StringComparison.Ordinal);
        Assert.Equal("\x256D", screen.Cells[0][0].Grapheme);
        Assert.Equal("\x2570", screen.Cells[9][0].Grapheme);
        // The frame is 40 columns: a border, 38 columns of text and a border. A heading has its fold glyph before its text.
        Assert.Equal(Framed("\u25BE Install"), screen.Text(1));
        Assert.Equal(Framed("  Intro text."), screen.Text(3));
        Assert.Equal(Framed("\u25BE Linux"), screen.Text(5));
        Assert.Equal(Framed("  \u2022 step one"), screen.Text(7));
    }

    [Fact]
    public void The_message_line_shows_the_heading_path_and_All_when_the_document_fits()
    {
        OpenedFile file = Opened("notes.md", Small);

        TerminalScreen screen = Run(file, 40, 12).Screens[0];

        Assert.Equal(Status("Install", "All"), screen.Text(10));
    }

    [Fact]
    public void The_hints_line_shows_Source_Save_Find_and_Copy_at_the_left_and_Quit_at_the_right_edge()
    {
        OpenedFile file = Opened("notes.md", Small);

        TerminalScreen screen = Run(file, 40, 12).Screens[0];

        // At 40 columns the row is cut inside Copy; the cut hint keeps its first characters and the quit key stays at the right.
        Assert.Equal(Status("^T Source  ^S Save  ^F Find  ^C …", "^Q Quit"), screen.Text(11));
    }

    [Fact]
    public void The_heading_path_is_the_chain_of_headings_above_the_top_line_and_a_deeper_section_replaces_its_level()
    {
        OpenedFile file = Opened("notes.md", Long);

        List<TerminalScreen> screens = Run(
            file,
            40,
            12,
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown }),
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown }),
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown })).Screens;

        // The blank line after the intro belongs to Install, so the path after one wheel is Install alone. The first step (6 lines down)
        // is under Linux, and the Windows heading (9 lines down) replaces Linux, which has the same level.
        Assert.DoesNotContain("Linux", screens[0].Text(10), StringComparison.Ordinal);
        Assert.StartsWith("Install ", screens[1].Text(10), StringComparison.Ordinal);
        Assert.DoesNotContain("Linux", screens[1].Text(10), StringComparison.Ordinal);
        Assert.StartsWith("Install \u203A Linux", screens[2].Text(10), StringComparison.Ordinal);
        Assert.StartsWith("Install \u203A Windows", screens[3].Text(10), StringComparison.Ordinal);
    }

    [Fact]
    public void The_message_line_shows_a_percentage_in_the_middle_and_Bot_at_the_end()
    {
        OpenedFile file = Opened("notes.md", Long);

        List<TerminalScreen> screens = Run(
            file,
            40,
            12,
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown }),
            (_, window) =>
            {
                for (int i = 0; i < 10; i++)
                {
                    window.View.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown });
                }
            }).Screens;

        Assert.EndsWith("Top", screens[0].Text(10), StringComparison.Ordinal);
        Assert.EndsWith("%", screens[1].Text(10), StringComparison.Ordinal);
        Assert.EndsWith("Bot", screens[2].Text(10), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 20, 8, "Top")]
    [InlineData(5, 8, 8, "All")]
    [InlineData(3, 20, 8, "25%")]
    [InlineData(12, 20, 8, "Bot")]
    [InlineData(4, 4, 8, "All")]
    public void The_scroll_position_is_All_Top_Bot_or_a_percentage(int top, int lines, int page, string expected)
    {
        Assert.Equal(expected, MainWindow.ScrollPosition(top, lines, page));
    }

    [Fact]
    public void A_message_replaces_the_status_until_its_expiry_and_the_next_key_clears_it()
    {
        OpenedFile file = Opened("notes.md", Small);
        Action? expire = null;
        var delays = new List<TimeSpan>();

        List<TerminalScreen> screens = RunScheduled(
            file,
            40,
            12,
            () => (PickOutcome.Back, null),
            (delay, action) =>
            {
                delays.Add(delay);
                expire = action;
                return () => { };
            },
            (_, window) => window.ShowMessage("Copied text", error: false),
            (_, _) => expire!(),
            (_, window) => window.ShowMessage("Too large to copy", error: true),
            (_, window) => window.NewKeyDownEvent(Key.CursorDown)).Screens;

        Assert.Equal("Copied text", screens[1].Text(10));
        Assert.Equal(Status("Install", "All"), screens[2].Text(10));
        Assert.Equal("Too large to copy", screens[3].Text(10));
        Assert.Equal(Status("Install", "All"), screens[4].Text(10));
        Assert.Equal([MainWindow.MessageDuration, MainWindow.MessageDuration], delays);
    }

    [Fact]
    public void Ctrl_Q_stops_the_window_with_no_switch()
    {
        OpenedFile file = Opened("notes.md", Small);
        int pickers = 0;

        Session session = Run(
            file,
            40,
            12,
            () =>
            {
                pickers++;
                return (PickOutcome.Back, null);
            },
            (_, window) => window.NewKeyDownEvent(Key.Q.WithCtrl));

        Assert.True(session.Window.Stopped);
        Assert.Null(session.Window.SwitchTo);
        Assert.Equal(0, pickers);
    }

    [Fact]
    public void At_80_columns_the_hints_are_Source_Save_Find_Copy_Fold_Outline_and_Open_at_the_left_and_Quit_at_the_right_edge()
    {
        OpenedFile file = Opened("notes.md", Small);

        TerminalScreen screen = Run(file, 80, 12).Screens[0];

        string left = "^T Source  ^S Save  ^F Find  ^C Copy  Alt+F Fold  Alt+O Outline  ^O Open";
        Assert.Equal(left + new string(' ', 80 - left.Length - "^Q Quit".Length) + "^Q Quit", screen.Text(11));
    }

    [Fact]
    public void Esc_is_swallowed_and_the_window_keeps_running()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(file, 40, 12, (_, window) => window.NewKeyDownEvent(Key.Esc));

        Assert.False(session.Window.Stopped);
        Assert.Equal(Status("Install", "All"), session.Screens[^1].Text(10));
    }

    [Fact]
    public void Ctrl_O_then_a_file_chosen_in_the_picker_becomes_the_next_document()
    {
        OpenedFile first = Opened("first.md", Small);
        OpenedFile second = Opened("second.md", Long);
        int pickers = 0;

        Session session = Run(
            first,
            40,
            12,
            () => (pickers++ == 0 ? PickOutcome.Chosen : PickOutcome.Back, second),
            (_, window) => window.NewKeyDownEvent(Key.O.WithCtrl));

        Assert.Equal(1, pickers);
        Assert.Same(second, session.Window.SwitchTo);
        Assert.True(session.Window.Stopped);
    }

    [Fact]
    public void Ctrl_O_then_esc_in_the_picker_keeps_the_document()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(
            file,
            40,
            12,
            () => (PickOutcome.Back, null),
            (_, window) => window.NewKeyDownEvent(Key.O.WithCtrl));

        Assert.Null(session.Window.SwitchTo);
        Assert.False(session.Window.Stopped);
    }

    // ----- Helpers -----

    // A row of the 40-column frame: the border, the text cut or padded to 38 columns, and the border.
    private static string Framed(string inner) => "\u2502" + inner.PadRight(38) + "\u2502";

    // A message line of the 40-column window: the left text, then the right text at the edge.
    private static string Status(string left, string right) => left + new string(' ', 40 - left.Length - right.Length) + right;

    private OpenedFile Opened(string name, string text)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mdedit-window-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _folders.Add(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return MdeditHost.Load(path);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (string dir in _folders)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static Session Run(OpenedFile file, int width, int height, params Action<IApplication, MainWindow>[] steps) =>
        Run(file, width, height, () => (PickOutcome.Back, null), steps);

    private static Session Run(
        OpenedFile file,
        int width,
        int height,
        Func<(PickOutcome Outcome, OpenedFile? File)> pick,
        params Action<IApplication, MainWindow>[] steps) =>
        RunScheduled(file, width, height, pick, (_, _) => () => { }, steps);

    // The window with a scheduler the test drives: each scheduled action is kept for the test to run.
    private static Session RunScheduled(
        OpenedFile file,
        int width,
        int height,
        Func<(PickOutcome Outcome, OpenedFile? File)> pick,
        Func<TimeSpan, Action, Action> schedule,
        params Action<IApplication, MainWindow>[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);
        using var window = new MainWindow(file, pick, schedule);
        List<TerminalScreen> screens = TerminalHarness.RunWindow(
            app,
            window,
            width,
            height,
            steps.Select(step => (Action)(() => step(app, window))).ToArray());
        return new Session(screens, window);
    }

    private sealed record Session(List<TerminalScreen> Screens, MainWindow Window);
}
