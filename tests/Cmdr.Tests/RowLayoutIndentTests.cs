using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Shared.Tui;

namespace Cmdr.Tests;

/// <summary>The indentation of nested rows: two columns per depth, with the columns reported for the glyph and the name.</summary>
public sealed class RowLayoutIndentTests
{
    private static readonly StatusText Running = new("↻", StatusKind.Running, "12:00:00", "0:05");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Command_main_line_is_exactly_the_width_at_every_depth_and_width(int depth)
    {
        for (int width = 0; width <= 60; width++)
        {
            CommandMainText row = RowLayout.FormatCommandMain(width, marked: true, "✗", "some-command", depth);

            Assert.Equal(width, TextWidth.Columns(row.Text));
            Assert.True(row.GlyphColumn < width, $"glyph column {row.GlyphColumn} outside width {width}");
            Assert.True(row.NameColumn < width, $"name column {row.NameColumn} outside width {width}");
        }
    }

    [Theory]
    [InlineData(0, 2, 4)]
    [InlineData(1, 4, 6)]
    [InlineData(2, 6, 8)]
    [InlineData(3, 8, 10)]
    public void Glyph_and_name_columns_move_two_per_depth(int depth, int glyphColumn, int nameColumn)
    {
        CommandMainText row = RowLayout.FormatCommandMain(60, marked: false, "↻", "web", depth);

        Assert.Equal(glyphColumn, row.GlyphColumn);
        Assert.Equal(nameColumn, row.NameColumn);
        Assert.Equal("↻", row.Text.Substring(glyphColumn, 1));
        Assert.Equal("web", row.Text.Substring(nameColumn, 3));
        Assert.StartsWith(new string(' ', glyphColumn) + "↻", row.Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Group_glyph_is_at_its_column_and_matches_the_glyph_of_a_command_one_depth_shallower(int depth)
    {
        foreach (bool collapsed in new[] { false, true })
        {
            string header = RowLayout.FormatGroupRow(60, "grp", GroupMode.Parallel, false, collapsed, string.Empty, depth);
            string glyph = collapsed ? "▸" : "▾";

            Assert.Equal(RowLayout.GroupGlyphColumn(depth), header.IndexOf(glyph, StringComparison.Ordinal));
        }

        CommandMainText command = RowLayout.FormatCommandMain(60, marked: false, "↻", "web", depth);
        Assert.Equal(RowLayout.GroupGlyphColumn(depth + 1), command.GlyphColumn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Mark_is_at_column_0_at_every_depth_while_glyph_and_name_stay_put(int depth)
    {
        CommandMainText marked = RowLayout.FormatCommandMain(60, marked: true, "↻", "web", depth);
        CommandMainText unmarked = RowLayout.FormatCommandMain(60, marked: false, "↻", "web", depth);

        Assert.Equal(RowLayout.MarkColumn, 0);
        Assert.Equal("→", marked.Text.Substring(0, 1));
        Assert.Equal(" ", unmarked.Text.Substring(0, 1));
        Assert.Equal(new string(' ', 2 * depth + 1), marked.Text.Substring(1, 2 * depth + 1));
        Assert.Equal(2 * depth + 2, marked.GlyphColumn);
        Assert.Equal(2 * depth + 4, marked.NameColumn);
        Assert.Equal("↻", marked.Text.Substring(marked.GlyphColumn, 1));
        Assert.Equal("web", marked.Text.Substring(marked.NameColumn, 3));
        Assert.Equal(unmarked.GlyphColumn, marked.GlyphColumn);
        Assert.Equal(unmarked.NameColumn, marked.NameColumn);
    }

    [Fact]
    public void Top_level_command_line_is_unchanged_by_the_default_depth()
    {
        Assert.Equal(RowLayout.FormatCommandMain(40, true, "↻", "web"), RowLayout.FormatCommandMain(40, true, "↻", "web", depth: 0));
    }

    [Fact]
    public void Name_is_truncated_to_the_space_left_after_the_indent()
    {
        string name = new string('n', 80);

        CommandMainText row = RowLayout.FormatCommandMain(30, marked: false, "↻", name, depth: 2);

        Assert.Equal(30, TextWidth.Columns(row.Text));
        Assert.Equal(8, row.NameColumn);
        Assert.Equal(22, TextWidth.Columns(row.Text.Substring(8)));
        Assert.EndsWith("…", row.Text);
    }

    [Fact]
    public void Narrow_nested_command_drops_the_name_then_the_glyph()
    {
        // Depth 2: glyph at 6, name at 8.
        CommandMainText glyphOnly = RowLayout.FormatCommandMain(7, marked: false, "↻", "web", depth: 2);
        CommandMainText blankIndent = RowLayout.FormatCommandMain(3, marked: false, "↻", "web", depth: 2);

        Assert.Equal(-1, glyphOnly.NameColumn);
        Assert.Equal(6, glyphOnly.GlyphColumn);
        Assert.Equal(7, TextWidth.Columns(glyphOnly.Text));
        Assert.Equal(-1, blankIndent.GlyphColumn);
        Assert.Equal(-1, blankIndent.NameColumn);
        Assert.Equal("   ", blankIndent.Text);
    }

    [Fact]
    public void A_width_inside_the_indent_gives_blank_text()
    {
        CommandMainText row = RowLayout.FormatCommandMain(4, marked: true, "↻", "web", depth: 3);

        Assert.Equal("    ", row.Text);
        Assert.Equal(-1, row.GlyphColumn);
    }

    [Fact]
    public void Command_detail_is_indented_to_the_name_column_of_its_depth()
    {
        string text = RowLayout.FormatCommandDetail(50, RowLayout.CommandDetailIndent(2), Running);

        Assert.Equal(50, TextWidth.Columns(text));
        Assert.Equal(8, RowLayout.CommandDetailIndent(2));
        Assert.Equal(new string(' ', 8), text.Substring(0, 8));
        Assert.Equal("12:00:00  0:05", text.Substring(8).TrimEnd());
    }

    [Fact]
    public void Command_detail_at_top_level_keeps_the_name_column()
    {
        Assert.Equal(RowLayout.NameColumn, RowLayout.CommandDetailIndent(0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Group_row_is_exactly_the_width_at_every_depth_and_width(int depth)
    {
        for (int width = 0; width <= 50; width++)
        {
            string text = RowLayout.FormatGroupRow(width, "build", GroupMode.Sequential, sequenceRunning: true, collapsed: true, summary: "1 running", depth);

            Assert.Equal(width, TextWidth.Columns(text));
        }
    }

    [Fact]
    public void Group_row_starts_after_two_columns_per_depth()
    {
        string text = RowLayout.FormatGroupRow(40, "nav", GroupMode.Parallel, sequenceRunning: false, collapsed: false, summary: string.Empty, depth: 2);

        Assert.Equal(40, TextWidth.Columns(text));
        Assert.Equal("    ▾ nav  [parallel]", text.TrimEnd());
    }

    [Fact]
    public void Collapsed_nested_header_keeps_its_summary_after_the_mode()
    {
        string text = RowLayout.FormatGroupRow(60, "deep", GroupMode.Sequential, sequenceRunning: false, collapsed: true, summary: "2 failed", depth: 1);

        Assert.Equal("  ▸ deep  [sequential]  2 failed", text.TrimEnd());
    }

    [Fact]
    public void Group_row_narrower_than_its_indent_is_blank()
    {
        Assert.Equal("   ", RowLayout.FormatGroupRow(3, "nav", GroupMode.Parallel, false, false, string.Empty, depth: 2));
        Assert.Equal(string.Empty, RowLayout.FormatGroupRow(0, "nav", GroupMode.Parallel, false, false, string.Empty, depth: 2));
    }

    [Fact]
    public void Indent_is_two_columns_per_depth_and_never_negative()
    {
        Assert.Equal(0, RowLayout.IndentOf(0));
        Assert.Equal(2, RowLayout.IndentOf(1));
        Assert.Equal(6, RowLayout.IndentOf(3));
        Assert.Equal(0, RowLayout.IndentOf(-1));
    }
}
