using Cmdr.Core.Output;

namespace Cmdr.Core.Tests.Output;

/// <summary>Shared helpers for output tests.</summary>
internal static class OutputTestHelpers
{
    /// <summary>Parses the chunks in order, then flushes. Returns all completed lines.</summary>
    public static List<OutputLine> Parse(params string[] chunks)
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var lines = new List<OutputLine>();
        foreach (string chunk in chunks)
        {
            parser.Feed(chunk, lines);
        }

        parser.Flush(lines);
        return lines;
    }

    /// <summary>Plain text of each line.</summary>
    public static string[] Texts(IEnumerable<OutputLine> lines) => lines.Select(l => l.PlainText).ToArray();

    /// <summary>A stable string form of lines (source, text and every span with its style) for equality checks.</summary>
    public static string[] Describe(IEnumerable<OutputLine> lines) =>
        lines.Select(l => $"{l.Source}|{l.PlainText}|{string.Join(";", l.Spans.Select(s => $"{s.Text}/{s.Style}"))}").ToArray();

    /// <summary>The style of the first character of the single line produced by <paramref name="input"/>.</summary>
    public static SpanStyle StyleOf(string input)
    {
        List<OutputLine> lines = Parse(input);
        Assert.Single(lines);
        return lines[0].Spans[0].Style;
    }

    /// <summary>A system-source line with one default-styled span.</summary>
    public static OutputLine Line(string text, OutputSource source = OutputSource.StdOut) =>
        new(source, text.Length == 0 ? Array.Empty<StyledSpan>() : new[] { new StyledSpan(text, SpanStyle.Default) });

    /// <summary>Plain text of all lines in a buffer, in order.</summary>
    public static string[] BufferTexts(OutputBuffer buffer) =>
        Texts(buffer.GetLines(0, buffer.Count));
}
