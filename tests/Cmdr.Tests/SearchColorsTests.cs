using Cmdr.Tui;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tests;

public sealed class SearchColorsTests
{
    private static readonly Attribute Normal = new(new Color(200, 201, 202), new Color(10, 11, 12), TextStyle.None);

    [Fact]
    public void Match_is_black_on_bright_yellow()
    {
        Attribute attr = SearchColors.Match(Normal);

        Assert.Equal(new Color(ColorName16.Black), attr.Foreground);
        Assert.Equal(new Color(ColorName16.BrightYellow), attr.Background);
    }

    [Fact]
    public void Active_is_black_on_bright_cyan()
    {
        Attribute attr = SearchColors.Active(Normal);

        Assert.Equal(new Color(ColorName16.Black), attr.Foreground);
        Assert.Equal(new Color(ColorName16.BrightCyan), attr.Background);
    }

    [Fact]
    public void Bold_is_kept_in_both_highlights()
    {
        Attribute bold = new(Normal.Foreground, Normal.Background, TextStyle.Bold);

        Assert.True(SearchColors.Match(bold).Style.HasFlag(TextStyle.Bold));
        Assert.True(SearchColors.Active(bold).Style.HasFlag(TextStyle.Bold));
    }

    [Fact]
    public void Reverse_is_removed_from_both_highlights()
    {
        Attribute reversed = new(Normal.Foreground, Normal.Background, TextStyle.Bold | TextStyle.Reverse);

        Assert.False(SearchColors.Match(reversed).Style.HasFlag(TextStyle.Reverse));
        Assert.False(SearchColors.Active(reversed).Style.HasFlag(TextStyle.Reverse));
        Assert.True(SearchColors.Match(reversed).Style.HasFlag(TextStyle.Bold));
    }
}
