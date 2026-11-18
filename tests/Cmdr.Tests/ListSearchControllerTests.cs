using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tests;

/// <summary>
/// The list search through the real controller: a match inside a collapsed nested group is revealed, the groups that the search
/// expanded are collapsed again when it closes (except those above the focused row), and the matches are highlighted in the list.
/// </summary>
[Collection("Terminal GUI")]
public sealed class ListSearchControllerTests
{
    /// <summary>One cell of a frame: its grapheme and its attribute as the driver holds it.</summary>
    private readonly record struct Painted(string Grapheme, Attribute? Attribute);

    /// <summary>
    /// The tree of the reveal tests: group "top" collapsed, holding group "mid" collapsed, holding "target" and "other-in-mid"; then
    /// group "open" with "oc".
    /// </summary>
    private static GroupSpec[] Nested() => new[]
    {
        new GroupSpec
        {
            Name = "top",
            Expanded = false,
            Items =
            {
                new GroupSpec
                {
                    Name = "mid",
                    Expanded = false,
                    Items = { Command("target"), Command("other-in-mid") },
                },
            },
        },
        new GroupSpec { Name = "open", Items = { Command("oc") } },
    };

    /// <summary>A command with the given name, run by the shell.</summary>
    private static CommandSpec Command(string name) => new() { Name = name, Program = "sh" };

    /// <summary>The pattern for a case-sensitive literal query.</summary>
    private static SearchPattern Literal(string text)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(text, CaseSensitive: true, WholeWord: false, Regex: false), out string? error);
        Assert.Null(error);
        return Assert.IsType<SearchPattern>(pattern);
    }

    /// <summary>The pattern for a case-sensitive regular expression.</summary>
    private static SearchPattern Regex(string text)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(text, CaseSensitive: true, WholeWord: false, Regex: true), out string? error);
        Assert.Null(error);
        return Assert.IsType<SearchPattern>(pattern);
    }

    [Fact]
    public void Find_reaches_a_command_inside_a_collapsed_nested_group_and_expands_its_ancestors()
    {
        using TempSession temp = TempSession.Open(Nested());
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        Assert.Equal(new[] { "top", "open", "oc" }, Names(source));

        controller.ListSearch.SetSearch(Literal("target"));
        Assert.Equal(SearchResult.Found, controller.ListSearch.Find(backward: false, inclusive: true));

        Assert.Equal(new[] { "top", "mid", "target", "other-in-mid", "open", "oc" }, Names(source));
        Assert.Equal("target", FocusedName(controller));
    }

    [Fact]
    public void Restore_collapses_the_groups_the_search_expanded_except_those_above_the_focused_row()
    {
        using TempSession temp = TempSession.Open(Nested());
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        controller.ListSearch.SetSearch(Literal("target"));
        controller.ListSearch.Find(backward: false, inclusive: true);

        // Focus on a row outside the expanded groups: nothing above it is kept expanded, so both groups collapse again.
        FocusRowNamed(controller, "oc");
        controller.RestoreSearchCollapsed();

        Assert.Equal(new[] { "top", "open", "oc" }, Names(source));
        Assert.Equal("oc", FocusedName(controller));
    }

    [Fact]
    public void Restore_keeps_the_ancestors_of_the_focused_command_expanded()
    {
        using TempSession temp = TempSession.Open(Nested());
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        controller.ListSearch.SetSearch(Literal("target"));
        controller.ListSearch.Find(backward: false, inclusive: true);

        controller.RestoreSearchCollapsed();

        Assert.Equal(new[] { "top", "mid", "target", "other-in-mid", "open", "oc" }, Names(source));
        Assert.Equal("target", FocusedName(controller));
    }

    [Fact]
    public void Matches_in_group_and_command_names_are_drawn_in_the_match_colors_and_the_active_one_in_its_own()
    {
        using TempSession temp = TempSession.Open(
            new GroupSpec { Name = "ab", Items = { Command("xab ab"), Command("other") } });

        // Rows: the header "ab", then "xab ab" (its main line and its detail line "not started"), then "other". The header
        // is drawn at column 2, the name "xab ab" at column 4, so its matches are at columns 5-6 and 8-9.
        IReadOnlyList<Painted[][]> frames = Frames(
            temp,
            40,
            8,
            controller => { },
            controller => controller.ListSearch.SetSearch(Regex("ab|not")),
            controller => controller.ListSearch.Find(backward: false, inclusive: true),
            controller => controller.ListSearch.Find(backward: false, inclusive: false));

        Painted[][] plain = frames[0];
        var headerMatches = new HashSet<(int Row, int Column)> { (0, 2), (0, 3) };
        var commandMatches = new HashSet<(int Row, int Column)> { (1, 5), (1, 6), (1, 8), (1, 9) };

        // The search is on, nothing is active yet: every match is in the match color, and the detail line of a command keeps its cells.
        AssertHighlight(plain, frames[1], headerMatches.Concat(commandMatches), active: Array.Empty<(int, int)>());

        // The first find takes the header's match (the first one in list order from the top), the second the command's first match.
        AssertHighlight(plain, frames[2], commandMatches, active: headerMatches);
        AssertHighlight(plain, frames[3], headerMatches.Concat(new[] { (1, 8), (1, 9) }), active: new[] { (1, 5), (1, 6) });
    }

    /// <summary>
    /// Checks every cell of a frame against the plain one: the cells in <paramref name="matches"/> take the match color, those in
    /// <paramref name="active"/> the active color, and the rest keep their attributes.
    /// </summary>
    private static void AssertHighlight(
        Painted[][] plain,
        Painted[][] frame,
        IEnumerable<(int Row, int Column)> matches,
        IEnumerable<(int Row, int Column)> active)
    {
        var matched = new HashSet<(int Row, int Column)>(matches);
        var activeCells = new HashSet<(int Row, int Column)>(active);
        for (int row = 0; row < plain.Length; row++)
        {
            for (int column = 0; column < plain[row].Length; column++)
            {
                Painted before = plain[row][column];
                Painted after = frame[row][column];
                Assert.Equal(before.Grapheme, after.Grapheme);
                Attribute? expected = before.Attribute;
                if (activeCells.Contains((row, column)))
                {
                    expected = SearchColors.Active(before.Attribute!.Value);
                }
                else if (matched.Contains((row, column)))
                {
                    expected = SearchColors.Match(before.Attribute!.Value);
                }

                Assert.Equal(expected, after.Attribute);
            }
        }
    }

    /// <summary>The names of the rows the source shows, in order.</summary>
    private static IEnumerable<string> Names(CommandListSource source) => source.Rows.Select(row => TreeRows.ItemOf(row).Name);

    /// <summary>The name of the focused row, read from the list's selection.</summary>
    private static string? FocusedName(MainController controller)
    {
        CommandListView list = Field<CommandListView>(controller, "_list");
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        int row = list.SelectedItem is { } line ? source.Lines.LineToRow(line) : -1;
        return row >= 0 ? TreeRows.ItemOf(source.Rows[row]).Name : null;
    }

    /// <summary>Focuses the shown row with the given name, through the list's selection.</summary>
    private static void FocusRowNamed(MainController controller, string name)
    {
        CommandListView list = Field<CommandListView>(controller, "_list");
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        int row = Enumerable.Range(0, source.Rows.Count).Single(i => TreeRows.ItemOf(source.Rows[i]).Name == name);
        list.SelectedItem = source.Lines.RowToLine(row);
    }

    /// <summary>The value of a private field of the controller, read by reflection.</summary>
    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;

    /// <summary>
    /// Draws the command list of a controller over the given session in a window of the given size, one frame per step. Each step
    /// runs on the UI loop before its frame. Returns each frame's cells, row by row.
    /// </summary>
    private static IReadOnlyList<Painted[][]> Frames(
        TempSession temp,
        int width,
        int height,
        params Action<MainController>[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);

        var frames = new List<Painted[][]>();
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        var list = new CommandListView(controller, source) { X = 0, Y = 0, Width = width, Height = height };
        var window = new Window { X = 0, Y = 0, Width = width, Height = height, BorderStyle = LineStyle.None };
        window.Add(list);

        int frame = 0;
        app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            steps[frame](controller);

            // The controller redraws its own list, not this one, so this list is invalidated here.
            list.SetNeedsDraw();
            app.LayoutAndDraw();
            var driver = app.Driver!;
            var rows = new Painted[height][];
            for (int r = 0; r < height; r++)
            {
                rows[r] = new Painted[width];
                for (int c = 0; c < width; c++)
                {
                    rows[r][c] = new Painted(driver.Contents![r, c].Grapheme, driver.Contents[r, c].Attribute);
                }
            }

            frames.Add(rows);
            frame++;
            if (frame == steps.Length)
            {
                app.RequestStop(window);
                return false;
            }

            return true;
        });
        app.Run(window);
        return frames;
    }
}
