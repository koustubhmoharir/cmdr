using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.Drawing;

namespace Cmdr.Tests;

public sealed class StatusColorsTests
{
    private static readonly Color White = new(0xFF, 0xFF, 0xFF);
    private static readonly Color Black = new(0x00, 0x00, 0x00);
    private static readonly Color NormalForeground = new(0x12, 0x34, 0x56);

    private static readonly Color BrightWarning = new(0xE5, 0xA5, 0x0A);

    [Fact]
    public void Contrast_of_black_and_white_is_21()
    {
        Assert.Equal(21.0, StatusColors.ContrastRatio(White, Black), 2);
    }

    [Fact]
    public void Contrast_of_a_color_with_itself_is_1()
    {
        Assert.Equal(1.0, StatusColors.ContrastRatio(NormalForeground, NormalForeground), 6);
    }

    [Fact]
    public void Contrast_is_symmetric()
    {
        Color other = new(0x10, 0x7C, 0x10);
        Assert.Equal(
            StatusColors.ContrastRatio(other, White),
            StatusColors.ContrastRatio(White, other),
            6);
    }

    [Theory]
    [MemberData(nameof(BrightVariants))]
    public void Coloured_status_uses_bright_variant_on_black(StatusKind kind, Color bright)
    {
        Assert.Equal(bright, StatusColors.For(kind, Black, NormalForeground));
    }

    [Theory]
    [MemberData(nameof(DarkVariants))]
    public void Coloured_status_uses_dark_variant_on_white(StatusKind kind, Color dark)
    {
        Assert.Equal(dark, StatusColors.For(kind, White, NormalForeground));
    }

    [Theory]
    [MemberData(nameof(DarkVariants))]
    public void Dark_variant_has_enough_contrast_on_white(StatusKind kind, Color dark)
    {
        Assert.True(
            StatusColors.ContrastRatio(dark, White) >= 4.5,
            $"{kind} dark variant has contrast {StatusColors.ContrastRatio(dark, White):0.00} on white.");
    }

    [Theory]
    [MemberData(nameof(BrightVariants))]
    public void Default_background_uses_bright_variant(StatusKind kind, Color bright)
    {
        Assert.Equal(bright, StatusColors.For(kind, Color.None, NormalForeground));
        Assert.Equal(bright, StatusColors.For(kind, default, NormalForeground));
    }

    [Fact]
    public void Running_and_starting_are_yellow_and_stopping_is_red_like_failure()
    {
        Assert.Equal(
            StatusColors.For(StatusKind.Failure, Black, NormalForeground),
            StatusColors.For(StatusKind.Stopping, Black, NormalForeground));
        Assert.Equal(
            StatusColors.For(StatusKind.Stopping, White, NormalForeground),
            StatusColors.For(StatusKind.Failure, White, NormalForeground));
        Assert.Equal(
            StatusColors.For(StatusKind.Running, White, NormalForeground),
            StatusColors.For(StatusKind.Starting, White, NormalForeground));
        Assert.NotEqual(
            StatusColors.For(StatusKind.Running, White, NormalForeground),
            StatusColors.For(StatusKind.Ready, White, NormalForeground));
    }

    [Theory]
    [InlineData(StatusKind.Idle)]
    [InlineData(StatusKind.Stopped)]
    public void Kind_without_color_keeps_normal_foreground(StatusKind kind)
    {
        Assert.Equal(NormalForeground, StatusColors.For(kind, White, NormalForeground));
        Assert.Equal(NormalForeground, StatusColors.For(kind, Black, NormalForeground));
        Assert.Equal(NormalForeground, StatusColors.For(kind, Color.None, NormalForeground));
    }

    /// <summary>The bright variant of each coloured kind.</summary>
    public static TheoryData<StatusKind, Color> BrightVariants() => new()
    {
        { StatusKind.Running, new Color(ColorName16.BrightYellow) },
        { StatusKind.Starting, new Color(ColorName16.BrightYellow) },
        { StatusKind.Ready, new Color(ColorName16.BrightGreen) },
        { StatusKind.Success, new Color(ColorName16.BrightGreen) },
        { StatusKind.Stopping, new Color(ColorName16.BrightRed) },
        { StatusKind.Failure, new Color(ColorName16.BrightRed) },
        { StatusKind.Warning, BrightWarning },
        { StatusKind.Queued, new Color(ColorName16.BrightCyan) },
    };

    /// <summary>The dark variant of each coloured kind.</summary>
    public static TheoryData<StatusKind, Color> DarkVariants() => new()
    {
        { StatusKind.Running, new Color(0x6B, 0x60, 0x00) },
        { StatusKind.Starting, new Color(0x6B, 0x60, 0x00) },
        { StatusKind.Ready, new Color(0x10, 0x7C, 0x10) },
        { StatusKind.Success, new Color(0x10, 0x7C, 0x10) },
        { StatusKind.Stopping, new Color(0xC5, 0x0F, 0x1F) },
        { StatusKind.Failure, new Color(0xC5, 0x0F, 0x1F) },
        { StatusKind.Warning, new Color(0x8A, 0x5A, 0x00) },
        { StatusKind.Queued, new Color(0x0F, 0x6B, 0x73) },
    };
}
