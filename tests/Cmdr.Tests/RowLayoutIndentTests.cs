using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The indentation of nested rows: two columns per depth, with the columns reported for the glyph and the name.</summary>
public sealed class RowLayoutIndentTests
{
    private static readonly StatusText Running = new("●", StatusKind.Running, "12:00:00", "0:05");

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
    [InlineData(0, 4, 6)]
    [InlineData(1, 6, 8)]
    [InlineData(2, 8, 10)]
    [InlineData(3, 10, 12)]
    public void Glyph_and_name_columns_move_two_per_depth(int depth, int glyphColumn, int nameColumn)
    {
        CommandMainText row = RowLayout.FormatCommandMain(60, marked: false, "●", "web", depth);

        Assert.Equal(glyphColumn, row.GlyphColumn);
        Assert.Equal(nameColumn, row.NameColumn);
        Assert.Equal("●", row.Text.Substring(glyphColumn, 1));
        Assert.Equal("web", row.Text.Substring(nameColumn, 3));
        Assert.StartsWith(new string(' ', glyphColumn) + "●", row.Text);
    }

    [Fact]
    public void Top_level_command_line_is_unchanged_by_the_default_depth()
    {
        Assert.Equal(RowLayout.FormatCommandMain(40, true, "●", "web"), RowLayout.FormatCommandMain(40, true, "●", "web", depth: 0));
    }

    [Fact]
    public void Name_is_truncated_to_the_space_left_after_the_indent()
    {
        string name = new string('n', 80);

        CommandMainText row = RowLayout.FormatCommandMain(30, marked: false, "●", name, depth: 2);

        Assert.Equal(30, TextWidth.Columns(row.Text));
        Assert.Equal(10, row.NameColumn);
        Assert.Equal(20, TextWidth.Columns(row.Text.Substring(10)));
        Assert.EndsWith("…", row.Text);
    }

    [Fact]
    public void Narrow_nested_command_drops_the_name_then_the_glyph()
    {
        // Depth 2: glyph at 8, name at 10.
        CommandMainText glyphOnly = RowLayout.FormatCommandMain(9, marked: false, "●", "web", depth: 2);
        CommandMainText blankIndent = RowLayout.FormatCommandMain(3, marked: false, "●", "web", depth: 2);

        Assert.Equal(-1, glyphOnly.NameColumn);
        Assert.Equal(8, glyphOnly.GlyphColumn);
        Assert.Equal(9, TextWidth.Columns(glyphOnly.Text));
        Assert.Equal(-1, blankIndent.GlyphColumn);
        Assert.Equal(-1, blankIndent.NameColumn);
        Assert.Equal("   ", blankIndent.Text);
    }

    [Fact]
    public void A_width_inside_the_indent_gives_blank_text()
    {
        CommandMainText row = RowLayout.FormatCommandMain(4, marked: true, "●", "web", depth: 3);

        Assert.Equal("    ", row.Text);
        Assert.Equal(-1, row.GlyphColumn);
    }

    [Fact]
    public void Command_detail_is_indented_to_the_name_column_of_its_depth()
    {
        string text = RowLayout.FormatCommandDetail(50, RowLayout.CommandDetailIndent(2), Running);

        Assert.Equal(50, TextWidth.Columns(text));
        Assert.Equal(10, RowLayout.CommandDetailIndent(2));
        Assert.Equal(new string(' ', 10), text.Substring(0, 10));
        Assert.Equal("12:00:00  0:05", text.Substring(10).TrimEnd());
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
