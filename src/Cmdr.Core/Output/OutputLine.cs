namespace Cmdr.Core.Output;

/// <summary>An immutable line of output: styled spans tagged with their source.</summary>
public sealed class OutputLine
{
    // Exactly one of _spans/_plainText or _deferred is used: eager lines set the former, partial-line snapshots the latter.
    private readonly StyledSpan[]? _spans;
    private readonly string? _plainText;
    private readonly DeferredText? _deferred;

    /// <summary>
    /// Creates a line. Empty spans are dropped and adjacent spans with equal styles are merged.
    /// </summary>
    /// <param name="source">Where the line came from.</param>
    /// <param name="spans">The spans, in display order.</param>
    public OutputLine(OutputSource source, IReadOnlyList<StyledSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        Source = source;
        _spans = MergeSpans(spans);
        _plainText = ConcatText(_spans);
    }

    private OutputLine(OutputSource source, DeferredText deferred)
    {
        Source = source;
        _deferred = deferred;
    }

    /// <summary>
    /// Creates a line that shares a parser's partial-line arrays without copying them. Spans and text are built on the first
    /// read, at most once, and equal what the public constructor gives for the same spans.
    /// </summary>
    /// <remarks>
    /// The caller guarantees that the arrays do not change below <paramref name="length"/> and <paramref name="runCount"/>
    /// for as long as the line exists (see <c>AnsiLineParser</c>, which moves to fresh arrays before such a write).
    /// </remarks>
    internal static OutputLine FromPartial(
        OutputSource source,
        char[] chars,
        int length,
        (int Start, SpanStyle Style)[] runs,
        int runCount) =>
        new(source, new DeferredText(chars, length, runs, runCount));

    /// <summary>The styled spans, in display order. Contains no empty spans and no adjacent equal styles.</summary>
    public IReadOnlyList<StyledSpan> Spans => _spans ?? _deferred!.Value.Spans;

    /// <summary>Where the line came from.</summary>
    public OutputSource Source { get; }

    /// <summary>The line's text with all styling removed.</summary>
    public string PlainText => _plainText ?? _deferred!.Value.PlainText;

    /// <summary>Drops empty spans and merges adjacent spans with equal styles.</summary>
    private static StyledSpan[] MergeSpans(IReadOnlyList<StyledSpan> spans)
    {
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

        return merged.ToArray();
    }

    private static string ConcatText(StyledSpan[] spans)
    {
        var texts = new string[spans.Length];
        for (int i = 0; i < spans.Length; i++)
        {
            texts[i] = spans[i].Text;
        }

        return string.Concat(texts);
    }

    private sealed record Materialized(StyledSpan[] Spans, string PlainText);

    /// <summary>The text of a partial-line snapshot, built on first use. Safe for concurrent readers.</summary>
    private sealed class DeferredText
    {
        private readonly char[] _chars;
        private readonly int _length;
        private readonly (int Start, SpanStyle Style)[] _runs;
        private readonly int _runCount;
        private readonly Lazy<Materialized> _value;

        public DeferredText(char[] chars, int length, (int Start, SpanStyle Style)[] runs, int runCount)
        {
            _chars = chars;
            _length = length;
            _runs = runs;
            _runCount = runCount;
            _value = new Lazy<Materialized>(() => Materialize(), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Materialized Value => _value.Value;

        private Materialized Materialize()
        {
            var raw = new StyledSpan[_runCount];
            for (int i = 0; i < _runCount; i++)
            {
                int start = _runs[i].Start;
                int end = i + 1 < _runCount ? _runs[i + 1].Start : _length;
                raw[i] = new StyledSpan(new string(_chars, start, end - start), _runs[i].Style);
            }

            StyledSpan[] spans = MergeSpans(raw);
            return new Materialized(spans, ConcatText(spans));
        }
    }
}
