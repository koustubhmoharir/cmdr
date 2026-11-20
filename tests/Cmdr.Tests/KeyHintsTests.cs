using System.Reflection;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Cmdr.Tests;

/// <summary>
/// The key hints as data: the builders, the label of each key, and the text each state of the bar shows, which must not change
/// when the labels became click targets.
/// </summary>
[Collection("Terminal GUI")]
public sealed class KeyHintsTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Search_labels_map_to_their_keys_and_the_arrows_are_targets_of_their_own()
    {
        Assert.Equal(
            new[]
            {
                ("F3 Next", Key.F3),
                ("⇧F3 Prev", Key.F3.WithShift),
                ("↑", Key.CursorUp),
                ("↓", Key.CursorDown),
                ("Alt+C Case", Key.C.WithAlt),
                ("Alt+W Word", Key.W.WithAlt),
                ("Alt+R Regex", Key.R.WithAlt),
                ("Enter Pane", Key.Enter),
                ("Esc Clear", Key.Esc),
            },
            Labels(SearchHint.Build(hasText: true)));
        Assert.Equal(("Esc Close", Key.Esc), Labels(SearchHint.Build(hasText: false)).Last());
    }

    [Fact]
    public void The_search_text_is_unchanged_and_History_is_plain_text()
    {
        Assert.Equal("F3 Next  ⇧F3 Prev  ↑↓ History  Alt+C Case  Alt+W Word  Alt+R Regex  Enter Pane  Esc Clear", SearchHint.Describe(hasText: true));
        Assert.Equal("F3 Next  ⇧F3 Prev  ↑↓ History  Alt+C Case  Alt+W Word  Alt+R Regex  Enter Pane  Esc Close", SearchHint.Describe(hasText: false));
        Assert.Equal(SearchHint.Describe(hasText: true), SearchHint.Build(hasText: true).Text);
        Assert.DoesNotContain(Labels(SearchHint.Build(hasText: true)), label => label.Label == "History");
    }

    [Fact]
    public void The_bar_texts_of_the_list_states_are_unchanged_and_every_label_performs_its_key()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        CommandEntry first = temp.Session.AllEntries.First();

        // A command that can start, focused in the list: Enter starts it and c copies its command line.
        Focus(controller, first);
        Field<CommandListView>(controller, "_list").SetFocus();
        Tick(controller);
        Assert.Equal(
            "Enter Start  ^C Stop  F5 Restart  Space Mark  ^A All  L Log  Del Clear  c Copy command  + Max  ^E Edit  ^Q Quit",
            Bar(controller));
        AssertLabelsPerformTheirKeys(controller);

        // A group header: no command, so no c hint.
        Focus(controller, temp.Session.Groups.First());
        Field<CommandListView>(controller, "_list").SetFocus();
        Tick(controller);
        string group = Bar(controller);
        Assert.StartsWith("Enter Start  ^C Stop  F5 Restart  ^A All  Del Clear  + Max  ^E Edit", group, StringComparison.Ordinal);
        Assert.DoesNotContain("c Copy", group, StringComparison.Ordinal);
        AssertLabelsPerformTheirKeys(controller);
    }

    [Fact]
    public void The_bar_texts_of_the_edit_mode_states_and_every_label_performs_its_key()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        CommandEntry first = temp.Session.AllEntries.First();

        // A command focused in the list: Enter and Del act on it, c copies it, Alt+Up/Down move it, and Save and Run always show.
        Focus(controller, first);
        Field<CommandListView>(controller, "_list").SetFocus();
        Tick(controller);
        Assert.Equal(
            "Enter Edit  ^N Command  ^G Group  Del Delete  Alt+↑↓ Move  c Copy command  ^S Save  ^E Run  ^Q Quit",
            Bar(controller));
        AssertLabelsPerformTheirKeys(controller);

        // A group header: no command, so no c hint.
        Focus(controller, temp.Session.Groups.First());
        Field<CommandListView>(controller, "_list").SetFocus();
        Tick(controller);
        Assert.Equal(
            "Enter Edit  ^N Command  ^G Group  Del Delete  Alt+↑↓ Move  ^S Save  ^E Run  ^Q Quit",
            Bar(controller));
        AssertLabelsPerformTheirKeys(controller);

        // The info pane shows the command: Enter and Del act on it; there is no move and no add.
        Focus(controller, first);
        Field<DetailsView>(controller, "_details").SetFocus();
        Tick(controller);
        Assert.Equal("Enter Edit  Del Delete  c Copy command  ^S Save  ^E Run  ^Q Quit", Bar(controller));
        AssertLabelsPerformTheirKeys(controller);

        // The info pane on a group header: nothing to edit there.
        Focus(controller, temp.Session.Groups.First());
        Field<DetailsView>(controller, "_details").SetFocus();
        Tick(controller);
        Assert.Equal("^S Save  ^E Run  ^Q Quit", Bar(controller));
    }

    [Fact]
    public void The_edit_mode_bar_with_no_row_offers_only_the_adds_Save_and_Run()
    {
        using TempSession temp = TempSession.Open(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        Field<CommandListView>(controller, "_list").SetFocus();
        Tick(controller);

        Assert.Equal("^N Command  ^G Group  ^S Save  ^E Run  ^Q Quit", Bar(controller));
    }

    [Fact]
    public void The_bar_text_while_the_find_field_has_the_focus_is_the_search_keys_and_the_quit_hint()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();

        Assert.True(controller.OnGlobalKey((Key)'/'));
        SearchBar bar = Field<SearchBar>(controller, "_searchBar");
        bar.SetQuery(new SearchQuery("x", false, false, false));
        Assert.True(bar.HasFieldFocus);
        Tick(controller);
        Assert.Equal(
            "F3 Next  ⇧F3 Prev  ↑↓ History  Alt+C Case  Alt+W Word  Alt+R Regex  Enter Pane  Esc Clear  ^Q Quit",
            Bar(controller));
        AssertLabelsPerformTheirKeys(controller);
    }

    /// <summary>The labels of a hint, in order, with the key each performs.</summary>
    private static List<(string Label, Key Key)> Labels(Hint hint) =>
        hint.Targets.Select(target => (hint.Text.Substring(target.Start, target.Length), target.Key)).ToList();

    /// <summary>The text of the bar: the left hint and the right one, as the status line shows them.</summary>
    private static string Bar(MainController controller)
    {
        Hint left = Field<HintsLine>(controller, "_status").Shown;
        Hint right = Field<HintsLine>(controller, "_status").ShownRight;
        return left.Text + right.Text;
    }

    /// <summary>
    /// Every label of the bar is a target, and each target performs the key its label names. Labels that are not targets (History) are
    /// left out.
    /// </summary>
    private static void AssertLabelsPerformTheirKeys(MainController controller)
    {
        HintsLine status = Field<HintsLine>(controller, "_status");
        Hint[] hints = { status.Shown, status.ShownRight };
        // A label can name one key in one state of the bar only: "↓" is the search's next and the edit bar's Alt+Down.
        (string Label, Key Key)[] expected =
        {
            ("Enter Start", Key.Enter),
            ("Enter Kill", Key.Enter),
            ("Enter Edit", Key.Enter),
            ("^C Stop", Key.C.WithCtrl),
            ("F5 Restart", Key.F5),
            ("Space Mark", Key.Space),
            ("^A All", Key.A.WithCtrl),
            ("L Log", Key.L),
            ("Del Clear", Key.Delete),
            ("c Copy command", (Key)'c'),
            ("c Copy selection", (Key)'c'),
            ("c Copy line", (Key)'c'),
            ("^S Save", Key.S.WithCtrl),
            ("+ Max", (Key)'+'),
            ("^E Edit", Key.E.WithCtrl),
            ("^E Run", Key.E.WithCtrl),
            ("^N Command", Key.N.WithCtrl),
            ("^G Group", Key.G.WithCtrl),
            ("Del Delete", Key.Delete),
            ("Alt+↑", Key.CursorUp.WithAlt),
            ("↓", Key.CursorDown.WithAlt),
            ("^Q Quit", Key.Q.WithCtrl),
            ("F3 Next", Key.F3),
            ("⇧F3 Prev", Key.F3.WithShift),
            ("↑", Key.CursorUp),
            ("↓", Key.CursorDown),
            ("Alt+C Case", Key.C.WithAlt),
            ("Alt+W Word", Key.W.WithAlt),
            ("Alt+R Regex", Key.R.WithAlt),
            ("Enter Pane", Key.Enter),
            ("Esc Clear", Key.Esc),
            ("Esc Close", Key.Esc),
        };
        foreach (Hint hint in hints)
        {
            foreach ((string label, Key key) in Labels(hint))
            {
                Assert.True(expected.Any(e => e.Label == label), $"'{label}' is a target but has no expected key");
                Assert.Contains(expected, e => e.Label == label && e.Key == key);
            }
        }
    }

    /// <summary>The value of a private field of the controller, read by reflection.</summary>
    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, Members)!.GetValue(controller)!;

    /// <summary>Points the panes at the row of the entry or group, as the list's focus does.</summary>
    private static void Focus(MainController controller, object key) =>
        typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new[] { key });

    /// <summary>One fast tick of the controller, which refreshes the bar's hints as the running timer does.</summary>
    private static void Tick(MainController controller) =>
        typeof(MainController).GetMethod("OnFastTick", Members)!.Invoke(controller, null);
}
