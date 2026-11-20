using Cmdr.Core.Output;
using Cmdr.Tui;
using Shared.Core;

namespace Cmdr.Tests;

public sealed class OutputSearchIndexTests
{
    private static OutputLine Line(string text) => new(OutputSource.StdOut, new[] { new StyledSpan(text, SpanStyle.Default) });

    private static SearchPattern Literal(string text)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(text, true, false, false), out string? error);
        Assert.Null(error);
        return Assert.IsType<SearchPattern>(pattern);
    }

    /// <summary>The matches on every retained line, counted from scratch.</summary>
    private static int Expected(OutputBuffer buffer, SearchPattern pattern) =>
        buffer.GetLines(0, buffer.Count).Sum(line => pattern.Count(line.PlainText));

    /// <summary>The matches on the retained lines numbered below <paramref name="line"/>, counted from scratch.</summary>
    private static int ExpectedBefore(OutputBuffer buffer, SearchPattern pattern, long line)
    {
        IReadOnlyList<OutputLine> lines = buffer.GetLines(0, buffer.Count);
        long first = buffer.FirstLineNumber;
        int total = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (first + i < line)
            {
                total += pattern.Count(lines[i].PlainText);
            }
        }

        return total;
    }

    private static OutputSearchIndex IndexFor(OutputBuffer buffer, SearchPattern pattern)
    {
        var index = new OutputSearchIndex();
        index.Reset(pattern);
        index.Update(buffer);
        return index;
    }

    [Fact]
    public void Total_counts_matches_on_every_appended_line()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("x"), Line("xx"), Line("y") });

        OutputSearchIndex index = IndexFor(buffer, Literal("x"));

        Assert.Equal(3, index.Total);
    }

    [Fact]
    public void Update_after_more_appends_adds_their_matches()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("x"), Line("a") });
        OutputSearchIndex index = IndexFor(buffer, Literal("x"));

        buffer.Append(new[] { Line("xxx"), Line("b") });
        index.Update(buffer);

        Assert.Equal(4, index.Total);
        Assert.Equal(Expected(buffer, Literal("x")), index.Total);
    }

    [Fact]
    public void Update_with_no_new_lines_keeps_the_total()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("x") });
        OutputSearchIndex index = IndexFor(buffer, Literal("x"));

        index.Update(buffer);
        index.Update(buffer);

        Assert.Equal(1, index.Total);
    }

    [Fact]
    public void Evicted_lines_stop_counting()
    {
        var buffer = new OutputBuffer(3);
        var index = new OutputSearchIndex();
        index.Reset(Literal("x"));
        buffer.Append(new[] { Line("x"), Line("x"), Line("x") });
        index.Update(buffer);
        Assert.Equal(3, index.Total);

        // Lines 0 and 1 are evicted: the retained lines are 2 "x", 3 "x", 4 "a".
        buffer.Append(new[] { Line("x"), Line("a") });
        index.Update(buffer);

        Assert.Equal(2, index.Total);
        Assert.Equal(Expected(buffer, Literal("x")), index.Total);
        Assert.Equal(0, index.CountOn(0));
        Assert.Equal(0, index.CountOn(1));
        Assert.Equal(1, index.CountOn(2));
        Assert.Equal(0, index.CountOn(4));
    }

    [Fact]
    public void Clear_drops_all_counts()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("x"), Line("x") });
        OutputSearchIndex index = IndexFor(buffer, Literal("x"));

        buffer.Clear();
        index.Update(buffer);
        Assert.Equal(0, index.Total);

        buffer.Append(new[] { Line("x") });
        index.Update(buffer);
        Assert.Equal(1, index.Total);
        Assert.Equal(0, index.CountOn(0));
        Assert.Equal(1, index.CountOn(2));
    }

    [Fact]
    public void Partial_line_is_counted_and_recounted_when_it_changes()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("x") });
        OutputSearchIndex index = IndexFor(buffer, Literal("x"));

        buffer.SetPartial(OutputSource.StdOut, Line("x"));
        index.Update(buffer);
        Assert.Equal(2, index.Total);
        Assert.Equal(1, index.CountOn(1));

        buffer.SetPartial(OutputSource.StdOut, Line("xxx"));
        index.Update(buffer);
        Assert.Equal(4, index.Total);
        Assert.Equal(3, index.CountOn(1));
    }

    [Fact]
    public void Partial_line_is_counted_once_when_it_completes()
    {
        var buffer = new OutputBuffer(10);
        buffer.SetPartial(OutputSource.StdOut, Line("xx"));
        OutputSearchIndex index = IndexFor(buffer, Literal("x"));
        Assert.Equal(2, index.Total);

        buffer.AppendAndSetPartial(new[] { Line("xx") }, OutputSource.StdOut, null);
        index.Update(buffer);

        Assert.Equal(2, index.Total);
        Assert.Equal(Expected(buffer, Literal("x")), index.Total);
        Assert.Equal(2, index.CountOn(0));
    }

    [Fact]
    public void CountBefore_and_CountOn_cover_completed_and_partial_lines()
    {
        // Numbers: 0 "x" (1), 1 "a" (0), 2 "xx" (2), partial 3 "x" (1).
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("x"), Line("a"), Line("xx") });
        buffer.SetPartial(OutputSource.StdOut, Line("x"));
        OutputSearchIndex index = IndexFor(buffer, Literal("x"));

        Assert.Equal(0, index.CountBefore(0));
        Assert.Equal(1, index.CountBefore(1));
        Assert.Equal(1, index.CountBefore(2));
        Assert.Equal(3, index.CountBefore(3));
        Assert.Equal(4, index.CountBefore(4));
        Assert.Equal(4, index.CountBefore(100));

        Assert.Equal(1, index.CountOn(0));
        Assert.Equal(0, index.CountOn(1));
        Assert.Equal(2, index.CountOn(2));
        Assert.Equal(1, index.CountOn(3));
    }

    [Fact]
    public void Lines_that_are_not_retained_count_as_zero()
    {
        var buffer = new OutputBuffer(2);
        buffer.Append(new[] { Line("x"), Line("x"), Line("x") });
        OutputSearchIndex index = IndexFor(buffer, Literal("x"));

        Assert.Equal(0, index.CountOn(-1));
        Assert.Equal(0, index.CountOn(0));
        Assert.Equal(1, index.CountOn(1));
        Assert.Equal(0, index.CountOn(3));
        Assert.Equal(0, index.CountOn(100));
        Assert.Equal(0, index.CountBefore(1));
        Assert.Equal(1, index.CountBefore(2));
    }

    [Fact]
    public void CountBefore_matches_a_recount_for_every_line()
    {
        var buffer = new OutputBuffer(7);
        buffer.Append(new[] { Line("x y"), Line("none"), Line("xxx"), Line("x"), Line("yy"), Line("xy x") });
        buffer.SetPartial(OutputSource.StdOut, Line("x x"));
        SearchPattern pattern = Literal("x");
        OutputSearchIndex index = IndexFor(buffer, pattern);

        for (long line = 0; line <= buffer.NextLineNumber + 2; line++)
        {
            Assert.Equal(ExpectedBefore(buffer, pattern, line), index.CountBefore(line));
        }
    }

    [Fact]
    public void Reset_with_null_pattern_gives_zero_and_later_updates_do_nothing()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("x") });
        OutputSearchIndex index = IndexFor(buffer, Literal("x"));

        index.Reset(null);
        index.Update(buffer);

        Assert.Null(index.Pattern);
        Assert.Equal(0, index.Total);
        Assert.Equal(0, index.CountBefore(10));
        Assert.Equal(0, index.CountOn(0));
    }

    [Fact]
    public void Update_without_a_pattern_is_a_no_op()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("x") });
        var index = new OutputSearchIndex();

        index.Update(buffer);

        Assert.Equal(0, index.Total);
    }

    [Fact]
    public void Reset_with_a_new_pattern_recounts_the_retained_lines()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("x a"), Line("a"), Line("aa") });
        OutputSearchIndex index = IndexFor(buffer, Literal("x"));
        Assert.Equal(1, index.Total);

        index.Reset(Literal("a"));
        index.Update(buffer);

        Assert.Equal(4, index.Total);
        Assert.Equal(Expected(buffer, Literal("a")), index.Total);
    }

    [Fact]
    public void Update_on_a_different_buffer_counts_that_buffer_only()
    {
        var first = new OutputBuffer(10);
        first.Append(new[] { Line("x"), Line("x") });
        var second = new OutputBuffer(10);
        second.Append(new[] { Line("x") });
        OutputSearchIndex index = IndexFor(first, Literal("x"));

        index.Update(second);

        Assert.Equal(1, index.Total);
    }

    [Fact]
    public void Long_stream_with_eviction_matches_a_recount_after_every_chunk()
    {
        var buffer = new OutputBuffer(1_000);
        SearchPattern pattern = Literal("x");
        var index = new OutputSearchIndex();
        index.Reset(pattern);

        for (int chunk = 0; chunk < 150; chunk++)
        {
            var lines = new List<OutputLine>();
            for (int i = 0; i < 100; i++)
            {
                int n = chunk * 100 + i;
                lines.Add(Line(n % 3 == 0 ? $"x {n} x" : $"line {n}"));
            }

            buffer.Append(lines);
            index.Update(buffer);

            Assert.Equal(Expected(buffer, pattern), index.Total);
        }

        long first = buffer.FirstLineNumber;
        Assert.Equal(ExpectedBefore(buffer, pattern, first + 500), index.CountBefore(first + 500));
        Assert.Equal(0, index.CountOn(first - 1));
    }
}
