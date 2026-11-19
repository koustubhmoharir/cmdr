using System.Reflection;
using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Logvu.Tui;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tests;

/// <summary>
/// The alternate-row stripes as drawn: the odd event rows by viewport position, the whole row striped, and the cells that keep their own
/// backgrounds. Includes the redraw when the terminal reports its default colors.
/// </summary>
[Collection("Terminal GUI")]
public sealed class TimelineStripeTests
{
    private const int Width = 40;
    private const int Height = 6;

    private static readonly Color BrightYellow = new(ColorName16.BrightYellow);

    [Fact]
    public void Every_other_event_row_is_striped_by_screen_row_and_the_header_is_not()
    {
        TimelineView view = Timeline(5);

        TerminalScreen screen = Draw(view)[0];

        Color stripe = RowStripe.For(screen.Normal, null);
        Assert.NotEqual(screen.Normal.Background, stripe);
        Assert.All(screen.Cells[0], cell => Assert.Equal(screen.Normal.Background, cell.Attribute.Background));
        // Event rows are numbered from 0, so screen rows 2 and 4 are the striped ones.
        for (int row = 1; row <= 5; row++)
        {
            Color expected = row is 2 or 4 ? stripe : screen.Normal.Background;
            Assert.All(screen.Cells[row], cell => Assert.Equal(expected, cell.Attribute.Background));
        }
    }

    [Fact]
    public void A_striped_row_is_drawn_in_full_including_the_gutter_and_the_padding()
    {
        TimelineView view = Timeline(3);

        TerminalScreen screen = Draw(view)[0];

        Color stripe = RowStripe.For(screen.Normal, null);
        Assert.Equal(" ", screen.Cells[2][0].Grapheme);
        Assert.Equal(stripe, screen.Cells[2][0].Attribute.Background);
        Assert.Equal(stripe, screen.Cells[2][Width - 1].Attribute.Background);
        Assert.Equal(screen.Normal.Foreground, screen.Cells[2][Width - 1].Attribute.Foreground);
    }

    [Fact]
    public void A_search_match_on_a_striped_row_keeps_the_match_background()
    {
        TimelineView view = Timeline(3, search: "needle", preview: index => index == 1 ? "needle here" : $"r{index}");

        TerminalScreen screen = Draw(view)[0];

        Color stripe = RowStripe.For(screen.Normal, null);
        Assert.Equal(BrightYellow, screen.At("needle").Background);
        Assert.Equal(stripe, screen.At("here").Background);
        Assert.Equal(stripe, screen.Cells[2][Width - 1].Attribute.Background);
    }

    [Fact]
    public void A_fatal_label_on_a_striped_row_keeps_its_background()
    {
        TimelineView view = Timeline(3, level: index => index == 1 ? LogLevel.Fatal : LogLevel.Unknown);

        TerminalScreen screen = Draw(view)[0];

        Color stripe = RowStripe.For(screen.Normal, null);
        Assert.Equal(LevelColors.FatalBackground, screen.At("FATAL").Background);
        Assert.Equal(stripe, screen.Cells[2][Width - 1].Attribute.Background);
    }

    [Fact]
    public void A_reverse_video_selection_on_a_striped_row_keeps_its_look()
    {
        TimelineView view = Timeline(5);

        // Select the row at screen row 4 (striped), then drag across the start of its time.
        List<TerminalScreen> screens = TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleKey(Key.CursorUp),
            () => view.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(3, 4) }),
            () => view.HandleMouse(new Mouse { Flags = MouseFlags.PositionReport, Position = new(9, 4) }));
        TerminalScreen drawn = screens[^1];

        Color stripe = RowStripe.For(drawn.Normal, null);
        Attribute selected = new(drawn.Normal.Foreground, drawn.Normal.Background, TextStyle.Reverse);
        List<int> reversed = Enumerable.Range(0, Width).Where(column => drawn.Cells[4][column].Attribute.Style.HasFlag(TextStyle.Reverse)).ToList();
        Assert.NotEmpty(reversed);
        Assert.All(reversed, column => Assert.Equal(selected, drawn.Cells[4][column].Attribute));
        Assert.Equal(stripe, drawn.Cells[4][Width - 1].Attribute.Background);
    }

    [Fact]
    public void The_stripe_switches_to_the_terminal_shade_when_the_default_colors_are_reported()
    {
        TimelineView view = Timeline(3);

        // A dark terminal: the reply's background is black and its foreground is #AAAAAA, so the shade is #191919.
        var reply = new Attribute(new Color(0xAA, 0xAA, 0xAA), new Color(0x00, 0x00, 0x00));
        List<TerminalScreen> screens = TerminalHarness.Run(view, Width, Height, () => ReportDefaults(view.App!.Driver!, reply));

        Color before = RowStripe.For(screens[0].Normal, null);
        Assert.Equal(before, screens[0].Cells[2][1].Attribute.Background);
        Assert.Equal(new Color(0x19, 0x19, 0x19), screens[1].Cells[2][1].Attribute.Background);
        Assert.Equal(new Color(0x19, 0x19, 0x19), screens[1].Cells[2][Width - 1].Attribute.Background);
    }

    private static void ReportDefaults(IDriver driver, Attribute attribute) =>
        driver.GetType().GetMethod("SetDefaultAttribute", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(driver, [attribute]);

    /// <summary>
    /// A timeline of <paramref name="count"/> rows, the newest selected, in a view of <see cref="Width"/> by <see cref="Height"/>. The rows
    /// are named by their index unless <paramref name="preview"/> names them, and their levels are <paramref name="level"/>'s.
    /// </summary>
    private static TimelineView Timeline(int count, string? search = null, Func<int, string>? preview = null, Func<int, LogLevel>? level = null)
    {
        var source = new FakeTimelineSource(Enumerable.Range(0, count).Select(index => Row(index, preview?.Invoke(index) ?? $"r{index}", level?.Invoke(index) ?? LogLevel.Unknown)));
        var model = new TimelineModel(source, action => action(), FakeTimelineSource.CompareKeys);
        model.Start(count);
        return new TimelineView(model, TimeZoneInfo.Utc) { Search = search is null ? null : Pattern(search) };
    }

    private static List<TerminalScreen> Draw(TimelineView view) => TerminalHarness.Run(view, Width, Height);

    private static SearchPattern Pattern(string text) =>
        SearchPattern.Create(new SearchQuery(text, CaseSensitive: false, WholeWord: false, Regex: false), out _)!;

    private static EventRow Row(int index, string preview, LogLevel level) => new(
        new RowKey(1, index * 100L),
        new EventExtent(0, 0, 0, Provisional: false, Orphan: false),
        new DateTimeOffset(2026, 11, 18, 10, 0, index, TimeSpan.Zero),
        null,
        level,
        null,
        preview,
        IsRaw: false,
        Orphan: false,
        Provisional: false);
}
