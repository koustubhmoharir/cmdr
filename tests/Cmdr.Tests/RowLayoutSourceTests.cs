using Cmdr.Tui;
using Shared.Tui;

namespace Cmdr.Tests;

/// <summary>
/// The file row of a configuration file: its display path after the ▾ or ▸, and the faint " (base)" marker after the path of a base
/// file. The marker is kept when the path is cut, and dropped only when the line is too narrow for it.
/// </summary>
public sealed class RowLayoutSourceTests
{
    private const string BasePath = "shared/base.json";
    private const string Marker = " (base)";

    [Fact]
    public void Base_file_row_is_the_glyph_and_path_followed_by_the_marker()
    {
        FileHeaderText row = RowLayout.FormatFileRow(40, BasePath, collapsed: false, isBase: true);

        Assert.Equal("▾ " + BasePath + Marker, row.Text.TrimEnd());
        Assert.Equal(Marker, row.BaseText);
        Assert.Equal(TextWidth.Columns("▾ " + BasePath), row.BaseColumn);
        Assert.Equal(Marker, row.Text.Substring(row.BaseColumn, Marker.Length));
    }

    [Fact]
    public void Main_file_row_has_no_marker()
    {
        FileHeaderText row = RowLayout.FormatFileRow(40, "cmdr.json", collapsed: false, isBase: false);

        Assert.Equal("▾ cmdr.json", row.Text.TrimEnd());
        Assert.Equal(-1, row.BaseColumn);
        Assert.Equal(string.Empty, row.BaseText);
    }

    [Fact]
    public void Collapsed_file_row_shows_the_collapsed_glyph_and_the_marker()
    {
        FileHeaderText row = RowLayout.FormatFileRow(40, BasePath, collapsed: true, isBase: true);

        Assert.Equal("▸ " + BasePath + Marker, row.Text.TrimEnd());
        Assert.Equal(Marker, row.BaseText);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(60)]
    public void File_row_is_exactly_the_requested_width_and_never_throws(int width)
    {
        FileHeaderText row = RowLayout.FormatFileRow(width, BasePath, collapsed: false, isBase: true);

        Assert.Equal(width, TextWidth.Columns(row.Text));
        Assert.True(row.BaseColumn < width);
    }

    [Fact]
    public void The_marker_is_kept_and_the_path_is_cut_before_it_when_the_line_is_narrow()
    {
        FileHeaderText row = RowLayout.FormatFileRow(20, "shared/a-very-long-name.json", collapsed: false, isBase: true);

        Assert.Equal(20, TextWidth.Columns(row.Text));
        Assert.Equal(20 - Marker.Length, row.BaseColumn);
        Assert.EndsWith(Marker, row.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_marker_is_dropped_when_the_line_is_no_wider_than_it()
    {
        FileHeaderText row = RowLayout.FormatFileRow(Marker.Length, BasePath, collapsed: false, isBase: true);

        Assert.Equal(-1, row.BaseColumn);
        Assert.Equal(string.Empty, row.BaseText);
        Assert.Equal(Marker.Length, TextWidth.Columns(row.Text));
    }
}
