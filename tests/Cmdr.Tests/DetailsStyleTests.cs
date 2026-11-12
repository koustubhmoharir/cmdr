using Cmdr.Tui;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tests;

public sealed class DetailsStyleTests
{
    private static readonly Attribute Normal = new(new Color(200, 201, 202), new Color(10, 11, 12), TextStyle.None);

    [Fact]
    public void Named_styles_use_the_bright_16_colors()
    {
        var expected = new Dictionary<DetailsStyle, ColorName16>
        {
            [DetailsStyle.Command] = ColorName16.BrightGreen,
            [DetailsStyle.Path] = ColorName16.BrightCyan,
            [DetailsStyle.EnvName] = ColorName16.BrightMagenta,
            [DetailsStyle.Unset] = ColorName16.BrightRed,
        };

        foreach ((DetailsStyle style, ColorName16 color) in expected)
        {
            Attribute attr = DetailsView.AttributeFor(style, Normal);

            Assert.Equal((Color)color, attr.Foreground);
            Assert.Equal(TextStyle.None, attr.Style);
        }
    }

    [Fact]
    public void Uncolored_styles_keep_the_normal_foreground_and_add_only_the_text_style()
    {
        var expected = new Dictionary<DetailsStyle, TextStyle>
        {
            [DetailsStyle.Normal] = TextStyle.None,
            [DetailsStyle.EnvValue] = TextStyle.None,
            [DetailsStyle.Prompt] = TextStyle.Faint,
            [DetailsStyle.Label] = TextStyle.Faint,
            [DetailsStyle.Faint] = TextStyle.Faint,
        };

        foreach ((DetailsStyle style, TextStyle textStyle) in expected)
        {
            Attribute attr = DetailsView.AttributeFor(style, Normal);

            Assert.Equal(Normal.Foreground, attr.Foreground);
            Assert.Equal(textStyle, attr.Style);
        }
    }

    [Fact]
    public void Every_style_is_drawn_on_the_normal_background()
    {
        foreach (DetailsStyle style in Enum.GetValues<DetailsStyle>())
        {
            Assert.Equal(Normal.Background, DetailsView.AttributeFor(style, Normal).Background);
        }
    }

    [Fact]
    public void Only_the_named_color_styles_change_the_foreground_color()
    {
        var colored = Enum.GetValues<DetailsStyle>()
            .Where(style => !DetailsView.AttributeFor(style, Normal).Foreground.Equals(Normal.Foreground))
            .ToList();

        Assert.Equal(
            new[] { DetailsStyle.Command, DetailsStyle.Path, DetailsStyle.EnvName, DetailsStyle.Unset },
            colored);
    }
}
