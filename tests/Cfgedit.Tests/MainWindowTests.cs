using System.Text;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Cfgedit.Tests;

/// <summary>
/// The main window as drawn: the title with the format tag, the tree in its frame, the detail line of the focused part, and the key hints.
/// Keys go through the window, as the terminal sends them, and Ctrl+O and Ctrl+Q are checked against the picker's outcome.
/// </summary>
[Collection("Terminal GUI")]
public sealed class MainWindowTests
{
    private const string JsonText = "{\n  \"name\": \"web\",\n  \"port\": 8080\n}";

    private const string XmlText =
        "<configuration>\n  <appSettings>\n    <add key=\"a\" value=\"1\"/>\n    <add key=\"b\" value=\"2\"/>\n  </appSettings>\n</configuration>";

    private const string Hints = "Space Fold  \x2190\x2192 Part  ^O Open  ^Q Quit";

    [Fact]
    public void At_80_by_24_the_title_has_the_format_tag_the_frame_holds_the_tree_and_the_detail_and_hints_are_on_the_last_rows()
    {
        OpenedFile file = Opened("settings.json", JsonText);

        TerminalScreen screen = Run(file, 80, 24).Screens[0];

        Assert.Contains("settings.json [jsonc]", screen.Text(0), StringComparison.Ordinal);
        Assert.Equal("\x256D", screen.Cells[0][0].Grapheme);
        Assert.Equal("\x2570", screen.Cells[21][0].Grapheme);
        Assert.StartsWith("\x2502\x25BE {2}", screen.Text(1), StringComparison.Ordinal);
        Assert.Equal("$  object  2", screen.Text(22));
        Assert.Equal(Hints, screen.Text(23));
    }

    [Fact]
    public void At_40_by_12_the_hints_fit_and_the_detail_line_is_cut_at_the_width()
    {
        OpenedFile file = Opened("settings.json", JsonText);

        TerminalScreen screen = Run(file, 40, 12).Screens[0];

        Assert.Contains("settings.json [jsonc]", screen.Text(0), StringComparison.Ordinal);
        Assert.Equal("$  object  2", screen.Text(10));
        Assert.Equal(Hints, screen.Text(11));
    }

    [Fact]
    public void The_detail_line_shows_the_path_kind_and_value_of_a_json_key_and_then_its_value()
    {
        OpenedFile file = Opened("settings.json", JsonText);

        List<TerminalScreen> screens = Run(
            file,
            80,
            24,
            (_, window) => window.NewKeyDownEvent(Key.CursorDown),
            (_, window) => window.NewKeyDownEvent(Key.CursorRight)).Screens;

        Assert.Equal("$.name  key  name", screens[1].Text(22));
        Assert.Equal("$.name  string  web", screens[2].Text(22));
    }

    [Fact]
    public void The_detail_line_shows_the_xpath_like_path_of_an_xml_element_and_of_its_attribute_value_with_the_ordinal()
    {
        OpenedFile file = Opened("app.config", XmlText);

        List<TerminalScreen> screens = Run(
            file,
            80,
            24,
            (_, window) => window.NewKeyDownEvent(Key.CursorDown),
            (_, window) => window.NewKeyDownEvent(Key.CursorDown),
            (_, window) => window.NewKeyDownEvent(Key.CursorDown),
            (_, window) => window.NewKeyDownEvent(Key.End)).Screens;

        Assert.Equal("/configuration/appSettings/add[2]  element  add", screens[3].Text(22));
        Assert.Equal("/configuration/appSettings/add[2]/@value  attribute  2", screens[4].Text(22));
        Assert.Contains("app.config [xml]", screens[4].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_O_shows_the_picker_and_a_file_chosen_there_becomes_the_next_document()
    {
        OpenedFile first = Opened("first.json", JsonText);
        OpenedFile second = Opened("second.xml", XmlText);
        int pickers = 0;

        Session session = Run(
            first,
            80,
            24,
            () => (pickers++ == 0 ? PickOutcome.Chosen : PickOutcome.Back, second),
            (_, window) => window.NewKeyDownEvent(Key.O.WithCtrl));

        Assert.Equal(1, pickers);
        Assert.Same(second, session.Window.SwitchTo);
    }

    [Fact]
    public void Ctrl_O_then_esc_in_the_picker_keeps_the_document_and_chooses_nothing()
    {
        OpenedFile file = Opened("settings.json", JsonText);
        int pickers = 0;

        Session session = Run(
            file,
            80,
            24,
            () =>
            {
                pickers++;
                return (PickOutcome.Back, null);
            },
            (_, window) => window.NewKeyDownEvent(Key.O.WithCtrl));

        Assert.Equal(1, pickers);
        Assert.Null(session.Window.SwitchTo);
        Assert.Equal("$  object  2", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Ctrl_Q_stops_the_window_with_no_switch_and_does_not_show_the_picker()
    {
        OpenedFile file = Opened("settings.json", JsonText);
        int pickers = 0;

        Session session = Run(
            file,
            80,
            24,
            () =>
            {
                pickers++;
                return (PickOutcome.Back, null);
            },
            (_, window) => window.NewKeyDownEvent(Key.Q.WithCtrl));

        Assert.Equal(0, pickers);
        Assert.Null(session.Window.SwitchTo);
    }

    // ----- Helpers -----

    private static OpenedFile Opened(string name, string text)
    {
        IConfigFormat format = Formats.ForPath(name) ?? throw new ArgumentException(name, nameof(name));
        return new OpenedFile(Path.Combine(Path.GetTempPath(), name), ConfigDocument.Load(Encoding.UTF8.GetBytes(text), null, format));
    }

    /// <summary>Runs the window at a size, with the picker answering Back. Each step gets the window; the screens are one after the first draw and one after each step.</summary>
    private static Session Run(OpenedFile file, int width, int height, params Action<IApplication, MainWindow>[] steps) =>
        Run(file, width, height, () => (PickOutcome.Back, null), steps);

    private static Session Run(
        OpenedFile file,
        int width,
        int height,
        Func<(PickOutcome Outcome, OpenedFile? File)> pick,
        params Action<IApplication, MainWindow>[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);
        using var window = new MainWindow(file, pick);
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
