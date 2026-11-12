using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class RowLayoutTests
{
    private static StatusText Running(string detail = "0:05") => new("↻", StatusKind.Running, "12:00:00", detail);

    private static CommandMainText MainLine(int width, bool marked = false, string name = "web") =>
        RowLayout.FormatCommandMain(width, marked, "↻", name);

    [Fact]
    public void Main_line_is_exactly_the_requested_width()
    {
        CommandMainText row = MainLine(60);

        Assert.Equal(60, TextWidth.Columns(row.Text));
    }

    [Fact]
    public void Glyph_is_at_column_2_and_name_at_column_4()
    {
        CommandMainText row = MainLine(60);

        Assert.Equal(RowLayout.GlyphColumn, row.GlyphColumn);
        Assert.Equal(2, row.GlyphColumn);
        Assert.Equal(RowLayout.NameColumn, row.NameColumn);
        Assert.Equal(4, row.NameColumn);
        Assert.Equal("↻", row.Text.Substring(2, 1));
        Assert.Equal(" ", row.Text.Substring(1, 1));
        Assert.Equal("web", row.Text.Substring(4, 3));
    }

    [Fact]
    public void Main_line_starts_with_the_name_after_the_glyph()
    {
        Assert.StartsWith("  ↻ web", MainLine(60).Text);
    }

    [Fact]
    public void Name_uses_all_remaining_width()
    {
        // 40 columns: 4 for the prefix, 36 for the name.
        string name = new string('n', 36);

        CommandMainText row = MainLine(40, name: name);

        Assert.Equal(40, TextWidth.Columns(row.Text));
        Assert.EndsWith(name, row.Text);
        Assert.DoesNotContain("…", row.Text);
    }

    [Fact]
    public void Names_that_fit_the_full_width_are_shown_in_full()
    {
        foreach (string name in new[] { "SheetKraftWebV3", "test:go-procedure-codegen", "CodeKraft.AspNet.Tests" })
        {
            CommandMainText row = MainLine(120, name: name);

            Assert.Contains(name, row.Text);
            Assert.DoesNotContain("…", row.Text);
        }
    }

    [Fact]
    public void Names_longer_than_the_width_are_truncated_with_an_ellipsis()
    {
        string name = new string('x', 80);

        CommandMainText row = MainLine(40, name: name);

        Assert.Equal(40, TextWidth.Columns(row.Text));
        Assert.Contains("…", row.Text);
        Assert.DoesNotContain(new string('x', 80), row.Text);
    }

    [Fact]
    public void Marked_rows_show_the_chevron_mark_at_column_0()
    {
        CommandMainText marked = MainLine(60, marked: true);
        CommandMainText unmarked = MainLine(60, marked: false);

        Assert.Equal("→", marked.Text.Substring(RowLayout.MarkColumn, 1));
        Assert.Equal(" ", unmarked.Text.Substring(RowLayout.MarkColumn, 1));
        Assert.Equal(marked.GlyphColumn, unmarked.GlyphColumn);
    }

    [Fact]
    public void Wide_names_are_truncated_by_columns()
    {
        const string name = "日本語のコマンド名です";

        CommandMainText row = MainLine(20, name: name);

        Assert.Equal(20, TextWidth.Columns(row.Text));
        Assert.Equal("↻", row.Text.Substring(2, 1));
        Assert.Contains("…", row.Text);
    }

    [Fact]
    public void Narrow_widths_never_throw_and_never_exceed_the_width()
    {
        for (int width = 0; width <= 80; width++)
        {
            CommandMainText row = RowLayout.FormatCommandMain(width, marked: true, "✗", "some-command-name");

            Assert.Equal(width, TextWidth.Columns(row.Text));
            Assert.True(row.GlyphColumn < width, $"glyph column {row.GlyphColumn} outside width {width}");
            Assert.True(row.NameColumn < width, $"name column {row.NameColumn} outside width {width}");
        }
    }

    [Fact]
    public void Glyph_is_hidden_when_the_row_is_too_narrow_for_it()
    {
        Assert.Equal(-1, MainLine(2).GlyphColumn);
        Assert.Equal(2, MainLine(3).GlyphColumn);
        Assert.Equal(-1, MainLine(0).GlyphColumn);
        Assert.Equal(string.Empty, MainLine(0).Text);
    }

    [Fact]
    public void Name_is_hidden_when_the_width_does_not_pass_the_prefix()
    {
        CommandMainText row = MainLine(4);

        Assert.Equal(-1, row.NameColumn);
        Assert.Equal(4, TextWidth.Columns(row.Text));
        Assert.DoesNotContain("web", row.Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Command_glyph_and_name_columns_match_a_sibling_group_header(int depth)
    {
        // A command in a group at depth d is a sibling of the groups at depth d + 1, whose ▾ and name it lines up with.
        const string groupName = "grp";
        CommandMainText command = RowLayout.FormatCommandMain(40, marked: false, "↻", "web", depth);
        GroupHeaderText header = RowLayout.FormatGroupHeader(40, groupName, GroupMode.Parallel, false, false, string.Empty, depth + 1);

        int markerColumn = header.Text.IndexOf('▾');
        int nameColumn = header.Text.IndexOf(groupName, StringComparison.Ordinal);

        Assert.True(markerColumn >= 0 && nameColumn >= 0);
        Assert.Equal(markerColumn, command.GlyphColumn);
        Assert.Equal(nameColumn, command.NameColumn);
        Assert.Equal(RowLayout.CommandDetailIndent(depth), nameColumn);
    }

    [Theory]
    [InlineData(0, true, "→ ↻ web")]
    [InlineData(0, false, "  ↻ web")]
    [InlineData(1, true, "→   ↻ web")]
    [InlineData(1, false, "    ↻ web")]
    [InlineData(2, true, "→     ↻ web")]
    public void Mark_sits_at_column_0_and_the_glyph_and_name_keep_their_columns(int depth, bool marked, string expected)
    {
        CommandMainText row = RowLayout.FormatCommandMain(40, marked, "↻", "web", depth);

        Assert.StartsWith(expected, row.Text);
        Assert.Equal(marked ? "→" : " ", row.Text.Substring(RowLayout.MarkColumn, 1));
        Assert.Equal("↻", row.Text.Substring(2 * depth + 2, 1));
        Assert.Equal(2 * depth + 2, row.GlyphColumn);
        Assert.Equal(2 * depth + 4, row.NameColumn);
    }

    [Theory]
    [InlineData("↻")]
    [InlineData("⧗")]
    [InlineData("…")]
    [InlineData("·")]
    [InlineData("∎")]
    [InlineData("✓")]
    [InlineData("✗")]
    [InlineData("⚠")]
    [InlineData("→")]
    [InlineData("▾")]
    [InlineData("▸")]
    [InlineData("▶")]
    [InlineData("●")]
    [InlineData("◌")]
    public void Glyphs_are_one_column_wide(string glyph)
    {
        Assert.Equal(1, TextWidth.Columns(glyph));
    }

    [Fact]
    public void Detail_line_is_indented_to_the_name_column()
    {
        string text = RowLayout.FormatCommandDetail(40, RowLayout.NameColumn, Running("0:05"));

        Assert.Equal(40, TextWidth.Columns(text));
        Assert.Equal(new string(' ', RowLayout.NameColumn), text.Substring(0, RowLayout.NameColumn));
        Assert.Equal("12:00:00  0:05", text.Substring(RowLayout.NameColumn).TrimEnd());
    }

    [Fact]
    public void Detail_of_a_started_command_is_start_time_then_detail()
    {
        var status = new StatusText("✓", StatusKind.Success, "09:30:15", "exit 0 · 0:03");

        string text = RowLayout.FormatCommandDetail(60, RowLayout.NameColumn, status);

        Assert.Equal("    09:30:15  exit 0 · 0:03", text.TrimEnd());
    }

    [Fact]
    public void Detail_for_a_running_command_shows_start_time_and_elapsed()
    {
        var status = new StatusText("↻", StatusKind.Running, "10:00:00", "1:23");

        Assert.Equal("10:00:00  1:23", RowLayout.FormatCommandDetail(40, 6, status).TrimStart().TrimEnd());
    }

    [Fact]
    public void Detail_for_a_stopping_command_shows_the_stop_time()
    {
        var status = new StatusText("⧗", StatusKind.Stopping, "10:00:00", "stopping 0:02");

        Assert.Equal("10:00:00  stopping 0:02", RowLayout.FormatCommandDetail(60, 6, status).TrimStart().TrimEnd());
    }

    [Fact]
    public void Detail_for_a_failed_exit_shows_the_exit_code()
    {
        var status = new StatusText("✗", StatusKind.Failure, "10:00:00", "exit 1 · 0:01");

        Assert.Equal("10:00:00  exit 1 · 0:01", RowLayout.FormatCommandDetail(60, 6, status).TrimStart().TrimEnd());
    }

    [Fact]
    public void Detail_for_leftovers_shows_the_exit_code_and_the_leftover_count()
    {
        var status = new StatusText("⚠", StatusKind.Warning, "10:00:00", "exit 0 · 2 left");

        Assert.Equal("10:00:00  exit 0 · 2 left", RowLayout.FormatCommandDetail(60, 6, status).TrimStart().TrimEnd());
    }

    [Fact]
    public void Detail_for_a_failure_to_start_has_no_dash_for_the_missing_start_time()
    {
        var status = new StatusText("✗", StatusKind.Failure, "—", "failed to start");

        string text = RowLayout.FormatCommandDetail(60, 6, status);

        Assert.Equal("failed to start", text.TrimStart().TrimEnd());
        Assert.DoesNotContain("—", text);
    }

    [Fact]
    public void Detail_for_a_command_that_was_never_started_reads_not_started()
    {
        var status = new StatusText("·", StatusKind.Idle, "—", string.Empty);

        string text = RowLayout.FormatCommandDetail(60, 6, status);

        Assert.Equal("not started", text.TrimStart().TrimEnd());
        Assert.DoesNotContain("—", text);
    }

    [Fact]
    public void Detail_for_a_queued_command_reads_queued_without_a_dash()
    {
        var status = new StatusText("…", StatusKind.Queued, "—", "queued");

        string text = RowLayout.FormatCommandDetail(60, 6, status);

        Assert.Equal("queued", text.TrimStart().TrimEnd());
        Assert.DoesNotContain("—", text);
    }

    [Fact]
    public void Detail_text_comes_from_the_status_formatter_for_idle_and_queued_commands()
    {
        var now = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
        var notStarted = new ProcessSnapshot(ProcessState.NotStarted, null, null, null, null, null, null, 0);

        string idle = RowLayout.FormatCommandDetail(40, 6, StatusFormatter.Format(notStarted, queued: false, now));
        string queued = RowLayout.FormatCommandDetail(40, 6, StatusFormatter.Format(notStarted, queued: true, now));

        Assert.Equal("not started", idle.TrimStart().TrimEnd());
        Assert.Equal("queued", queued.TrimStart().TrimEnd());
    }

    [Fact]
    public void Detail_text_comes_from_the_status_formatter_for_a_running_command()
    {
        var now = new DateTimeOffset(2026, 11, 11, 10, 0, 5, TimeSpan.Zero);
        var running = new ProcessSnapshot(ProcessState.Running, 1, now.AddSeconds(-5), null, null, null, null, 0);
        StatusText status = StatusFormatter.Format(running, queued: false, now);

        string text = RowLayout.FormatCommandDetail(60, 6, status).TrimStart().TrimEnd();

        Assert.Equal(status.Started + "  0:05", text);
    }

    [Fact]
    public void Detail_is_exactly_the_requested_width_and_never_throws_when_narrow()
    {
        var status = new StatusText("✓", StatusKind.Success, "09:30:15", "exit 0 · 0:03");

        for (int width = 0; width <= 60; width++)
        {
            string text = RowLayout.FormatCommandDetail(width, RowLayout.NameColumn, status);

            Assert.Equal(width, TextWidth.Columns(text));
        }
    }

    [Fact]
    public void Detail_is_truncated_when_it_is_longer_than_the_width()
    {
        var status = new StatusText("↻", StatusKind.Running, "10:00:00", "123:45:00");

        string text = RowLayout.FormatCommandDetail(16, RowLayout.NameColumn, status);

        Assert.Equal(16, TextWidth.Columns(text));
        Assert.EndsWith("…", text.TrimEnd());
    }

    [Fact]
    public void Detail_with_no_room_after_the_indent_is_blank()
    {
        var status = new StatusText("↻", StatusKind.Running, "10:00:00", "0:05");

        Assert.Equal(new string(' ', 6), RowLayout.FormatCommandDetail(6, 6, status));
        Assert.Equal(string.Empty, RowLayout.FormatCommandDetail(0, 6, status));
    }

    [Fact]
    public void Group_row_shows_name_and_mode_in_brackets()
    {
        string text = RowLayout.FormatGroupRow(40, "build", GroupMode.Sequential, sequenceRunning: false, collapsed: false, summary: string.Empty);

        Assert.Equal(40, TextWidth.Columns(text));
        Assert.StartsWith("▾ build  [sequential]", text);
        Assert.DoesNotContain("sequence running", text);
    }

    [Fact]
    public void Group_row_appends_sequence_running_while_a_sequence_runs()
    {
        string text = RowLayout.FormatGroupRow(60, "build", GroupMode.Parallel, sequenceRunning: true, collapsed: false, summary: string.Empty);

        Assert.Equal(60, TextWidth.Columns(text));
        Assert.Contains("[parallel]", text);
        Assert.Contains("▶ sequence running", text);
    }

    [Fact]
    public void Collapsed_group_row_shows_the_collapsed_glyph_and_the_summary_after_the_mode()
    {
        string text = RowLayout.FormatGroupRow(60, "build", GroupMode.Sequential, sequenceRunning: false, collapsed: true, summary: "2 running · 1 queued");

        Assert.Equal(60, TextWidth.Columns(text));
        Assert.StartsWith("▸ build  [sequential]  2 running · 1 queued", text);
    }

    [Fact]
    public void Collapsed_group_row_puts_the_summary_before_the_sequence_marker()
    {
        string text = RowLayout.FormatGroupRow(80, "steps", GroupMode.Sequential, sequenceRunning: true, collapsed: true, summary: "1 running · 2 queued");

        Assert.Equal("▸ steps  [sequential]  1 running · 2 queued  ▶ sequence running", text.TrimEnd());
    }

    [Fact]
    public void Collapsed_group_row_without_a_summary_has_no_trailing_text()
    {
        string text = RowLayout.FormatGroupRow(40, "build", GroupMode.Parallel, sequenceRunning: false, collapsed: true, summary: string.Empty);

        Assert.Equal("▸ build  [parallel]", text.TrimEnd());
    }

    [Fact]
    public void Expanded_group_row_ignores_the_summary()
    {
        string text = RowLayout.FormatGroupRow(60, "build", GroupMode.Parallel, sequenceRunning: false, collapsed: false, summary: "2 running");

        Assert.StartsWith("▾ build  [parallel]", text);
        Assert.DoesNotContain("2 running", text);
    }

    [Fact]
    public void Group_row_is_truncated_to_the_width()
    {
        string text = RowLayout.FormatGroupRow(12, "a-very-long-group-name", GroupMode.Parallel, sequenceRunning: true, collapsed: false, summary: string.Empty);

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
