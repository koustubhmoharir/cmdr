using Cmdr.Core.Output;

namespace Cmdr.Core.Tests.Output;

public class OutputLineTests
{
    private static readonly SpanStyle Red = SpanStyle.Default with { Foreground = AnsiColor.FromIndex(1) };
    private static readonly SpanStyle Bold = SpanStyle.Default with { Attrs = TextAttrs.Bold };

    [Fact]
    public void Runs_CoverThePlainText_WithEmptySpansDroppedAndEqualNeighboursMerged()
    {
        var line = new OutputLine(OutputSource.StdOut, new[]
        {
            new StyledSpan("ab", Red),
            new StyledSpan("", Bold),
            new StyledSpan("c", Red),
            new StyledSpan("de", Bold),
            new StyledSpan("f", SpanStyle.Default),
        });

        Assert.Equal("abcdef", line.PlainText);
        Assert.Equal(new[] { new StyleRun(0, Red), new StyleRun(3, Bold), new StyleRun(5, SpanStyle.Default) }, line.Runs.ToArray());
        Assert.Equal(new[] { 3, 5, 6 }, Enumerable.Range(0, line.Runs.Length).Select(line.RunEnd));
        Assert.Equal(
            new[] { new StyledSpan("abc", Red), new StyledSpan("de", Bold), new StyledSpan("f", SpanStyle.Default) },
            line.Spans);
    }

    [Fact]
    public void EmptyLine_HasNoRuns()
    {
        var line = new OutputLine(OutputSource.StdOut, new[] { new StyledSpan("", Red) });

        Assert.Equal("", line.PlainText);
        Assert.Equal(0, line.Runs.Length);
        Assert.Empty(line.Spans);
    }

    [Fact]
    public void ParsedLine_EqualsTheLineBuiltFromSpans()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var completed = new List<OutputLine>();

        parser.Feed("\x1b[31mab\x1b[1m\x1b[22mc\x1b[0;1mde\x1b[0mf\n", completed);

        OutputLine parsed = Assert.Single(completed);
        Assert.Equal("abcdef", parsed.PlainText);
        Assert.Equal(new[] { new StyleRun(0, Red), new StyleRun(3, Bold), new StyleRun(5, SpanStyle.Default) }, parsed.Runs.ToArray());
    }
}
