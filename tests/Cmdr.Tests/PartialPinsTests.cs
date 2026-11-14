using Cmdr.Core.Output;
using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class PartialPinsTests
{
    private static OutputLine Line(OutputSource source, string text) =>
        new(source, new[] { new StyledSpan(text, SpanStyle.Default) });

    /// <summary>A lookup over the given absolute numbers; other numbers are not retained.</summary>
    private static Func<long, OutputLine?> Lines(params (long Number, OutputLine Line)[] lines)
    {
        var map = lines.ToDictionary(l => l.Number, l => l.Line);
        return number => map.TryGetValue(number, out OutputLine? line) ? line : null;
    }

    [Fact]
    public void Pin_is_found_in_place_when_the_line_is_unchanged()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT");

        long? found = PartialPins.Find(pin, 5, 0, 5, Lines((5, Line(OutputSource.StdOut, "OUT"))));

        Assert.Equal(5, found);
    }

    [Fact]
    public void Growing_partial_keeps_matching_by_its_prefix()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT");

        long? found = PartialPins.Find(pin, 5, 0, 5, Lines((5, Line(OutputSource.StdOut, "OUT-PARTIAL"))));

        Assert.Equal(5, found);
    }

    [Fact]
    public void Pin_pushed_down_by_one_line_is_found_at_the_next_number()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT");

        long? found = PartialPins.Find(pin, 5, 0, 6, Lines(
            (5, Line(OutputSource.System, "completed")),
            (6, Line(OutputSource.StdOut, "OUT"))));

        Assert.Equal(6, found);
    }

    [Fact]
    public void Pin_pushed_down_by_two_lines_is_found_at_the_later_number()
    {
        var pin = new PartialPin(OutputSource.StdErr, "ERR");

        long? found = PartialPins.Find(pin, 5, 0, 7, Lines(
            (5, Line(OutputSource.System, "a")),
            (6, Line(OutputSource.System, "b")),
            (7, Line(OutputSource.StdErr, "ERR"))));

        Assert.Equal(7, found);
    }

    [Fact]
    public void Partial_that_became_a_completed_line_at_a_later_number_is_found_there()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT-PARTIAL");

        long? found = PartialPins.Find(pin, 5, 0, 6, Lines(
            (5, Line(OutputSource.StdErr, "ERR")),
            (6, Line(OutputSource.StdOut, "OUT-PARTIAL"))));

        Assert.Equal(6, found);
    }

    [Fact]
    public void Same_text_from_another_source_is_skipped()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT");

        long? found = PartialPins.Find(pin, 5, 0, 7, Lines(
            (5, Line(OutputSource.StdErr, "ERR")),
            (6, Line(OutputSource.StdErr, "OUT")),
            (7, Line(OutputSource.StdOut, "OUT"))));

        Assert.Equal(7, found);
    }

    [Fact]
    public void In_place_match_is_preferred_over_a_later_match()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT");

        long? found = PartialPins.Find(pin, 5, 0, 6, Lines(
            (5, Line(OutputSource.StdOut, "OUT")),
            (6, Line(OutputSource.StdOut, "OUT"))));

        Assert.Equal(5, found);
    }

    [Fact]
    public void Text_that_no_longer_starts_with_the_pin_gives_null()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT");

        long? found = PartialPins.Find(pin, 5, 0, 6, Lines(
            (5, Line(OutputSource.StdOut, "new")),
            (6, Line(OutputSource.StdErr, "x"))));

        Assert.Null(found);
    }

    [Fact]
    public void Lines_after_the_last_retained_line_are_not_searched()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT");

        long? found = PartialPins.Find(pin, 5, 0, 6, Lines(
            (5, Line(OutputSource.StdErr, "ERR")),
            (7, Line(OutputSource.StdOut, "OUT"))));

        Assert.Null(found);
    }

    [Fact]
    public void Evicted_pinned_line_is_searched_from_the_first_retained_line()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT");

        long? found = PartialPins.Find(pin, 5, 7, 8, Lines(
            (7, Line(OutputSource.StdErr, "ERR")),
            (8, Line(OutputSource.StdOut, "OUT"))));

        Assert.Equal(8, found);
    }

    [Fact]
    public void Evicted_pinned_line_with_a_match_at_the_first_retained_line_returns_that_line()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT");

        long? found = PartialPins.Find(pin, 5, 7, 7, Lines((7, Line(OutputSource.StdOut, "OUT"))));

        Assert.Equal(7, found);
    }

    [Fact]
    public void Pin_of_a_line_that_is_gone_with_nothing_retained_gives_null()
    {
        var pin = new PartialPin(OutputSource.StdOut, "OUT");

        long? found = PartialPins.Find(pin, 5, 9, 8, Lines());

        Assert.Null(found);
    }

    [Fact]
    public void For_captures_the_source_and_the_current_text()
    {
        var pin = PartialPin.For(Line(OutputSource.StdErr, "ERR-PARTIAL"));

        Assert.Equal(new PartialPin(OutputSource.StdErr, "ERR-PARTIAL"), pin);
    }
}
