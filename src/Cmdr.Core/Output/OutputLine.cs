namespace Cmdr.Core.Output;

/// <summary>An immutable line of output: styled spans tagged with their source.</summary>
public sealed class OutputLine
{
    private readonly StyledSpan[] _spans;

    /// <summary>
    /// Creates a line. Empty spans are dropped and adjacent spans with equal styles are merged.
    /// </summary>
    /// <param name="source">Where the line came from.</param>
    /// <param name="spans">The spans, in display order.</param>
    public OutputLine(OutputSource source, IReadOnlyList<StyledSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        Source = source;

        var merged = new List<StyledSpan>(spans.Count);
        foreach (StyledSpan span in spans)
        {
            ArgumentNullException.ThrowIfNull(span.Text);
            if (span.Text.Length == 0)
            {
                continue;
            }

            if (merged.Count > 0 && merged[^1].Style == span.Style)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + span.Text };
            }
            else
            {
                merged.Add(span);
            }
        }

        _spans = merged.ToArray();

        var texts = new string[_spans.Length];
        for (int i = 0; i < _spans.Length; i++)
        {
            texts[i] = _spans[i].Text;
        }

        PlainText = string.Concat(texts);
    }

    /// <summary>The styled spans, in display order. Contains no empty spans and no adjacent equal styles.</summary>
    public IReadOnlyList<StyledSpan> Spans => _spans;

    /// <summary>Where the line came from.</summary>
    public OutputSource Source { get; }

    /// <summary>The line's text with all styling removed.</summary>
    public string PlainText { get; }
}
