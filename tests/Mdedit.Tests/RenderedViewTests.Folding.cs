using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The folding of the view at 40 columns: Space, <c>*</c>, <c>-</c>, <c>0</c> to <c>6</c>, the glyph and summary clicks, the caret that a fold
/// moves, the screen row kept above the caret, the fold state across a width change, the search into folded content, and a folded code
/// block. The summaries count the source lines a fold hides, read from the source text.
/// </summary>
public sealed partial class RenderedViewTests
{
    // Source lines: 0 "# One", 2 "First…", 4 "## Sub", 6 "Sub text here.", 8 "# Two", 10 "Two text.". One's section holds Sub.
    private const string Sections = "# One\n\nFirst section text.\n\n## Sub\n\nSub text here.\n\n# Two\n\nTwo text.\n";

    // The rows of Sections with nothing folded.
    private static string[] OpenSections() =>
    [
        "▾ One",
        "",
        "First section text.",
        "",
        "▾ Sub",
        "",
        "Sub text here.",
        "",
        "▾ Two",
        "",
        "Two text.",
    ];

    [Fact]
    public void Space_on_a_heading_folds_its_section_and_a_second_Space_unfolds_it()
    {
        Result result = Run(View(Sections), v => v.HandleKey(Key.Space), v => v.HandleKey(Key.Space));

        // One's section hides the source lines 1 to 6: the blank after the heading, the paragraph, Sub and its paragraph.
        Assert.Equal(Pad(["▸ One … 6 lines", "", "▾ Two", "", "Two text."]), Rows(result.Screens[1]));
        Assert.Equal(Pad(OpenSections()), Rows(result.Screens[3]));
    }

    [Fact]
    public void Space_inside_a_paragraph_folds_its_section_and_moves_the_caret_to_the_heading()
    {
        RenderedView view = View(Sections);

        // One Down reaches the paragraph "First section text.", which is in One and not in Sub.
        Result result = Run(view, v => v.HandleKey(Key.CursorDown), v => v.HandleKey(Key.Space));

        Assert.Equal(Pad(["▸ One … 6 lines", "", "▾ Two", "", "Two text."]), Rows(result.Screens[3]));
        Assert.Equal(Offset(Sections, "One"), result.Snaps[1].Offset);
        Assert.Equal(new Point(2, 0), result.Snaps[1].Cursor);
    }

    [Fact]
    public void The_digit_keys_show_the_headings_down_to_their_level_and_zero_unfolds_everything()
    {
        const string nested = "# A\n\n## B\n\n### C\n\ntext\n\n# D\n\nd text\n";

        Result result = Run(View(nested), v => v.HandleKey((Key)'1'), v => v.HandleKey((Key)'0'));

        // The level-2 heading B folds the level-3 C inside it, which the digit 1 folds too: B hides source lines 3 to 6.
        Assert.Equal(Pad(["▾ A", "", "▸ B … 4 lines", "", "▾ D", "", "d text"]), Rows(result.Screens[1]));
        Assert.Equal(Pad(["▾ A", "", "▾ B", "", "▾ C", "", "text", "", "▾ D", "", "d text"]), Rows(result.Screens[3]));
    }

    [Fact]
    public void Minus_folds_the_innermost_section_and_star_unfolds_the_target_and_every_unit_inside_it()
    {
        RenderedView view = View(Sections);

        Result result = Run(
            view,
            v => { v.HandleKey(Key.CursorDown); v.HandleKey(Key.CursorDown); v.HandleKey(Key.CursorDown); },
            v => v.HandleKey((Key)'-'),
            v => v.HandleKey(Key.CursorUp),
            v => v.HandleKey(Key.Space),
            v => v.HandleKey((Key)'*'));

        // Sub is the section around "Sub text here.": only Sub folds, and the caret goes to its heading.
        Assert.Equal(Pad(["▾ One", "", "First section text.", "", "▸ Sub … 2 lines", "", "▾ Two", "", "Two text."]), Rows(result.Screens[3]));
        Assert.Equal(Offset(Sections, "Sub"), result.Snaps[1].Offset);
        // Space on the paragraph folds One, which holds the folded Sub; then * on One's heading unfolds One and Sub together.
        Assert.Equal(Pad(["▸ One … 6 lines", "", "▾ Two", "", "Two text."]), Rows(result.Screens[7]));
        Assert.Equal(Pad(OpenSections()), Rows(result.Screens[9]));
    }

    [Fact]
    public void A_click_on_a_heading_glyph_folds_it_and_a_click_on_its_summary_unfolds_it_while_a_click_on_its_text_places_the_caret()
    {
        Result result = Run(
            View(Sections),
            v => v.HandleMouse(Click(0, 0)),
            v => v.HandleMouse(Click(6, 0)),
            v => v.HandleMouse(Click(3, 0)));

        Assert.Equal(Pad(["▸ One … 6 lines", "", "▾ Two", "", "Two text."]), Rows(result.Screens[1]));
        Assert.Equal(Pad(OpenSections()), Rows(result.Screens[3]));
        Assert.Equal(Pad(OpenSections()), Rows(result.Screens[5]));
        // A click on the heading's text places the caret on the character under it: column 3 is the "n" of One.
        Assert.Equal(Offset(Sections, "One") + 1, result.Snaps[2].Offset);
    }

    [Fact]
    public void A_folded_code_block_shows_its_header_with_the_number_of_its_hidden_lines_and_the_caret_moves_past_it()
    {
        const string code = "```cs\nvar x = 1;\nvar y = 2;\n```\n\nafter\n";
        RenderedView view = View(code);

        Result result = Run(view, v => v.HandleKey(Key.Space));

        Assert.Equal(Pad(["▸ cs … 2 lines", "", "after"]), Rows(result.Screens[1]));
        Assert.Equal(Offset(code, "after"), view.CursorOffset);
    }

    [Fact]
    public void A_click_on_a_code_header_glyph_folds_the_code_block()
    {
        const string code = "```cs\nvar x = 1;\n```\n\nafter\n";

        Result result = Run(View(code), v => v.HandleMouse(Click(0, 0)));

        Assert.Equal(Pad(["▸ cs … 1 line", "", "after"]), Rows(result.Screens[1]));
    }

    [Fact]
    public void Folding_above_the_caret_keeps_the_caret_on_its_screen_row()
    {
        string text = Sections + string.Concat(Enumerable.Range(1, 15).Select(i => $"p{i}\n\n"));
        RenderedView view = View(text);
        int caret = Offset(text, "p5");

        Result result = Run(view, v => v.SelectRange(new SourceRange(caret, caret + 2)), v => v.ToggleFold(v.Units[0]));

        // The caret was on its row 11 of 12; One's six hidden lines are above it, so its line moves up by six and its row stays.
        Snap before = result.Snaps[0];
        Snap after = result.Snaps[1];
        Assert.Equal(11, before.Line - before.Top);
        Assert.Equal(caret, after.Offset);
        Assert.Equal(11, after.Cursor!.Value.Y);
        Assert.Equal(before.Line - 6 - 11, after.Top);
    }

    [Fact]
    public void A_fold_survives_a_width_change()
    {
        string text = Sections;
        RenderedView view = View(text);
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(Width, Height);
        using var window = new Window { BorderStyle = LineStyle.None };
        view.X = 0;
        view.Y = 0;
        view.Width = Dim.Fill();
        view.Height = Dim.Fill();
        window.Add(view);

        Result result = RunResizing(app, window, view, v => v.HandleKey(Key.Space), _ => window.Width = 25);

        Assert.Equal("▸ One … 6 lines", result.Screens[^1].Text(0));
        Assert.Equal("▾ Two", result.Screens[^1].Text(2));
    }

    [Fact]
    public void A_search_match_in_folded_content_unfolds_the_units_that_hide_it_and_selects_it()
    {
        RenderedView view = View(Sections);
        int match = Offset(Sections, "Sub text here.");

        Result result = Run(
            view,
            v => v.HandleMouse(Click(0, 4)),
            v => v.SelectRange(new SourceRange(match, match + 4)));

        // The click folds Sub; the search then unfolds it, and the match is selected.
        Assert.Equal(Pad(["▾ One", "", "First section text.", "", "▸ Sub … 2 lines", "", "▾ Two", "", "Two text."]), Rows(result.Screens[1]));
        Assert.Equal(Pad(OpenSections()), Rows(result.Screens[3]));
        Assert.Equal(new SourceRange(match, match + 4), view.SelectionRange);
    }
}
