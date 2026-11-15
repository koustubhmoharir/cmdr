using Cmdr.Core.Output;
using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class PartialPinsTests
{
    /// <summary>A standalone line: its identity is its own, so it is never the pinned line.</summary>
    private static OutputLine Line(OutputSource source, string text) =>
        new(source, new[] { new StyledSpan(text, SpanStyle.Default) });

    /// <summary>A lookup over the given absolute numbers; other numbers are not retained.</summary>
    private static Func<long, OutputLine?> Lines(params (long Number, OutputLine Line)[] lines)
    {
        var map = lines.ToDictionary(l => l.Number, l => l.Line);
        return number => map.TryGetValue(number, out OutputLine? line) ? line : null;
    }

    /// <summary>A parser producing one logical line: its partial snapshots, and its completed form.</summary>
    private sealed class OneLine(OutputSource source)
    {
        private readonly AnsiLineParser _parser = new(source);

        public OutputLine Partial(string more)
        {
            _parser.Feed(more, new List<OutputLine>());
            return _parser.Partial!;
        }

        public OutputLine Complete(string more)
        {
            var completed = new List<OutputLine>();
            _parser.Feed(more + "\n", completed);
            return Assert.Single(completed);
        }
    }

    [Fact]
    public void Pin_is_found_in_place_when_the_line_is_unchanged()
    {
        var line = new OneLine(OutputSource.StdOut);
        OutputLine partial = line.Partial("OUT");

        long? found = PartialPins.Find(PartialPin.For(partial), 5, 0, 5, Lines((5, partial)));

        Assert.Equal(5, found);
    }

    [Fact]
    public void Growing_partial_keeps_matching_by_its_prefix()
    {
        var line = new OneLine(OutputSource.StdOut);
        PartialPin pin = PartialPin.For(line.Partial("OUT"));

        long? found = PartialPins.Find(pin, 5, 0, 5, Lines((5, line.Partial("-PARTIAL"))));

        Assert.Equal(5, found);
    }

    [Fact]
    public void Pin_pushed_down_by_two_lines_is_found_at_the_later_number()
    {
        var line = new OneLine(OutputSource.StdErr);
        OutputLine partial = line.Partial("ERR");

        long? found = PartialPins.Find(PartialPin.For(partial), 5, 0, 7, Lines(
            (5, Line(OutputSource.System, "a")),
            (6, Line(OutputSource.System, "b")),
            (7, partial)));

        Assert.Equal(7, found);
    }

    [Fact]
    public void Partial_that_became_a_completed_line_at_a_later_number_is_found_there()
    {
        var line = new OneLine(OutputSource.StdOut);
        PartialPin pin = PartialPin.For(line.Partial("OUT"));

        long? found = PartialPins.Find(pin, 5, 0, 6, Lines(
            (5, Line(OutputSource.StdErr, "ERR")),
            (6, line.Complete("-DONE"))));

        Assert.Equal(6, found);
    }

    [Fact]
    public void Another_line_with_the_same_source_and_text_is_not_the_pinned_line()
    {
        var line = new OneLine(OutputSource.StdOut);
        PartialPin pin = PartialPin.For(line.Partial("OUT"));

        long? found = PartialPins.Find(pin, 5, 0, 6, Lines(
            (5, Line(OutputSource.StdOut, "new")),
            (6, Line(OutputSource.StdOut, "OUT-UNRELATED"))));

        Assert.Null(found);
    }

    [Fact]
    public void Partial_rewritten_by_a_carriage_return_no_longer_matches()
    {
        var line = new OneLine(OutputSource.StdOut);
        PartialPin pin = PartialPin.For(line.Partial("OUT"));

        long? found = PartialPins.Find(pin, 5, 0, 6, Lines(
            (5, line.Complete("\rOTHER")),
            (6, Line(OutputSource.StdOut, "OUT-UNRELATED"))));

        Assert.Null(found);
    }

    [Fact]
    public void Evicted_pinned_line_gives_null_even_when_a_retained_line_has_its_text()
    {
        var line = new OneLine(OutputSource.StdOut);
        PartialPin pin = PartialPin.For(line.Partial("OUT"));

        long? found = PartialPins.Find(pin, 5, 7, 8, Lines(
            (7, Line(OutputSource.StdErr, "ERR")),
            (8, Line(OutputSource.StdOut, "OUT"))));

        Assert.Null(found);
    }

    [Fact]
    public void Lines_after_the_last_retained_line_are_not_searched()
    {
        var line = new OneLine(OutputSource.StdOut);
        OutputLine partial = line.Partial("OUT");

        long? found = PartialPins.Find(PartialPin.For(partial), 5, 0, 6, Lines(
            (5, Line(OutputSource.StdErr, "ERR")),
            (7, partial)));

        Assert.Null(found);
    }

    [Fact]
    public void Pin_of_a_line_that_is_gone_with_nothing_retained_gives_null()
    {
        var line = new OneLine(OutputSource.StdOut);

        long? found = PartialPins.Find(PartialPin.For(line.Partial("OUT")), 5, 9, 8, Lines());

        Assert.Null(found);
    }

    [Fact]
    public void For_captures_the_identity_and_the_current_text()
    {
        OutputLine partial = new OneLine(OutputSource.StdErr).Partial("ERR-PARTIAL");

        Assert.Equal(new PartialPin(partial.Identity, "ERR-PARTIAL"), PartialPin.For(partial));
    }
}
