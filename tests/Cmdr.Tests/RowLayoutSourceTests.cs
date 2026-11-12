using Cmdr.Core.Config;
using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The source label of a top-level base group, drawn after the group header and dropped first when narrow.</summary>
public sealed class RowLayoutSourceTests
{
    private const string Label = "shared/base.json";

    private static GroupHeaderText Header(int width, string? label = Label, int depth = 0, bool collapsed = false, string summary = "") =>
        RowLayout.FormatGroupHeader(width, "shared", GroupMode.Parallel, sequenceRunning: false, collapsed, summary, depth, label);

    [Fact]
    public void Label_follows_the_header_after_two_spaces()
    {
        GroupHeaderText header = Header(60);

        Assert.Equal("⊟ shared  [parallel]  " + Label, header.Text.TrimEnd());
        Assert.Equal(Label, header.SourceText);
        Assert.Equal(TextWidth.Columns("⊟ shared  [parallel]  "), header.SourceColumn);
        Assert.Equal(Label, header.Text.Substring(header.SourceColumn, Label.Length));
    }

    [Fact]
    public void Header_is_exactly_the_requested_width()
    {
        GroupHeaderText header = Header(60);

        Assert.Equal(60, TextWidth.Columns(header.Text));
    }

    [Fact]
    public void Label_is_dropped_before_the_header_is_truncated()
    {
        // "⊟ shared  [parallel]" is 20 columns; with the label it needs 20 + 2 + 16 = 38.
        GroupHeaderText header = Header(37);

        Assert.Equal(-1, header.SourceColumn);
        Assert.Equal(string.Empty, header.SourceText);
        Assert.Equal(RowLayout.FormatGroupRow(37, "shared", GroupMode.Parallel, sequenceRunning: false, collapsed: false, summary: string.Empty), header.Text);
        Assert.Equal("⊟ shared  [parallel]", header.Text.TrimEnd());
    }

    [Fact]
    public void Label_is_shown_at_the_exact_width_it_needs()
    {
        GroupHeaderText header = Header(38);

        Assert.Equal(Label, header.SourceText);
        Assert.Equal(38, TextWidth.Columns(header.Text));
    }

    [Fact]
    public void Without_a_label_the_header_is_unchanged()
    {
        string plain = RowLayout.FormatGroupRow(60, "shared", GroupMode.Parallel, sequenceRunning: false, collapsed: false, summary: string.Empty);
        GroupHeaderText header = Header(60, label: null);

        Assert.Equal(plain, header.Text);
        Assert.Equal(-1, header.SourceColumn);
    }

    [Fact]
    public void Empty_label_is_no_label()
    {
        GroupHeaderText header = Header(60, label: string.Empty);

        Assert.Equal(-1, header.SourceColumn);
        Assert.Equal(string.Empty, header.SourceText);
    }

    [Fact]
    public void Label_column_moves_with_the_depth_indent()
    {
        GroupHeaderText header = Header(70, depth: 2);

        Assert.Equal(4 + TextWidth.Columns("⊟ shared  [parallel]  "), header.SourceColumn);
        Assert.Equal(Label, header.Text.Substring(header.SourceColumn, Label.Length));
    }

    [Fact]
    public void Label_comes_after_the_collapsed_summary_and_the_sequence_marker()
    {
        GroupHeaderText header = RowLayout.FormatGroupHeader(
            80, "shared", GroupMode.Sequential, sequenceRunning: true, collapsed: true, summary: "1 running", sourceLabel: Label);

        Assert.Equal(Label, header.SourceText);
        Assert.True(header.Text.IndexOf("▶ sequence running", StringComparison.Ordinal) < header.SourceColumn);
        Assert.Equal(80, TextWidth.Columns(header.Text));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void Narrow_widths_never_throw_and_never_exceed_the_width(int width)
    {
        GroupHeaderText header = Header(width);

        Assert.Equal(width, TextWidth.Columns(header.Text));
        Assert.True(header.SourceColumn < width);
    }
}
