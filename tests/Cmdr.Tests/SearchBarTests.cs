using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using StatusLine = Cmdr.Tui.StatusLine;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cmdr.Tests;

/// <summary>
/// The search bar in the main window: how it opens and closes, the keys it takes while it has the focus, the pane it searches, and
/// the status it shows. The keys go through the application's own routing, so the bar's modal handling is what is tested.
/// </summary>
[Collection("Terminal GUI")]
public sealed class SearchBarTests
{
    private const int Width = 120;
    private const int Height = 40;
    private const string NextPrevHint = "F3 next · Shift+F3 prev";
    private const string SearchKeys = "F3 Next  ⇧F3 Prev  ↑↓ History  Alt+C Case  Alt+W Word  Alt+R Regex  Enter Pane";

    [Fact]
    public void Slash_in_the_list_opens_the_bar_above_the_status_line_and_Esc_closes_it_and_restores_the_layout()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            StatusLine status = Field<StatusLine>(controller, "_status");
            OutputView output = Field<OutputView>(controller, "_output");
            CommandListView list = Field<CommandListView>(controller, "_list");
            list.SetFocus();
            app.LayoutAndDraw();
            int closedHeight = output.Frame.Height;
            Assert.False(bar.Visible);

            Press(app, (Key)'/');

            Assert.True(bar.Visible);
            Assert.True(bar.HasFieldFocus);
            Assert.Equal(status.Frame.Y - 1, bar.Frame.Y);
            Assert.Equal(closedHeight - 1, output.Frame.Height);
            Assert.Equal(bar.Frame.Y, output.Frame.Bottom);

            Press(app, Key.Esc);

            Assert.False(bar.Visible);
            Assert.Equal(closedHeight, output.Frame.Height);
            Assert.True(list.HasFocus);
        });
    }

    [Fact]
    public void Ctrl_F_from_the_output_pane_opens_the_bar_and_Esc_returns_the_focus_to_the_output()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            output.SetFocus();
            app.LayoutAndDraw();
            int closedHeight = output.Frame.Height;

            Press(app, Key.F.WithCtrl);

            Assert.True(bar.Visible);
            Assert.True(bar.HasFieldFocus);
            Assert.Equal(closedHeight - 1, output.Frame.Height);

            Press(app, Key.Esc);

            Assert.False(bar.Visible);
            Assert.True(output.HasFocus);
            Assert.Equal(closedHeight, output.Frame.Height);
        });
    }

    [Fact]
    public void Typing_a_query_counts_the_matches_in_the_target_pane_and_the_bar_shows_the_active_one()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "alpha\nbeta alpha\ngamma\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();

            Press(app, (Key)'/');
            Type(app, "alpha");

            Assert.Equal(new SearchStatus(2, 1), output.SearchStatus());
            Assert.Equal("1 of 2", bar.Status);
        });
    }

    [Fact]
    public void Plus_types_into_the_field_and_F5_and_Ctrl_N_do_nothing_while_the_field_has_the_focus()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            FrameView list = Field<FrameView>(controller, "_listFrame");
            FrameView details = Field<FrameView>(controller, "_detailsFrame");
            CommandListView listView = Field<CommandListView>(controller, "_list");
            listView.SetFocus();
            app.LayoutAndDraw();

            Press(app, (Key)'/');
            Press(app, (Key)'+');
            Press(app, Key.F5);
            Press(app, Key.N.WithCtrl);

            Assert.Equal("+", bar.Query.Text);
            Assert.True(bar.HasFieldFocus);
            Assert.True(bar.Visible);
            Assert.True(list.Visible, "'+' must not maximize while the field has the focus.");
            Assert.True(details.Visible);
        });
    }

    [Fact]
    public void Enter_moves_the_focus_to_the_pane_and_F3_and_Shift_F3_step_through_the_matches_and_F3_past_the_last_wraps()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "hit one\nhit two\nhit three\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "hit");
            Assert.Equal("1 of 3", bar.Status);

            Press(app, Key.Enter);

            Assert.True(bar.Visible);
            Assert.True(output.HasFocus);
            Assert.False(bar.HasFieldFocus);

            Press(app, Key.F3);
            Assert.Equal("2 of 3", bar.Status);
            Press(app, Key.F3.WithShift);
            Assert.Equal("1 of 3", bar.Status);
            Press(app, Key.F3);
            Press(app, Key.F3);
            Assert.Equal("3 of 3", bar.Status);

            Press(app, Key.F3);

            Assert.Equal("1 of 3", bar.Status);
            Assert.Equal("Search wrapped", StatusText(Field<StatusLine>(controller, "_status")));
        });
    }

    [Fact]
    public void Alt_C_toggles_case_sensitivity_and_Alt_R_with_an_invalid_pattern_shows_invalid_pattern()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "Hit hit HIT\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "hit");
            Assert.Equal(new SearchStatus(3, 1), output.SearchStatus());

            Press(app, Key.C.WithAlt);

            Assert.True(bar.Query.CaseSensitive);
            Assert.Equal(new SearchStatus(1, 1), output.SearchStatus());
            Assert.Equal("1 of 1", bar.Status);

            Press(app, Key.R.WithAlt);
            Type(app, "(");

            Assert.True(bar.Query.Regex);
            Assert.Equal("invalid pattern", bar.Status);
            Assert.Equal(new SearchStatus(0, 0), output.SearchStatus());
        });
    }

    [Fact]
    public void Esc_with_a_selection_in_the_output_clears_it_and_the_next_Esc_closes_the_search()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "hit one\nhit two\nhit three\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "hit");
            Press(app, Key.Enter);
            Press(app, Key.CursorDown.WithShift);
            Assert.True(output.HasSelection, "The selection must exist for this test to mean anything.");

            Press(app, Key.Esc);

            Assert.False(output.HasSelection);
            Assert.True(bar.Visible);
            Assert.Equal(new SearchStatus(3, 1), output.SearchStatus());

            Press(app, Key.Esc);

            Assert.False(bar.Visible);
            Assert.Equal(new SearchStatus(0, 0), output.SearchStatus());
        });
    }

    [Fact]
    public void Moving_the_focus_to_another_pane_moves_the_search_there_without_jumping()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            DetailsView details = Field<DetailsView>(controller, "_details");
            WriteOutput(temp, "hit one\nhit two\nhit three\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "hit");
            Press(app, Key.Enter);

            // Shift+Tab from the output goes backward, to the info pane.
            Press(app, Key.Tab.WithShift);
            Tick(controller);

            Assert.True(details.HasFocus);
            Assert.Equal(new SearchStatus(0, 0), output.SearchStatus());
            Assert.True(details.SearchStatus().Count > 0);
            Assert.True(bar.Visible);
        });
    }

    [Fact]
    public void Closing_after_a_list_search_collapses_the_groups_it_expanded_when_the_focus_is_elsewhere()
    {
        using TempSession temp = TempSession.Open(
            new GroupSpec
            {
                Name = "top",
                Expanded = false,
                Items = { new GroupSpec { Name = "mid", Expanded = false, Items = { Command("target"), Command("other-in-mid") } } },
            },
            new GroupSpec { Name = "open", Items = { Command("oc") } });

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            CommandListSource source = Field<CommandListSource>(controller, "_source");
            CommandListView list = Field<CommandListView>(controller, "_list");
            list.SetFocus();
            app.LayoutAndDraw();
            int collapsedLines = source.Count;

            Press(app, (Key)'/');
            Type(app, "target");
            Assert.True(source.Count > collapsedLines, "The search must expand the groups for this test to mean anything.");

            Press(app, Key.Enter);
            Press(app, Key.CursorDown);
            Press(app, Key.CursorDown);
            Press(app, Key.Esc);

            Assert.Equal(collapsedLines, source.Count);
        });
    }

    [Fact]
    public void F3_with_the_bar_closed_after_a_search_reopens_it_with_the_last_query_and_keeps_the_focus_in_the_pane()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "hit one\nhit two\nhit three\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "hit");
            Press(app, Key.Esc);
            Press(app, Key.Esc);
            Assert.False(bar.Visible);

            Press(app, Key.F3);

            Assert.True(bar.Visible);
            Assert.False(bar.HasFieldFocus);
            Assert.True(output.HasFocus);
            Assert.Equal("hit", bar.Query.Text);
            Assert.EndsWith(" of 3", bar.Status);
        });
    }

    [Theory]
    [InlineData(120, true)]
    [InlineData(31, true)]
    [InlineData(30, false)]
    [InlineData(20, false)]
    public void A_narrow_bar_hides_the_options_when_the_field_has_no_room_for_them_while_the_field_has_the_focus(int width, bool options)
    {
        using TempSession temp = Session();

        RunLaidOut(temp, width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            CommandListView list = Field<CommandListView>(controller, "_list");
            list.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "hit");

            string row = ScreenRow(app, bar.Frame.Y, width);
            Assert.StartsWith("Find: hit", row);
            Assert.Equal(options, row.Contains("Word", StringComparison.Ordinal));
            Assert.DoesNotContain(NextPrevHint, row);
        });
    }

    [Theory]
    [InlineData(38, true)]
    [InlineData(37, false)]
    public void A_narrow_bar_hides_the_next_and_previous_hint_when_the_field_has_no_room_for_it(int width, bool hint)
    {
        using TempSession temp = Session();

        RunLaidOut(temp, width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            TypeHitInOutput(temp, app, controller);
            Press(app, Key.Enter);

            string row = ScreenRow(app, bar.Frame.Y, width);
            Assert.StartsWith("Find: hit", row);
            Assert.Equal(hint, row.Contains(NextPrevHint, StringComparison.Ordinal));
            Assert.Equal(hint ? 1 : 0, CountOf(row, "F3 next"));
        });
    }

    [Fact]
    public void At_full_width_with_the_field_focused_the_bar_row_shows_the_caption_field_toggles_and_status_but_no_hint()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            TypeHitInOutput(temp, app, controller);

            string row = ScreenRow(app, bar.Frame.Y, Width);
            Assert.True(bar.HasFieldFocus);
            Assert.StartsWith("Find: hit", row);
            Assert.Contains(" Aa ", row);
            Assert.Contains(" Word ", row);
            Assert.Contains(" .* ", row);
            Assert.EndsWith("1 of 3", row.TrimEnd());
            Assert.DoesNotContain(NextPrevHint, row);
        });
    }

    [Fact]
    public void After_Enter_the_bar_row_shows_the_next_and_previous_hint_in_place_of_the_toggles_until_the_field_has_the_focus_again()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            TypeHitInOutput(temp, app, controller);
            Press(app, Key.Enter);

            string row = ScreenRow(app, bar.Frame.Y, Width);
            Assert.False(bar.HasFieldFocus);
            int hintAt = row.IndexOf(NextPrevHint, StringComparison.Ordinal);
            Assert.True(hintAt > 0, "The hint must be shown.");
            Assert.Equal("Find: hit", row[..hintAt].TrimEnd());
            Assert.DoesNotContain(" Aa ", row);
            Assert.DoesNotContain(" Word ", row);
            Assert.DoesNotContain(" .* ", row);

            bar.FocusField(false);
            app.LayoutAndDraw();

            row = ScreenRow(app, bar.Frame.Y, Width);
            Assert.True(bar.HasFieldFocus);
            Assert.Contains(" Aa ", row);
            Assert.Contains(" Word ", row);
            Assert.DoesNotContain(NextPrevHint, row);
        });
    }

    [Fact]
    public void A_toggle_that_is_on_is_drawn_in_the_active_colour_and_one_that_is_off_in_the_normal_colour()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            TypeHitInOutput(temp, app, controller);
            Press(app, Key.C.WithAlt);

            int row = bar.Frame.Y;
            string text = ScreenRow(app, row, Width);
            Terminal.Gui.Drawing.Attribute normal = AttributeAt(app, row, 0);
            Terminal.Gui.Drawing.Attribute on = AttributeAt(app, row, text.IndexOf(" Aa ", StringComparison.Ordinal) + 1);
            Terminal.Gui.Drawing.Attribute off = AttributeAt(app, row, text.IndexOf(" Word ", StringComparison.Ordinal) + 1);

            Assert.Equal(SearchColors.Active(normal), on);
            Assert.Equal(normal, off);
            Assert.False(normal.Style.HasFlag(Terminal.Gui.Drawing.TextStyle.Italic));
            Assert.False(on.Style.HasFlag(Terminal.Gui.Drawing.TextStyle.Italic));
        });
    }

    [Fact]
    public void A_left_click_on_a_toggle_flips_its_option_updates_the_matches_and_leaves_the_focus_in_the_field()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            WriteOutput(temp, "Hit hit HIT\n");
            ShowFirstCommand(app, controller);
            OutputView output = Field<OutputView>(controller, "_output");
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "hit");
            Assert.EndsWith(" of 3", bar.Status);

            int column = ScreenRow(app, bar.Frame.Y, Width).IndexOf(" Aa ", StringComparison.Ordinal) + 1;
            Click(app, column, bar.Frame.Y);

            Assert.True(bar.Query.CaseSensitive);
            Assert.True(bar.HasFieldFocus);
            Assert.Equal("1 of 1", bar.Status);

            Click(app, column, bar.Frame.Y);

            Assert.False(bar.Query.CaseSensitive);
            Assert.True(bar.HasFieldFocus);
            Assert.EndsWith(" of 3", bar.Status);
        });
    }

    [Fact]
    public void Status_line_shows_the_search_keys_while_the_find_field_has_the_focus_and_the_pane_hints_otherwise()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            StatusLine status = Field<StatusLine>(controller, "_status");
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            Field<CommandListView>(controller, "_list").SetFocus();
            app.LayoutAndDraw();
            Tick(controller);
            string paneHints = StatusText(status);
            Assert.Contains("+ Max  ^E Edit", paneHints);

            Press(app, (Key)'/');
            Tick(controller);
            Assert.True(bar.HasFieldFocus);
            Assert.EndsWith(SearchKeys + "  Esc Close", StatusText(status));

            Press(app, (Key)'x');
            Tick(controller);
            Assert.EndsWith(SearchKeys + "  Esc Clear", StatusText(status));

            Press(app, Key.Enter);
            Tick(controller);
            Assert.False(bar.HasFieldFocus);
            Assert.True(bar.Visible);
            Assert.Equal(paneHints, StatusText(status));

            Press(app, (Key)'/');
            Tick(controller);
            Assert.True(bar.HasFieldFocus);
            Assert.EndsWith(SearchKeys + "  Esc Clear", StatusText(status));

            Press(app, Key.Esc);
            Tick(controller);
            Assert.True(bar.Visible);
            Assert.EndsWith(SearchKeys + "  Esc Close", StatusText(status));

            Press(app, Key.Esc);
            Tick(controller);
            Assert.False(bar.Visible);
            Assert.Equal(paneHints, StatusText(status));
        });
    }

    [Fact]
    public void Esc_with_text_clears_the_field_and_keeps_the_bar_open_with_no_status_and_the_next_Esc_closes_it()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            StatusLine status = Field<StatusLine>(controller, "_status");
            TypeHitInOutput(temp, app, controller);
            Assert.Equal("1 of 3", bar.Status);

            Press(app, Key.Esc);
            Tick(controller);

            Assert.True(bar.Visible);
            Assert.True(bar.HasFieldFocus);
            Assert.Equal(string.Empty, bar.Query.Text);
            Assert.Equal(string.Empty, bar.Status);
            Assert.Equal(new SearchStatus(0, 0), output.SearchStatus());
            Assert.EndsWith(SearchKeys + "  Esc Close", StatusText(status));

            Press(app, Key.Esc);

            Assert.False(bar.Visible);
        });
    }

    [Fact]
    public void After_Esc_clears_and_Esc_closes_F3_with_the_bar_closed_reopens_with_the_cleared_query()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            TypeHitInOutput(temp, app, controller);
            Press(app, Key.Esc);
            Press(app, Key.Esc);
            Assert.False(bar.Visible);
            Press(app, Key.F3);

            Assert.True(bar.Visible);
            Assert.True(output.HasFocus);
            Assert.Equal("hit", bar.Query.Text);
            Assert.EndsWith(" of 3", bar.Status);
        });
    }

    [Fact]
    public void Up_and_Down_step_through_the_terms_of_this_session_and_Down_past_the_newest_restores_the_draft()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "alpha line\nbeta line\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();

            Press(app, (Key)'/');
            Type(app, "alpha");
            Press(app, Key.Enter);
            Press(app, (Key)'/');
            Type(app, "beta");
            Press(app, Key.Enter);
            Press(app, (Key)'/');
            Type(app, "draft");
            Assert.Equal("draft", bar.Query.Text);

            Press(app, Key.CursorUp);
            Assert.Equal("beta", bar.Query.Text);
            Press(app, Key.CursorUp);
            Assert.Equal("alpha", bar.Query.Text);
            Press(app, Key.CursorUp);
            Assert.Equal("alpha", bar.Query.Text);
            Press(app, Key.CursorDown);
            Assert.Equal("beta", bar.Query.Text);
            Press(app, Key.CursorDown);
            Assert.Equal("draft", bar.Query.Text);
            Assert.True(bar.HasFieldFocus);
            Assert.True(bar.Visible);
        });
    }

    [Fact]
    public void Up_puts_the_cursor_at_the_end_of_the_term_shown()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "alpha line\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "alpha");
            Press(app, Key.Enter);
            Press(app, (Key)'/');

            Press(app, Key.CursorUp);

            Assert.Equal("alpha", bar.Query.Text);
            Assert.Equal(5, InsertionPointOf(bar));
        });
    }

    [Fact]
    public void Up_updates_the_match_count_live()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "alpha one\nalpha two\nbeta\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "alpha");
            Press(app, Key.Enter);
            Press(app, (Key)'/');
            Type(app, "beta");
            Assert.Equal("1 of 1", bar.Status);

            Press(app, Key.CursorUp);

            Assert.Equal("alpha", bar.Query.Text);
            Assert.Equal(new SearchStatus(2, 1), output.SearchStatus());
            Assert.Equal("1 of 2", bar.Status);
        });
    }

    /// <summary>A selection in the output pane (press, drag, release at view cells, the end cell included) is the query of '/'.</summary>
    [Fact]
    public void Slash_with_a_selection_in_the_output_seeds_the_field_and_the_selected_occurrence_is_the_active_match()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "hit one\nhit two\nhit three\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();

            // "hit" on the second line.
            SelectInOutput(app, output, 0, 1, 2, 1);
            Press(app, (Key)'/');

            Assert.Equal("hit", bar.Query.Text);
            Assert.Equal("2 of 3", bar.Status);
            Assert.True(output.HasSelection);
        });
    }

    /// <summary>With Regex on, the selected text is escaped, so that it matches itself.</summary>
    [Fact]
    public void Slash_with_Regex_on_escapes_the_selected_text()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "xa.by a-b\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Press(app, Key.R.WithAlt);
            Press(app, Key.Esc);
            Assert.False(bar.Visible);

            // "a.b" in the first line (the "a-b" after it does not match the escaped text).
            SelectInOutput(app, output, 1, 0, 3, 0);
            Press(app, (Key)'/');

            Assert.Equal(@"a\.b", bar.Query.Text);
            Assert.True(bar.Query.Regex);
            Assert.Equal("1 of 1", bar.Status);
        });
    }

    /// <summary>With the bar open and the focus back in the output, a new selection replaces the text.</summary>
    [Fact]
    public void Slash_with_the_bar_open_replaces_the_text_with_a_new_selection()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "hit one\nhit two\nhit three\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "hit");
            Press(app, Key.Enter);
            Assert.True(output.HasFocus);

            // "two" on the second line.
            SelectInOutput(app, output, 4, 1, 6, 1);
            Press(app, (Key)'/');

            Assert.True(bar.HasFieldFocus);
            Assert.Equal("two", bar.Query.Text);
            Assert.Equal("1 of 1", bar.Status);
        });
    }

    /// <summary>A selection across lines is not a seed: the query stays as it was.</summary>
    [Fact]
    public void Slash_with_a_multi_line_selection_keeps_the_previous_query()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            OutputView output = Field<OutputView>(controller, "_output");
            WriteOutput(temp, "hit one\nhit two\nhit three\n");
            ShowFirstCommand(app, controller);
            output.SetFocus();
            app.LayoutAndDraw();
            Press(app, (Key)'/');
            Type(app, "hit");
            Press(app, Key.Enter);

            SelectInOutput(app, output, 0, 0, 2, 1);
            Press(app, (Key)'/');

            Assert.True(bar.HasFieldFocus);
            Assert.Equal("hit", bar.Query.Text);
        });
    }

    /// <summary>Selects from one cell of the output pane to another, through its own mouse handling (press, drag, release).</summary>
    private static void SelectInOutput(IApplication app, OutputView output, int fromX, int fromY, int toX, int toY)
    {
        SendMouse(output, MouseFlags.LeftButtonPressed, fromX, fromY);
        SendMouse(output, MouseFlags.LeftButtonPressed, toX, toY);
        SendMouse(output, MouseFlags.LeftButtonReleased, toX, toY);
        app.LayoutAndDraw();
    }

    private static void SendMouse(OutputView output, MouseFlags flags, int x, int y) =>
        typeof(OutputView).GetMethod("OnMouseEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(output, new object[] { new Mouse { Flags = flags, Position = new(x, y) } });

    /// <summary>The cursor position of the bar's find field, in graphemes.</summary>
    private static int InsertionPointOf(SearchBar bar) =>
        ((Terminal.Gui.Views.TextField)typeof(SearchBar).GetField("_field", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!)
        .InsertionPoint;

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>A session with one group "work" holding a command "hit-build" (run as "hit-tool") and a command "other".</summary>
    private static TempSession Session() => TempSession.Open(
        new GroupSpec
        {
            Name = "work",
            Items = { new CommandSpec { Name = "hit-build", Program = "hit-tool" }, new CommandSpec { Name = "other", Program = "sh" } },
        });

    /// <summary>A command with the given name, run by the shell.</summary>
    private static CommandSpec Command(string name) => new() { Name = name, Program = "sh" };

    /// <summary>Writes the text to the output of the first command of the session, without starting it.</summary>
    private static void WriteOutput(TempSession temp, string text) =>
        temp.Session.AllEntries.First().Process.Output.WriteStdOut(text);

    /// <summary>Writes "hit one, hit two, hit three" to the first command's output, focuses the output and types "hit" into the search.</summary>
    private static void TypeHitInOutput(TempSession temp, IApplication app, MainController controller)
    {
        WriteOutput(temp, "hit one\nhit two\nhit three\n");
        ShowFirstCommand(app, controller);
        Field<OutputView>(controller, "_output").SetFocus();
        app.LayoutAndDraw();
        Press(app, (Key)'/');
        Type(app, "hit");
    }

    /// <summary>A left press at the screen position, through the application's mouse routing.</summary>
    private static void Click(IApplication app, int x, int y)
    {
        app.Mouse.RaiseMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed, ScreenPosition = new(x, y) });
        app.LayoutAndDraw();
    }

    /// <summary>The attribute of the screen cell at the position.</summary>
    private static Terminal.Gui.Drawing.Attribute AttributeAt(IApplication app, int row, int column) =>
        app.Driver!.Contents![row, column].Attribute!.Value;

    /// <summary>Moves the list focus to the first command, so that the output and the info pane show it.</summary>
    private static void ShowFirstCommand(IApplication app, MainController controller)
    {
        Field<CommandListView>(controller, "_list").SetFocus();
        Press(app, Key.CursorDown);
        Tick(controller);
    }

    /// <summary>Sends one key through the application's routing, then lays out and draws.</summary>
    private static void Press(IApplication app, Key key)
    {
        app.Keyboard.RaiseKeyDownEvent(key);
        app.LayoutAndDraw();
    }

    /// <summary>Sends each character of the text as a key.</summary>
    private static void Type(IApplication app, string text)
    {
        foreach (char ch in text)
        {
            Press(app, (Key)ch);
        }
    }

    /// <summary>One fast tick of the controller, which retargets the search and refreshes its status.</summary>
    private static void Tick(MainController controller) =>
        typeof(MainController).GetMethod("OnFastTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null);

    /// <summary>The text the status line shows.</summary>
    private static string StatusText(Cmdr.Tui.StatusLine status) =>
        (string)typeof(Cmdr.Tui.StatusLine).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(status)!;

    /// <summary>The characters drawn on one row of the screen, left to right.</summary>
    private static string ScreenRow(IApplication app, int row, int width)
    {
        var text = new StringBuilder();
        for (int column = 0; column < width; column++)
        {
            text.Append(app.Driver!.Contents![row, column].Grapheme);
        }

        return text.ToString();
    }

    /// <summary>Builds the controller at the given size, lays it out and runs <paramref name="check"/> on the UI loop.</summary>
    /// <remarks>
    /// Assertions inside <paramref name="check"/> run on the Terminal.Gui loop. Their failures are rethrown here after the
    /// application stops, so they fail the test as usual.
    /// </remarks>
    private static void RunLaidOut(TempSession temp, int width, int height, Action<IApplication, MainController> check)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);
        using var controller = new MainController(app, temp.Session);
        controller.Build();

        ExceptionDispatchInfo? failure = null;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            try
            {
                app.LayoutAndDraw();
                check(app, controller);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                app.RequestStop(controller.Window);
            }

            return false;
        });
        app.Run(controller.Window);

        failure?.Throw();
    }

    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
}
