using Logvu.Tui;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tests;

/// <summary>The alternate-row stripe's colour: the order of the colours it is blended from, and the blend itself.</summary>
public sealed class RowStripeTests
{
    private static readonly Attribute DefaultNormal = new(Color.None, Color.None);

    [Fact]
    public void A_dark_terminal_default_is_blended_six_percent_toward_the_foreground()
    {
        // 0 + (0xAA - 0) * 15 / 100 = 25 = 0x19 on each channel.
        var defaults = new Attribute(new Color(0xAA, 0xAA, 0xAA), new Color(0x00, 0x00, 0x00));

        Color stripe = RowStripe.For(DefaultNormal, defaults);

        Assert.Equal(new Color(0x19, 0x19, 0x19), stripe);
    }

    [Fact]
    public void A_light_terminal_default_is_blended_toward_the_dark_foreground_so_the_stripe_is_a_little_darker()
    {
        // 0xFF + (0 - 0xFF) * 15 / 100 = 255 - 38 = 217 = 0xD9 on each channel.
        var defaults = new Attribute(new Color(0x00, 0x00, 0x00), new Color(0xFF, 0xFF, 0xFF));

        Color stripe = RowStripe.For(DefaultNormal, defaults);

        Assert.Equal(new Color(0xD9, 0xD9, 0xD9), stripe);
    }

    [Fact]
    public void Unknown_defaults_and_a_default_coloured_normal_give_the_fallback_shade()
    {
        Assert.Equal(new Color(0x26, 0x2A, 0x33), RowStripe.For(DefaultNormal, null));
    }

    [Fact]
    public void Explicit_normal_colours_are_used_when_the_defaults_are_unknown()
    {
        // 0x20 + (0xFF - 0x20) * 15 / 100 = 32 + 33 = 65 = 0x41 on each channel.
        var normal = new Attribute(new Color(0xFF, 0xFF, 0xFF), new Color(0x20, 0x20, 0x20));

        Assert.Equal(new Color(0x41, 0x41, 0x41), RowStripe.For(normal, null));
    }

    [Fact]
    public void The_terminal_defaults_are_preferred_to_explicit_normal_colours()
    {
        var normal = new Attribute(new Color(0xFF, 0xFF, 0xFF), new Color(0x20, 0x20, 0x20));
        var defaults = new Attribute(new Color(0xAA, 0xAA, 0xAA), new Color(0x00, 0x00, 0x00));

        Assert.Equal(new Color(0x19, 0x19, 0x19), RowStripe.For(normal, defaults));
    }

    [Fact]
    public void Defaults_with_a_default_coloured_foreground_are_not_used()
    {
        var normal = new Attribute(new Color(0xFF, 0xFF, 0xFF), new Color(0x20, 0x20, 0x20));
        var defaults = new Attribute(Color.None, new Color(0x00, 0x00, 0x00));

        Assert.Equal(new Color(0x41, 0x41, 0x41), RowStripe.For(normal, defaults));
    }

    [Fact]
    public void Apply_replaces_a_background_that_is_the_normal_one_and_keeps_the_rest_of_the_attribute()
    {
        var normalBackground = new Color(0x20, 0x20, 0x20);
        var stripe = new Color(0x2D, 0x2D, 0x2D);
        var attr = new Attribute(new Color(0xEE, 0xEE, 0xEE), normalBackground, TextStyle.Bold);

        Attribute applied = RowStripe.Apply(attr, normalBackground, stripe);

        Assert.Equal(new Attribute(new Color(0xEE, 0xEE, 0xEE), stripe, TextStyle.Bold), applied);
    }

    [Fact]
    public void Apply_keeps_a_background_of_its_own()
    {
        var attr = new Attribute(new Color(0x00, 0x00, 0x00), new Color(0xAF, 0x3A, 0x3A));

        Assert.Equal(attr, RowStripe.Apply(attr, new Color(0x20, 0x20, 0x20), new Color(0x2D, 0x2D, 0x2D)));
    }
}
