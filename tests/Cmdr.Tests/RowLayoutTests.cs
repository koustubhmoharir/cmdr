using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class RowLayoutTests
{
    private static StatusText Running(string detail = "0:05") => new("●", StatusKind.Running, "12:00:00", detail);

    [Fact]
    public void Command_row_is_exactly_the_requested_width()
    {
        CommandRowText row = RowLayout.FormatCommandRow(60, marked: false, Running(), "web");

        Assert.Equal(60, TextWidth.Columns(row.Text));
    }

    [Fact]
    public void Glyph_is_at_column_4_with_the_mark_in_column_2()
    {
        CommandRowText row = RowLayout.FormatCommandRow(60, marked: false, Running(), "web");

        Assert.Equal(RowLayout.GlyphColumn, row.GlyphColumn);
        Assert.Equal(4, row.GlyphColumn);
        Assert.Equal("●", row.Text.Substring(4, 1));
        Assert.Equal(" ", row.Text.Substring(2, 1));
    }

    [Fact]
    public void Started_and_detail_are_aligned_at_the_end_of_the_row()
    {
        CommandRowText row = RowLayout.FormatCommandRow(60, marked: false, Running("1:23"), "web");

        Assert.Equal(60, TextWidth.Columns(row.Text));
        Assert.EndsWith(" 1:23", row.Text);
        Assert.Contains("12:00:00", row.Text);
        Assert.Equal("1:23", row.Text.TrimEnd().Split(' ').Last());
        Assert.Equal(' ', row.Text[row.Text.Length - 1 - "1:23".Length]);
    }

    [Fact]
    public void Name_is_left_aligned_after_the_glyph()
    {
        CommandRowText row = RowLayout.FormatCommandRow(60, marked: false, Running(), "web");

        Assert.StartsWith("    ● web", row.Text);
    }

    [Fact]
    public void Long_names_are_truncated_with_an_ellipsis()
    {
        string name = new string('x', 80);

        CommandRowText row = RowLayout.FormatCommandRow(40, marked: false, Running(), name);

        Assert.Equal(40, TextWidth.Columns(row.Text));
        Assert.Contains("…", row.Text);
        Assert.DoesNotContain(new string('x', 80), row.Text);
    }

    [Fact]
    public void Marked_rows_show_the_square_mark()
    {
        CommandRowText marked = RowLayout.FormatCommandRow(60, marked: true, Running(), "web");
        CommandRowText unmarked = RowLayout.FormatCommandRow(60, marked: false, Running(), "web");

        Assert.Equal("■", marked.Text.Substring(2, 1));
        Assert.Equal(" ", unmarked.Text.Substring(2, 1));
        Assert.Equal(marked.GlyphColumn, unmarked.GlyphColumn);
    }

    [Fact]
    public void Wide_names_are_truncated_by_columns()
    {
        const string name = "日本語のコマンド名です";

        CommandRowText row = RowLayout.FormatCommandRow(40, marked: false, Running(), name);

        Assert.Equal(40, TextWidth.Columns(row.Text));
        Assert.Equal("●", row.Text.Substring(4, 1));
    }

    [Fact]
    public void Narrow_widths_never_throw_and_never_exceed_the_width()
    {
        var status = new StatusText("✗", StatusKind.Failure, "—", "exit 1 · 0:01");

        for (int width = 0; width <= 80; width++)
        {
            CommandRowText row = RowLayout.FormatCommandRow(width, marked: true, status, "some-command-name");

            Assert.Equal(width, TextWidth.Columns(row.Text));
            if (row.GlyphColumn >= 0)
            {
                Assert.True(row.GlyphColumn < width, $"glyph column {row.GlyphColumn} outside width {width}");
            }
        }
    }

    [Fact]
    public void Glyph_is_hidden_when_the_row_is_too_narrow_for_it()
    {
        Assert.Equal(-1, RowLayout.FormatCommandRow(4, marked: false, Running(), "web").GlyphColumn);
        Assert.Equal(-1, RowLayout.FormatCommandRow(0, marked: false, Running(), "web").GlyphColumn);
        Assert.Equal(string.Empty, RowLayout.FormatCommandRow(0, marked: false, Running(), "web").Text);
    }

    [Fact]
    public void Time_columns_are_dropped_when_there_is_no_room_for_them()
    {
        CommandRowText row = RowLayout.FormatCommandRow(20, marked: false, Running(), "web");

        Assert.DoesNotContain("12:00:00", row.Text);
        Assert.StartsWith("    ● web", row.Text);
    }

    [Theory]
    [InlineData("●")]
    [InlineData("◐")]
    [InlineData("…")]
    [InlineData("○")]
    [InlineData("✓")]
    [InlineData("✗")]
    [InlineData("⚠")]
    [InlineData("■")]
    [InlineData("▾")]
    [InlineData("▶")]
    public void Glyphs_are_one_column_wide(string glyph)
    {
        Assert.Equal(1, TextWidth.Columns(glyph));
    }

    [Fact]
    public void Group_row_shows_name_and_mode_in_brackets()
    {
        string text = RowLayout.FormatGroupRow(40, "build", GroupMode.Sequential, sequenceRunning: false);

        Assert.Equal(40, TextWidth.Columns(text));
        Assert.StartsWith("▾ build  [sequential]", text);
        Assert.DoesNotContain("sequence running", text);
    }

    [Fact]
    public void Group_row_appends_sequence_running_while_a_sequence_runs()
    {
        string text = RowLayout.FormatGroupRow(60, "build", GroupMode.Parallel, sequenceRunning: true);

        Assert.Equal(60, TextWidth.Columns(text));
        Assert.Contains("[parallel]", text);
        Assert.Contains("▶ sequence running", text);
    }

    [Fact]
    public void Group_row_is_truncated_to_the_width()
    {
        string text = RowLayout.FormatGroupRow(12, "a-very-long-group-name", GroupMode.Parallel, sequenceRunning: true);

        Assert.Equal(12, TextWidth.Columns(text));
        Assert.EndsWith("…", text.TrimEnd());
    }

    [Fact]
    public void TextWidth_truncate_and_fit_are_exact()
    {
        Assert.Equal("abc…", TextWidth.Truncate("abcdef", 4));
        Assert.Equal("abcdef", TextWidth.Truncate("abcdef", 6));
        Assert.Equal("abc   ", TextWidth.Fit("abc", 6));
        Assert.Equal("   abc", TextWidth.FitRight("abc", 6));
        Assert.Equal("…", TextWidth.Truncate("abc", 1));
        Assert.Equal(string.Empty, TextWidth.Truncate("abc", 0));
    }
}
