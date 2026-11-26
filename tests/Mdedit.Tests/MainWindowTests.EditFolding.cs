using System.Drawing;
using System.Reflection;
using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Editor;
using Terminal.Gui.Editor.Document.Folding;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Mdedit.Tests;

/// <summary>
/// The folds in edit mode, shared with the view: a section folded in the view is folded in the editor, a fold changed in the editor (as a
/// gutter click does) is what the view shows when it is shown again, a code block folds both ways, folds survive typing above them, and a search
/// in edit mode opens the folded section that hides its match.
/// </summary>
public sealed partial class MainWindowTests
{
    // A code block with no heading: its one folding is the code block.
    private const string CodeBlock = "Intro.\n\n```csharp\nint x = 1;\nint y = 2;\n```\n\nAfter.\n";

    [Fact]
    public void A_section_folded_in_the_view_is_folded_in_the_editor_after_Ctrl_T_and_shows_only_its_heading()
    {
        OpenedFile file = Opened("notes.md", Small);

        var foldings = new List<bool>();
        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => foldings = window.Editor.FoldingManager!.AllFoldings.Select(folding => folding.IsFolded).ToList());

        Assert.Equal([true, false], foldings);

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("# Install", screen.Text(1), StringComparison.Ordinal);
        Assert.DoesNotContain(Enumerable.Range(0, screen.Cells.Count), row => screen.Text(row).Contains("Intro text.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_section_unfolded_in_the_editor_is_unfolded_in_the_view_when_it_is_shown_again()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.FoldingManager!.AllFoldings.First().IsFolded = false,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.Contains("Intro text.", session.Screens[^1].Text(3), StringComparison.Ordinal);
        Assert.False(session.Window.View.IsFolded(session.Window.View.Units[0]));
    }

    [Fact]
    public void A_code_block_folded_in_the_editor_is_folded_in_the_view_when_it_is_shown_again()
    {
        OpenedFile file = Opened("notes.md", CodeBlock);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.FoldingManager!.AllFoldings.Single().IsFolded = true,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.Equal(FoldUnitKind.Code, session.Window.View.Units[0].Kind);
        Assert.True(session.Window.View.IsFolded(session.Window.View.Units[0]));
        Assert.Contains(Enumerable.Range(0, session.Screens[^1].Cells.Count), row => session.Screens[^1].Text(row).Contains("2 lines", StringComparison.Ordinal));
    }

    [Fact]
    public void Folds_survive_typing_above_them_in_edit_mode()
    {
        const string text = "Intro line.\n\n## Linux\n\n- step one\n";
        OpenedFile file = Opened("notes.md", text);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = 0,
            (_, window) => Type(window, "Hi there. "),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.Single(session.Window.View.Units);
        Assert.True(session.Window.View.IsFolded(session.Window.View.Units[0]));
        Assert.Equal("Hi there. Intro line.\n\n## Linux\n\n- step one\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_search_in_edit_mode_opens_the_folded_section_that_hides_its_match()
    {
        OpenedFile file = Opened("notes.md", Small);

        (bool Folded, int Start, bool ViewFolded) state = default;
        Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl),
            (_, window) => Type(window, "Intro"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => state = (
                window.Editor.FoldingManager!.AllFoldings.First().IsFolded,
                window.Editor.SelectionStart,
                window.View.IsFolded(window.View.Units[0])));

        Assert.Equal((false, Index(Small, "Intro"), false), state);
    }

    [Fact]
    public void Space_in_edit_mode_types_a_space_and_folds_nothing()
    {
        OpenedFile file = Opened("notes.md", Small);

        (string Text, bool Folded) state = default;
        Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = 0,
            (_, window) => window.NewKeyDownEvent(Key.Space),
            (_, window) => state = (window.Editor.Buffer.Text, window.Editor.FoldingManager!.AllFoldings.Any(folding => folding.IsFolded)));

        Assert.Equal((" " + Small, false), state);
    }

    [Fact]
    public void The_fold_gutter_is_two_columns_so_the_text_starts_at_column_2_and_the_heading_has_the_open_glyph()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(file, 40, 12, (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains(Enumerable.Range(0, screen.Cells.Count), row => screen.Text(row).Contains("\u25BE # Install", StringComparison.Ordinal));
        Assert.Contains(Enumerable.Range(0, screen.Cells.Count), row => screen.Text(row).Contains("\u2502 Intro text.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_folded_section_shows_its_summary_after_the_heading_with_the_count_the_view_shows()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        string viewSummary = SummaryIn(session.Screens[1]);
        Assert.Equal($"\u2026 {session.Window.View.Units[0].HiddenLines} lines", viewSummary);
        Assert.Contains(Enumerable.Range(0, session.Screens[^1].Cells.Count), row => session.Screens[^1].Text(row).Contains("# Install " + viewSummary, StringComparison.Ordinal));
    }

    [Fact]
    public void A_folded_code_block_shows_its_summary_on_the_fence_line_with_the_count_the_view_shows()
    {
        OpenedFile file = Opened("notes.md", CodeBlock);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        string viewSummary = SummaryIn(session.Screens[1]);
        Assert.Equal("\u2026 2 lines", viewSummary);
        Assert.Contains(Enumerable.Range(0, session.Screens[^1].Cells.Count), row => session.Screens[^1].Text(row).Contains("```csharp " + viewSummary, StringComparison.Ordinal));
        Assert.DoesNotContain(Enumerable.Range(0, session.Screens[^1].Cells.Count), row => session.Screens[^1].Text(row).Contains("int x = 1;", StringComparison.Ordinal));
    }

    [Fact]
    public void A_folded_heading_that_wraps_shows_its_summary_once_on_the_last_segment()
    {
        const string text = "# A heading long enough to wrap onto a second visual line of the window\n\nIntro text.\n";
        OpenedFile file = Opened("notes.md", text);

        Session session = Run(
            file,
            40,
            14,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        TerminalScreen screen = session.Screens[^1];
        int[] rows = Enumerable.Range(0, screen.Cells.Count).Where(row => screen.Text(row).Contains(" \u2026 ", StringComparison.Ordinal)).ToArray();
        Assert.Single(rows);
        Assert.DoesNotContain("\u2026", screen.Text(rows[0] - 1), StringComparison.Ordinal);
        Assert.Contains("window \u2026 2 lines", screen.Text(rows[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_folded_setext_heading_shows_the_count_the_view_shows_not_the_editor_lines_it_hides()
    {
        // The underline is the heading's second line: the editor hides it, the view counts the body's lines only.
        OpenedFile file = Opened("notes.md", "Title\n=====\n\nBody text.\n");

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        string viewSummary = SummaryIn(session.Screens[1]);
        Assert.Equal("\u2026 2 lines", viewSummary);
        Assert.Contains(Enumerable.Range(0, session.Screens[^1].Cells.Count), row => session.Screens[^1].Text(row).Contains("Title " + viewSummary, StringComparison.Ordinal));
    }

    [Fact]
    public void A_folded_heading_that_hides_one_line_says_1_line()
    {
        OpenedFile file = Opened("notes.md", "# One\nText.\n");

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.Equal("\u2026 1 line", SummaryIn(session.Screens[1]));
        Assert.Equal("\u2026 1 line", SummaryIn(session.Screens[^1]));
    }

    [Fact]
    public void The_summary_in_edit_mode_is_drawn_in_the_view_s_dimmed_fold_colour()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        TerminalScreen screen = session.Screens[^1];
        int row = Enumerable.Range(0, screen.Cells.Count).First(r => screen.Text(r).Contains(" \u2026 ", StringComparison.Ordinal));
        int column = screen.Text(row).IndexOf('\u2026', StringComparison.Ordinal);
        Assert.Equal(MdColors.Dim, screen.Cells[row][column].Attribute.Foreground);
    }

    [Fact]
    public void The_edit_hints_at_80_columns_include_Alt_F_Fold_after_Alt_Z_Wrap()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(
            file,
            80,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.Contains("^T View  ^S Save  ^F Find  Alt+Z Wrap  Alt+F Fold", session.Screens[^1].Text(11), StringComparison.Ordinal);
    }

    [Fact]
    public void Alt_F_in_edit_mode_folds_the_section_under_the_caret_and_the_view_follows_and_a_second_Alt_F_unfolds_it()
    {
        OpenedFile file = Opened("notes.md", Small);

        var states = new List<bool>();
        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = Index(Small, "Intro"),
            (_, window) => window.NewKeyDownEvent(SourceEditor.FoldKey),
            (_, window) => states.Add(window.Editor.FoldingManager!.AllFoldings.First().IsFolded),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => states.Add(window.View.IsFolded(window.View.Units[0])),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(SourceEditor.FoldKey),
            (_, window) => states.Add(window.Editor.FoldingManager!.AllFoldings.First().IsFolded));

        Assert.Equal([true, true, false], states);
        Assert.Equal(Small, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Enter_in_edit_mode_inserts_a_line_break_and_does_not_toggle_a_fold()
    {
        OpenedFile file = Opened("notes.md", Small);

        (string Text, bool Folded) state = default;
        Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = 0,
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => state = (window.Editor.Buffer.Text, window.Editor.FoldingManager!.AllFoldings.Any(folding => folding.IsFolded)));

        Assert.Equal(("\n" + Small, false), state);
    }

    [Fact]
    public void A_click_on_the_fold_gutter_of_a_heading_in_edit_mode_folds_it_and_the_view_follows()
    {
        OpenedFile file = Opened("notes.md", Small);

        var states = new List<bool>();
        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => ClickGutter(window, row: 0),
            (_, window) => states.Add(window.Editor.FoldingManager!.AllFoldings.First().IsFolded),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => states.Add(window.View.IsFolded(window.View.Units[0])));

        Assert.Equal([true, true], states);
        string summary = $"Install \u2026 {session.Window.View.Units[0].HiddenLines} lines";
        Assert.Contains(Enumerable.Range(0, session.Screens[^1].Cells.Count), row => session.Screens[^1].Text(row).Contains(summary, StringComparison.Ordinal));
    }

    // The summary the view draws on the heading line: the text from the ellipsis to the frame's edge.
    private static string SummaryIn(TerminalScreen screen)
    {
        string row = Enumerable.Range(0, screen.Cells.Count).Select(screen.Text).First(text => text.Contains(" \u2026 ", StringComparison.Ordinal));
        string summary = row[row.IndexOf('\u2026', StringComparison.Ordinal)..];
        return summary[..summary.IndexOf('\u2502', StringComparison.Ordinal)].TrimEnd();
    }

    // A left click on the fold gutter of the editor, on the given row of its viewport. The gutter's folding view is internal to the editor, so
    // its mouse handler is invoked by reflection, as the other tests do for internal views.
    private static void ClickGutter(MainWindow window, int row)
    {
        View foldingGutter = window.Editor.Padding!.GetOrCreateView().SubViews.OfType<Gutter>().Single().SubViews.Single();
        var mouse = new Mouse { Flags = MouseFlags.LeftButtonClicked, Position = new Point(0, row), ScreenPosition = new Point(0, row) };
        foldingGutter.GetType().GetMethod("OnMouseEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(foldingGutter, [mouse]);
    }
}
