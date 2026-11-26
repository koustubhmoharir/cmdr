using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Editor.Document.Folding;
using Terminal.Gui.Input;

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
}
