using System.Diagnostics;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cfgedit.Tests;

/// <summary>The Terminal.Gui tests run one at a time, as the Logvu and Cmdr tests do.</summary>
[CollectionDefinition("Terminal GUI", DisableParallelization = true)]
public sealed class TerminalGuiCollection
{
}

/// <summary>
/// The tree view: its drawn rows and attributes, the keys, the mouse and the wheel, and the document after an edit. Each test draws
/// the view through <see cref="TerminalHarness"/> and checks the text and the attributes that were drawn.
/// </summary>
[Collection("Terminal GUI")]
public sealed class TreeViewTests
{
    // The JSON sample: rows 0 to 6 at width 40. The tags array is expanded, so its items are rows 4 and 5.
    private const string JsonSample = "{\n  \"name\": \"web\",\n  \"port\": 8080,\n  \"tags\": [\"a\", \"b\"],\n  \"empty\": \"\"\n}";

    private static readonly string[] JsonRows =
    [
        "\x25BE $ {…} (4)",
        "    name: web",
        "    port: 8080",
        "  \x25BE tags: […] (2)",
        "      0: a",
        "      1: b",
        "    empty: \"\"",
    ];

    private const int Width = 40;
    private const int Height = 10;

    [Fact]
    public void Json_rows_are_drawn_with_the_cursor_on_the_root_summary()
    {
        TerminalScreen screen = Draw(View(JsonSample, "json"))[0];

        Assert.Equal(JsonRows, Rows(screen, JsonRows.Length));
        Assert.Equal(string.Empty, screen.Text(JsonRows.Length));
        Assert.Equal("{…} (4)", Reversed(screen));
    }

    [Fact]
    public void The_cursor_row_is_tinted_and_the_other_rows_have_the_normal_background()
    {
        TerminalScreen screen = Draw(View(JsonSample, "json"))[0];
        Color tint = screen.Cells[0][0].Attribute.Background;

        Assert.NotEqual(screen.Normal.Background, tint);
        Assert.Equal(ExpectedTint(screen.Normal), tint);
        Assert.All(screen.Cells[0], cell => Assert.Equal(tint, cell.Attribute.Background));
        Assert.All(screen.Cells[1], cell => Assert.Equal(screen.Normal.Background, cell.Attribute.Background));
    }

    [Fact]
    public void Json_keys_strings_numbers_and_empty_strings_have_their_colours()
    {
        TerminalScreen screen = Draw(View(JsonSample, "json"))[0];

        Assert.Equal(new Color(ColorName16.BrightBlue), screen.At("name").Foreground);
        Assert.Equal(new Color(0x98, 0xC3, 0x79), screen.At("web").Foreground);
        Assert.Equal(new Color(0xC6, 0x78, 0xDD), screen.At("8080").Foreground);
        Assert.True(screen.At("\"\"").Style.HasFlag(TextStyle.Faint));
        Assert.True(screen.At("[…] (2)").Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public void A_jsonc_comment_is_drawn_faint_with_its_markers()
    {
        const string text = "{\n  // note\n  \"a\": 1 /* tail */\n}";

        TerminalScreen screen = Draw(View(text, "jsonc"))[0];

        Assert.Equal("\x25BE $ {…} (1)", screen.Text(0));
        Assert.Equal("    // note", screen.Text(1).TrimEnd());
        Assert.True(screen.At("note").Style.HasFlag(TextStyle.Faint));
        Assert.True(screen.At("//").Style.HasFlag(TextStyle.Faint));
        Assert.Equal("    a: 1", screen.Text(2));
        Assert.Contains("tail", screen.Text(3), StringComparison.Ordinal);
    }

    [Fact]
    public void Json5_is_drawn_with_unquoted_keys_and_values_without_quotes()
    {
        const string text = "{ a: 'x', b: 0x1F, }";

        TerminalScreen screen = Draw(View(text, "json5"))[0];

        Assert.Equal(new[] { "\x25BE $ {…} (2)", "    a: x", "    b: 0x1F" }, Rows(screen, 3));
    }

    [Fact]
    public void Xml_elements_attributes_text_and_end_rows_are_drawn_expanded()
    {
        const string text = "<configuration>\n  <appSettings>\n    <add key=\"Env\" value=\"Prod\" />\n  </appSettings>\n  <note>hello</note>\n  <empty/>\n</configuration>";

        TerminalScreen screen = DrawAt(View(text, "xml"), 60, 12)[0];

        Assert.Equal(
            new[]
            {
                "\x25BE <configuration>",
                "  \x25BE <appSettings>",
                "      <add key=\"Env\" value=\"Prod\"/> (0)",
                "    </appSettings>",
                "    <note>hello</note>",
                "    <empty/> (0)",
                "  </configuration>",
            },
            Rows(screen, 7));
    }

    [Fact]
    public void A_collapsed_xml_element_shows_an_ellipsis_and_its_end_tag_and_has_no_end_row()
    {
        const string text = "<configuration>\n  <appSettings>\n    <add key=\"Env\" value=\"Prod\" />\n  </appSettings>\n</configuration>";
        var view = View(text, "xml");

        TerminalScreen screen = DrawAt(view, 60, 12, v => Press(v, Key.CursorDown), v => Press(v, Key.Space))[^1];

        Assert.Equal("\x25BE <configuration>", screen.Text(0));
        Assert.Equal("  \x25B8 <appSettings>\x2026</appSettings>", screen.Text(1));
        Assert.Equal("  </configuration>", screen.Text(2));
        Assert.Equal("appSettings", Reversed(screen));
    }

    [Fact]
    public void Xml_text_mixed_content_and_text_only_elements_are_drawn_in_their_order()
    {
        const string text = "<mixed>one<b>two</b>three</mixed>";

        TerminalScreen screen = DrawAt(View(text, "xml"), 60, 10)[0];

        Assert.Equal(
            new[] { "\x25BE <mixed>", "    one", "    <b>two</b>", "    three", "  </mixed>" },
            Rows(screen, 5));
    }

    [Fact]
    public void Xml_attributes_wrap_under_the_first_attribute_and_the_focused_attribute_is_reversed()
    {
        const string text = "<add key=\"ConnectionString\" value=\"Server=db;Database=app\" timeout=\"30\"/>";
        var view = View(text, "xml");

        // End goes to the element's last part, its summary, so Left goes back to the last attribute value.
        TerminalScreen screen = DrawAt(view, 40, 4, v => Press(v, Key.End), v => Press(v, Key.CursorLeft))[^1];

        Assert.Equal(
            new[]
            {
                "  <add key=\"ConnectionString\"",
                "       value=\"Server=db;Database=app\"",
                "       timeout=\"30\"/> (0)",
            },
            Rows(screen, 3));
        Assert.Equal("30", Reversed(screen));
    }

    [Fact]
    public void Xml_attributes_at_a_narrow_width_align_at_four_columns_and_an_attribute_wider_than_a_line_is_cut()
    {
        const string text = "<add key=\"ConnectionString\" value=\"Server=db;Database=app\" timeout=\"30\"/>";

        TerminalScreen screen = DrawAt(View(text, "xml"), 30, 4)[0];

        Assert.Equal(
            new[]
            {
                "  <add key=\"ConnectionString\"",
                "    value=\"Server=db;Database\x2026",
            },
            Rows(screen, 2));
        Assert.Equal(string.Empty, screen.Text(2));
    }

    // ----- Keys -----

    [Fact]
    public void Down_and_up_keep_the_part_and_return_to_the_preferred_part_on_a_longer_row()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorRight),
            v => Press(v, Key.CursorUp),
            v => Press(v, Key.CursorDown));

        Assert.Equal("{…} (4)", Reversed(screens[0]));
        Assert.Equal("web", Reversed(screens[2]));
        Assert.Equal("{…} (4)", Reversed(screens[3]));
        Assert.Equal("web", Reversed(screens[4]));
    }

    [Fact]
    public void Moving_down_takes_the_same_part_of_each_row_and_the_last_part_of_a_shorter_one()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorRight),
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorDown));

        Assert.Equal("name", Reversed(screens[1]));
        Assert.Equal("web", Reversed(screens[2]));
        Assert.Equal("8080", Reversed(screens[3]));
        Assert.Equal("[…] (2)", Reversed(screens[4]));
        Assert.Equal("a", Reversed(screens[5]));
        Assert.Equal("b", Reversed(screens[6]));
        Assert.Equal("\"\"", Reversed(screens[7]));
    }

    [Fact]
    public void Left_and_right_move_by_part_and_stop_at_the_ends_of_the_row()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorRight),
            v => Press(v, Key.CursorRight),
            v => Press(v, Key.CursorLeft),
            v => Press(v, Key.CursorLeft));

        Assert.Equal("name", Reversed(screens[1]));
        Assert.Equal("web", Reversed(screens[2]));
        Assert.Equal("web", Reversed(screens[3]));
        Assert.Equal("name", Reversed(screens[4]));
        Assert.Equal("{…} (4)", Reversed(screens[5]));
        Assert.Equal(0, view.CursorRow);
    }

    [Fact]
    public void Left_on_the_first_part_goes_to_the_parent_row_and_keeps_the_preferred_part()
    {
        const string text = "{\n  \"o\": {\n    \"k\": 1,\n    // c\n    \"z\": 2\n  }\n}";
        var view = View(text, "jsonc");

        // The comment row has one part, so Down takes its last part and keeps the preferred part 1. Left from its only part goes to
        // the parent "o", whose part 1 is its summary.
        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorRight),
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorLeft));

        Assert.Equal("k", Reversed(screens[2]));
        Assert.Equal("1", Reversed(screens[3]));
        Assert.Equal(" c", Reversed(screens[4]));
        Assert.Equal("{…} (2)", Reversed(screens[5]));
        Assert.Equal(1, view.PreferredPart);
        Assert.Equal(1, view.CursorRow);
    }

    [Fact]
    public void Left_from_the_root_summary_reaches_the_marker_and_a_second_left_does_nothing()
    {
        // The cursor opens on the summary, the part after the root marker. Left reaches the marker, which is the first part.
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(view, v => Press(v, Key.CursorLeft), v => Press(v, Key.CursorLeft));

        Assert.Equal("$", Reversed(screens[1]));
        Assert.Equal("$", Reversed(screens[2]));
        Assert.Equal(0, view.CursorRow);
    }

    [Fact]
    public void Down_from_the_marker_lands_on_the_first_members_key_and_up_from_that_key_lands_on_the_summary()
    {
        // The marker is not a column for up and down: from it, Down keeps the first part (the key), and Up onto the root row takes the summary.
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorLeft),
            v => Press(v, Key.CursorLeft),
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.CursorUp));

        Assert.Equal("$", Reversed(screens[2]));
        Assert.Equal("name", Reversed(screens[3]));
        Assert.Equal("{…} (4)", Reversed(screens[4]));
    }

    [Fact]
    public void Home_and_end_go_to_the_first_and_last_part_and_set_the_preferred_part()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.End),
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.Home));

        Assert.Equal("web", Reversed(screens[2]));
        Assert.Equal("8080", Reversed(screens[3]));
        Assert.Equal("port", Reversed(screens[4]));
    }

    [Fact]
    public void Zero_and_dollar_are_home_and_end_and_g_and_capital_g_go_to_the_first_and_last_row()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, (Key)'$'),
            v => Press(v, (Key)'0'),
            v => Press(v, (Key)'G'),
            v => Press(v, (Key)'g'));

        // $ is the end, the summary; 0 is the home, the marker, which is the root row's first part.
        Assert.Equal("{…} (4)", Reversed(screens[1]));
        Assert.Equal("$", Reversed(screens[2]));
        Assert.Equal("empty", Reversed(screens[3]));
        Assert.Equal("{…} (4)", Reversed(screens[4]));
        Assert.Equal(0, view.CursorRow);
    }

    [Fact]
    public void Page_down_and_page_up_move_a_viewport_of_display_lines_and_keep_the_screen_position()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = DrawAt(
            view,
            Width,
            3,
            v => Press(v, Key.PageDown),
            v => Press(v, Key.PageDown),
            v => Press(v, Key.PageDown),
            v => Press(v, Key.PageUp));

        Assert.Equal(new[] { "  \x25BE tags: […] (2)", "      0: a", "      1: b" }, Rows(screens[1], 3));
        Assert.Equal("tags", Reversed(screens[1]));
        Assert.Equal("0", Reversed(screens[2]));
        Assert.Equal("empty", Reversed(screens[3]));
        Assert.Equal("tags", Reversed(screens[4]));
        Assert.Equal(1, view.TopLine);
    }

    [Fact]
    public void Page_down_with_wrapped_rows_moves_by_display_lines_not_rows()
    {
        // The attribute row wraps to several display lines. Page down moves the top by the view's height in display lines.
        const string text = "<root>\n  <add key=\"ConnectionString\" value=\"Server=db;Database=app\" timeout=\"30\"/>\n  <x/>\n  <y/>\n</root>";
        var view = View(text, "xml");

        List<TerminalScreen> screens = DrawAt(
            view,
            40,
            4,
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.PageDown),
            v => Press(v, Key.PageDown));

        // Lines: 0 root, 1-3 the wrapped add, 4 x, 5 y, 6 the end of root. Page down from line 0 moves the top to line 3 and the cursor to
        // the row at its screen position, line 4. The next page is at the end, so the cursor goes to the last row.
        Assert.Equal("add", Reversed(screens[1]));
        Assert.Equal(new[] { "         timeout=\"30\"/> (0)", "    <x/> (0)", "    <y/> (0)", "  </root>" }, Rows(screens[2], 4));
        Assert.Equal("x", Reversed(screens[2]));
        Assert.Equal("root", Reversed(screens[3]));
        Assert.Equal(4, view.CursorRow);
        Assert.Equal(3, view.TopLine);
    }

    [Fact]
    public void Star_expands_the_whole_subtree_and_the_initial_collapse_is_kept_until_then()
    {
        const string text = "{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":1}}}}}";
        var view = View(text, "json");

        List<TerminalScreen> screens = Draw(view, v => Press(v, (Key)'*'));

        Assert.Equal("      \x25B8 c: {…} (1)", screens[0].Text(3));
        Assert.Equal("\x25BE $ {…} (1)", screens[1].Text(0));
        Assert.Equal("  \x25BE a: {…} (1)", screens[1].Text(1));
        Assert.Equal("    \x25BE b: {…} (1)", screens[1].Text(2));
        Assert.Equal("      \x25BE c: {…} (1)", screens[1].Text(3));
        Assert.Equal("        \x25BE d: {…} (1)", screens[1].Text(4));
        Assert.Equal("            e: 1", screens[1].Text(5));
    }

    [Fact]
    public void Space_folds_and_unfolds_a_collapsible_row()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.Space),
            v => Press(v, Key.Space));

        Assert.Equal("\x25B8 $ {…} (4)", screens[1].Text(0));
        Assert.Equal(string.Empty, screens[1].Text(1));
        Assert.Equal(JsonRows, Rows(screens[2], JsonRows.Length));
    }

    [Fact]
    public void Space_on_a_leaf_folds_its_parent_and_moves_the_cursor_there()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.Space));

        Assert.Equal("\x25B8 $ {…} (4)", screens[2].Text(0));
        Assert.Equal(string.Empty, screens[2].Text(1));
        Assert.Equal("{…} (4)", Reversed(screens[2]));
        Assert.Equal(0, view.CursorRow);
    }

    [Fact]
    public void Minus_collapses_the_parent_of_a_nested_row_and_moves_the_cursor_there()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown, 4),
            v => Press(v, (Key)'-'));

        Assert.Equal("  \x25B8 tags: […] (2)", screens[2].Text(3));
        Assert.Equal("    empty: \"\"", screens[2].Text(4));
        Assert.Equal("tags", Reversed(screens[2]));
        Assert.Equal(3, view.CursorRow);
    }

    [Fact]
    public void Space_on_an_end_row_collapses_its_element_and_moves_to_the_start_row()
    {
        const string text = "<configuration>\n  <appSettings>\n    <add key=\"Env\" value=\"Prod\" />\n  </appSettings>\n</configuration>";
        var view = View(text, "xml");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown, 2),
            v => Press(v, Key.CursorDown),
            v => Press(v, Key.Space));

        Assert.Equal("    </appSettings>", screens[2].Text(3));
        Assert.Equal("  \x25B8 <appSettings>\x2026</appSettings>", screens[3].Text(1));
        Assert.Equal("appSettings", Reversed(screens[3]));
        Assert.Equal(1, view.CursorRow);
    }

    [Fact]
    public void Left_from_an_end_row_goes_to_its_element_row()
    {
        const string text = "<configuration>\n  <appSettings>\n    <add key=\"Env\" value=\"Prod\" />\n  </appSettings>\n</configuration>";
        var view = View(text, "xml");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown, 3),
            v => Press(v, Key.CursorLeft));

        Assert.Equal("appSettings", Reversed(screens[1]));
        Assert.Equal(1, view.CursorRow);
    }

    [Fact]
    public void The_initial_collapse_folds_nodes_below_depth_three()
    {
        const string text = "{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":1}}}}}";
        var view = View(text, "json");

        TerminalScreen screen = Draw(view)[0];

        Assert.Equal("      \x25B8 c: {…} (1)", screen.Text(3));
        Assert.Equal(string.Empty, screen.Text(4));
    }

    [Fact]
    public void Printable_keys_that_the_tree_does_not_bind_are_not_handled()
    {
        var view = View(JsonSample, "json");

        Draw(view, v => Assert.False(v.HandleKey((Key)'x')));
    }

    // ----- Mouse -----

    [Fact]
    public void A_click_on_a_part_focuses_it()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(11, 2) })));

        Assert.Equal("8080", Reversed(screens[1]));
        Assert.Equal(2, view.CursorRow);
        Assert.Equal(1, view.CursorPart);
    }

    [Fact]
    public void A_click_past_the_parts_focuses_the_first_part_of_the_row()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.End),
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(30, 1) })));

        Assert.Equal("name", Reversed(screens[2]));
        Assert.Equal(0, view.CursorPart);
    }

    [Fact]
    public void A_click_on_a_fold_glyph_toggles_the_row_and_leaves_the_others_as_they_were()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(2, 3) })),
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(2, 3) })));

        Assert.Equal("  \x25B8 tags: […] (2)", screens[1].Text(3));
        Assert.Equal("    empty: \"\"", screens[1].Text(4));
        Assert.Equal("  \x25BE tags: […] (2)", screens[2].Text(3));
        Assert.Equal("      1: b", screens[2].Text(5));
    }

    [Fact]
    public void The_wheel_scrolls_by_three_display_lines_and_moves_the_cursor_when_its_row_leaves_the_view()
    {
        // Three lines of view on seven lines of rows: the wheel can move the top down to line 3 and back to 0.
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = DrawAt(
            view,
            Width,
            3,
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown })),
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.WheeledUp })));

        Assert.Equal(new[] { "  \x25BE tags: […] (2)", "      0: a", "      1: b" }, Rows(screens[1], 3));
        Assert.Equal("tags", Reversed(screens[1]));
        Assert.Equal(new[] { "\x25BE $ {…} (4)", "    name: web", "    port: 8080" }, Rows(screens[2], 3));
        Assert.Equal("port", Reversed(screens[2]));
        Assert.Equal(0, view.TopLine);
    }

    [Fact]
    public void The_wheel_keeps_the_cursor_while_its_row_is_still_in_view()
    {
        var view = View(JsonSample, "json");

        List<TerminalScreen> screens = DrawAt(
            view,
            Width,
            4,
            v => Press(v, Key.CursorDown, 3),
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown })));

        Assert.Equal(3, view.CursorRow);
        Assert.Equal(3, view.TopLine);
        Assert.Equal("tags", Reversed(screens[2]));
    }

    // ----- Document changes -----

    [Fact]
    public void Set_document_after_an_edit_keeps_the_folds_and_places_the_cursor_on_the_focus()
    {
        var view = View(JsonSample, "json");
        ConfigDocument before = TreeFixtures.Parse(JsonSample, "json");
        var port = new NodePath([new ItemStep(0), new MemberStep("port", 0)]);

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown, 3),
            v => Press(v, Key.Space),
            v =>
            {
                EditResult result = before.Apply(new SetPart(port, PartKind.Value, -1, "9090"));
                v.SetDocument(result.Document, result.Focus);
            });

        Assert.Equal("  \x25B8 tags: […] (2)", screens[2].Text(3));
        Assert.Equal("    port: 9090", screens[3].Text(2));
        Assert.Equal("port", Reversed(screens[3]));
        Assert.Equal(2, view.CursorRow);
    }

    [Fact]
    public void Set_document_with_a_focus_inside_a_collapsed_node_puts_the_cursor_on_that_node()
    {
        var view = View(JsonSample, "json");
        ConfigDocument document = TreeFixtures.Parse(JsonSample, "json");

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.CursorDown, 3),
            v => Press(v, Key.Space),
            v => v.SetDocument(document, new NodePath([new ItemStep(0), new MemberStep("tags", 0), new ItemStep(1)])));

        Assert.Equal("  \x25B8 tags: […] (2)", screens[3].Text(3));
        Assert.Equal("tags", Reversed(screens[3]));
        Assert.Equal(3, view.CursorRow);
    }

    // ----- Helpers -----

    private static TreeView View(string text, string format)
    {
        var view = new TreeView();
        view.SetDocument(TreeFixtures.Parse(text, format), null);
        return view;
    }

    private static List<TerminalScreen> Draw(TreeView view, params Action<TreeView>[] steps) => DrawAt(view, Width, Height, steps);

    private static List<TerminalScreen> DrawAt(TreeView view, int width, int height, params Action<TreeView>[] steps) =>
        TerminalHarness.Run(view, width, height, steps.Select(step => (Action)(() => step(view))).ToArray());

    private static void Press(TreeView view, Key key, int times = 1)
    {
        for (int i = 0; i < times; i++)
        {
            Assert.True(view.HandleKey(key), $"the tree did not handle {key}");
        }
    }

    private static string[] Rows(TerminalScreen screen, int count) => Enumerable.Range(0, count).Select(screen.Text).ToArray();

    /// <summary>The text of the reversed cells, row by row, concatenated. Only the focused part is reversed.</summary>
    private static string Reversed(TerminalScreen screen) => string.Concat(
        screen.Cells.SelectMany(row => row.Where(cell => cell.Attribute.Style.HasFlag(TextStyle.Reverse)).Select(cell => cell.Grapheme)));

    /// <summary>The cursor row's tint for a normal attribute, when the terminal reports no defaults: a share of the way toward the foreground.</summary>
    private static Color ExpectedTint(Attribute normal)
    {
        if (normal.Foreground.A == 0 || normal.Background.A == 0)
        {
            return new Color(0x26, 0x2A, 0x33);
        }

        int Mix(int from, int to) => from + (to - from) * 15 / 100;
        return new Color(
            Mix(normal.Background.R, normal.Foreground.R),
            Mix(normal.Background.G, normal.Foreground.G),
            Mix(normal.Background.B, normal.Foreground.B));
    }
}

/// <summary>Long values: a layout costs the width, not the length of the value.</summary>
public sealed class LongValueLayoutTests
{
    [Fact]
    public void A_one_million_character_value_lays_out_without_walking_the_whole_value_and_the_same_as_its_first_thousand()
    {
        const int width = 80;
        string million = new('a', 1_000_000);
        string thousand = million[..1_000];
        DocNode longMember = Member(million);
        DocNode shortMember = Member(thousand);

        // Allocation, not time, is the oracle: deterministic under load. Building cells for the whole value would allocate
        // tens of megabytes; the capped layout needs a few kilobytes.
        RowLayout.Layout(new Row(shortMember, false, 1), false, width);
        long before = GC.GetAllocatedBytesForCurrentThread();
        RowLines longLayout = RowLayout.Layout(new Row(longMember, false, 1), false, width);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        RowLines shortLayout = RowLayout.Layout(new Row(shortMember, false, 1), false, width);

        Assert.True(allocated < 1_000_000, $"the layout allocated {allocated} bytes");
        Assert.Equal(Flatten(shortLayout), Flatten(longLayout));
        Assert.Equal(shortLayout.Parts, longLayout.Parts);
        Assert.Equal(width, Flatten(longLayout).Length);
        Assert.EndsWith("\x2026", Flatten(longLayout), StringComparison.Ordinal);
    }

    [Fact]
    public void The_detail_value_of_a_one_million_character_value_is_cut_to_the_width_as_its_first_thousand_are()
    {
        string million = new('b', 1_000_000);

        Assert.Equal(RowLayout.DisplayValue(million[..1_000], 60), RowLayout.DisplayValue(million, 60));
    }

    private static DocNode Member(string value)
    {
        ConfigDocument document = TreeFixtures.Parse("{\"s\": \"" + value + "\"}", "json");
        return TreeFixtures.Find(document, new ItemStep(0), new MemberStep("s", 0));
    }

    private static string Flatten(RowLines lines) =>
        string.Join("|", lines.Lines.Select(line => line.Indent + ":" + string.Concat(line.Segments.Select(segment => segment.Text))));
}
