using Cmdr.Core.Output;
using Cmdr.Tui;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tests;

public sealed class ColorMapTests
{
    private static readonly Attribute Normal = new(new Color(200, 201, 202), new Color(10, 11, 12), TextStyle.None);

    private static SpanStyle Fg(AnsiColor fg) => new(fg, AnsiColor.Default, TextAttrs.None);

    private static Attribute Map(SpanStyle style, OutputSource source = OutputSource.StdOut) =>
        ColorMap.ToAttribute(style, source, Normal);

    [Theory]
    [InlineData(0, ColorName16.Black)]
    [InlineData(1, ColorName16.Red)]
    [InlineData(2, ColorName16.Green)]
    [InlineData(3, ColorName16.Yellow)]
    [InlineData(4, ColorName16.Blue)]
    [InlineData(5, ColorName16.Magenta)]
    [InlineData(6, ColorName16.Cyan)]
    [InlineData(7, ColorName16.Gray)]
    [InlineData(8, ColorName16.DarkGray)]
    [InlineData(9, ColorName16.BrightRed)]
    [InlineData(10, ColorName16.BrightGreen)]
    [InlineData(11, ColorName16.BrightYellow)]
    [InlineData(12, ColorName16.BrightBlue)]
    [InlineData(13, ColorName16.BrightMagenta)]
    [InlineData(14, ColorName16.BrightCyan)]
    [InlineData(15, ColorName16.White)]
    public void Indexed_0_to_15_maps_to_the_named_16_colors(byte index, ColorName16 expected)
    {
        Attribute attr = Map(Fg(AnsiColor.FromIndex(index)));

        Assert.Equal((Color)expected, attr.Foreground);
    }

    [Fact]
    public void Bright_colors_differ_from_their_normal_counterparts()
    {
        Assert.NotEqual(Map(Fg(AnsiColor.FromIndex(1))).Foreground, Map(Fg(AnsiColor.FromIndex(9))).Foreground);
        Assert.NotEqual(Map(Fg(AnsiColor.FromIndex(4))).Foreground, Map(Fg(AnsiColor.FromIndex(12))).Foreground);
    }

    [Theory]
    [InlineData(16, 0, 0, 0)]
    [InlineData(21, 0, 0, 255)]
    [InlineData(46, 0, 255, 0)]
    [InlineData(196, 255, 0, 0)]
    [InlineData(231, 255, 255, 255)]
    [InlineData(59, 95, 95, 95)]
    public void Cube_colors_use_the_xterm_levels(byte index, byte r, byte g, byte b)
    {
        Attribute attr = Map(Fg(AnsiColor.FromIndex(index)));

        Assert.Equal(new Color(r, g, b), attr.Foreground);
    }

    [Theory]
    [InlineData(232, 8)]
    [InlineData(233, 18)]
    [InlineData(255, 238)]
    public void Gray_ramp_follows_8_plus_10_steps(byte index, byte level)
    {
        Attribute attr = Map(Fg(AnsiColor.FromIndex(index)));

        Assert.Equal(new Color(level, level, level), attr.Foreground);
    }

    [Fact]
    public void Rgb_colors_pass_through()
    {
        Attribute attr = Map(Fg(AnsiColor.FromRgb(0x12, 0x34, 0x56)));

        Assert.Equal(new Color(0x12, 0x34, 0x56), attr.Foreground);
    }

    [Fact]
    public void Default_colors_fall_back_to_the_normal_attribute()
    {
        Attribute attr = Map(SpanStyle.Default);

        Assert.Equal(Normal.Foreground, attr.Foreground);
        Assert.Equal(Normal.Background, attr.Background);
        Assert.Equal(TextStyle.None, attr.Style);
    }

    [Fact]
    public void Inverse_uses_terminal_reverse_attribute()
    {
        Attribute defaultInverse = Map(new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Inverse));
        Assert.Equal(Normal.Foreground, defaultInverse.Foreground);
        Assert.Equal(Normal.Background, defaultInverse.Background);
        Assert.Equal(TextStyle.Reverse, defaultInverse.Style);

        Attribute explicitInverse = Map(new SpanStyle(AnsiColor.FromIndex(1), AnsiColor.Default, TextAttrs.Inverse));
        Assert.Equal((Color)ColorName16.Red, explicitInverse.Foreground);
        Assert.Equal(Normal.Background, explicitInverse.Background);
        Assert.Equal(TextStyle.Reverse, explicitInverse.Style);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Inverse_is_preserved_when_normal_colors_are_terminal_defaults(bool explicitForeground, bool explicitBackground)
    {
        var normal = new Attribute(explicitForeground ? new Color(1, 2, 3) : Color.None,
            explicitBackground ? new Color(4, 5, 6) : Color.None, TextStyle.None);

        Attribute attr = ColorMap.ToAttribute(new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Inverse), OutputSource.StdOut, normal);

        Assert.Equal(normal.Foreground, attr.Foreground);
        Assert.Equal(normal.Background, attr.Background);
        Assert.Equal(TextStyle.Reverse, attr.Style);
    }

    [Fact]
    public void Selection_inversion_toggles_existing_inverse_video()
    {
        Attribute normal = new(Color.None, Color.None, TextStyle.None);
        Attribute inverse = ColorMap.ToAttribute(
            new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Inverse), OutputSource.StdOut, normal);

        Assert.Equal(TextStyle.None, OutputView.Invert(inverse).Style);
        Assert.Equal(TextStyle.Reverse, OutputView.Invert(normal).Style);
    }

    [Fact]
    public void Stderr_with_default_foreground_is_soft_red()
    {
        Attribute attr = Map(SpanStyle.Default, OutputSource.StdErr);

        Assert.Equal(new Color(0xE0, 0x6C, 0x75), attr.Foreground);
        Assert.Equal(new Color(0xE0, 0x6C, 0x75), ColorMap.StdErrDefault);
    }

    [Fact]
    public void Stderr_with_explicit_foreground_keeps_it()
    {
        Attribute attr = Map(Fg(AnsiColor.FromIndex(2)), OutputSource.StdErr);

        Assert.Equal((Color)ColorName16.Green, attr.Foreground);
    }

    [Fact]
    public void System_lines_with_default_foreground_are_cyan_and_faint()
    {
        Attribute attr = Map(SpanStyle.Default, OutputSource.System);

        Assert.Equal((Color)ColorName16.Cyan, attr.Foreground);
        Assert.Equal(TextStyle.Faint, attr.Style);
    }

    [Fact]
    public void System_lines_with_explicit_foreground_are_not_tinted()
    {
        Attribute attr = Map(Fg(AnsiColor.FromIndex(5)), OutputSource.System);

        Assert.Equal((Color)ColorName16.Magenta, attr.Foreground);
        Assert.Equal(TextStyle.None, attr.Style);
    }

    [Fact]
    public void Attribute_flags_map_to_text_styles()
    {
        TextAttrs all = TextAttrs.Bold | TextAttrs.Dim | TextAttrs.Italic | TextAttrs.Underline | TextAttrs.Blink | TextAttrs.Strikethrough;

        Attribute attr = Map(new SpanStyle(AnsiColor.Default, AnsiColor.Default, all));

        Assert.Equal(TextStyle.Bold | TextStyle.Faint | TextStyle.Italic | TextStyle.Underline | TextStyle.Blink | TextStyle.Strikethrough, attr.Style);
    }

    [Fact]
    public void Each_flag_maps_alone()
    {
        Assert.Equal(TextStyle.Bold, Map(new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Bold)).Style);
        Assert.Equal(TextStyle.Faint, Map(new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Dim)).Style);
        Assert.Equal(TextStyle.Italic, Map(new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Italic)).Style);
        Assert.Equal(TextStyle.Underline, Map(new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Underline)).Style);
        Assert.Equal(TextStyle.Blink, Map(new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Blink)).Style);
        Assert.Equal(TextStyle.Strikethrough, Map(new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Strikethrough)).Style);
    }

    [Fact]
    public void Background_indexed_color_is_applied()
    {
        Attribute attr = Map(new SpanStyle(AnsiColor.Default, AnsiColor.FromIndex(4), TextAttrs.None));

        Assert.Equal(Normal.Foreground, attr.Foreground);
        Assert.Equal((Color)ColorName16.Blue, attr.Background);
    }

    [Fact]
    public void Background_rgb_color_is_applied()
    {
        Attribute attr = Map(new SpanStyle(AnsiColor.Default, AnsiColor.FromRgb(1, 2, 3), TextAttrs.None));

        Assert.Equal(Normal.Foreground, attr.Foreground);
        Assert.Equal(new Color(1, 2, 3), attr.Background);
    }
}
