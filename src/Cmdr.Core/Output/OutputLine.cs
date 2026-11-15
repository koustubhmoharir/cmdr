namespace Cmdr.Core.Output;

/// <summary>An immutable line of output: styled text tagged with its source.</summary>
/// <remarks>
/// The text is stored once (<see cref="PlainText"/>) with its styles as <see cref="Runs"/> (start index and style), not as one
/// string per span: a buffer holds many thousands of lines, and colored output would otherwise keep its text twice.
/// </remarks>
public sealed class OutputLine
{
    // Exactly one of _plainText/_runs or _deferred is used: eager lines set the former, partial-line snapshots the latter.
    private readonly string? _plainText;
    private readonly StyleRun[]? _runs;
    private readonly DeferredText? _deferred;

    private static long s_lastIdentity;

    // Shared by every line whose text is one default-styled run (most plain output), so those lines allocate no run array.
    private static readonly StyleRun[] s_defaultRun = [new StyleRun(0, SpanStyle.Default)];

    /// <summary>
    /// Creates a line. Empty spans are dropped and adjacent spans with equal styles are merged.
    /// </summary>
    /// <param name="source">Where the line came from.</param>
    /// <param name="spans">The spans, in display order.</param>
    public OutputLine(OutputSource source, IReadOnlyList<StyledSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        Source = source;
        Identity = NewIdentity();
        (_plainText, _runs) = FromSpans(spans);
    }

    private OutputLine(OutputSource source, string plainText, StyleRun[] runs, long identity)
    {
        Source = source;
        Identity = identity;
        _plainText = plainText;
        _runs = runs;
    }

    private OutputLine(OutputSource source, DeferredText deferred, long identity)
    {
        Source = source;
        Identity = identity;
        _deferred = deferred;
    }

    /// <summary>
    /// Creates a completed line from a parser's arrays, copying the text and the runs it needs: the caller may reuse the arrays.
    /// Runs are <c>(start, style)</c> pairs in increasing start order; the text of run <c>i</c> ends where run <c>i + 1</c> starts,
    /// or at <paramref name="length"/>. Text before the first run is not part of the line. The result equals what the public
    /// constructor gives for the same spans.
    /// </summary>
    internal static OutputLine FromParser(
        OutputSource source,
        char[] chars,
        int length,
        (int Start, SpanStyle Style)[] runs,
        int runCount,
        long identity)
    {
        (string text, StyleRun[] styleRuns) = Build(chars, length, runs, runCount);
        return new OutputLine(source, text, styleRuns, identity);
    }

    /// <summary>
    /// Creates a line that shares a parser's partial-line arrays without copying them. Text and runs are built on the first
    /// read, at most once, and equal what <see cref="FromParser"/> gives for the same arrays.
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
        int runCount,
        long identity) =>
        new(source, new DeferredText(chars, length, runs, runCount), identity);

    /// <summary>A new identity, distinct from every other in this process.</summary>
    internal static long NewIdentity() => Interlocked.Increment(ref s_lastIdentity);

    /// <summary>
    /// The style runs over <see cref="PlainText"/>, in order. No run is empty and adjacent runs have different styles; an empty
    /// line has none. Does not allocate.
    /// </summary>
    public ReadOnlySpan<StyleRun> Runs => _runs ?? _deferred!.Value.Runs;

    /// <summary>
    /// The styled spans, in display order: one per run in <see cref="Runs"/>. Contains no empty spans and no adjacent equal
    /// styles. Builds a new list (and a string per span) on every call; drawing code uses <see cref="Runs"/> instead.
    /// </summary>
    public IReadOnlyList<StyledSpan> Spans
    {
        get
        {
            string text = PlainText;
            ReadOnlySpan<StyleRun> runs = Runs;
            var spans = new StyledSpan[runs.Length];
            for (int i = 0; i < runs.Length; i++)
            {
                spans[i] = new StyledSpan(text[runs[i].Start..RunEnd(i)], runs[i].Style);
            }

            return spans;
        }
    }

    /// <summary>Where run <paramref name="index"/> of <see cref="Runs"/> ends in <see cref="PlainText"/> (exclusive).</summary>
    public int RunEnd(int index)
    {
        ReadOnlySpan<StyleRun> runs = Runs;
        return index + 1 < runs.Length ? runs[index + 1].Start : PlainText.Length;
    }

    /// <summary>Where the line came from.</summary>
    public OutputSource Source { get; }

    /// <summary>
    /// Which logical line this is. The snapshots of a partial line and the line it is completed as share it (even when a
    /// carriage return rewrote its text); any other two lines differ. Lines are not compared by it.
    /// </summary>
    public long Identity { get; }

    /// <summary>The line's text with all styling removed.</summary>
    public string PlainText => _plainText ?? _deferred!.Value.PlainText;

    /// <summary>Joins the non-empty spans and merges adjacent equal styles.</summary>
    private static (string Text, StyleRun[] Runs) FromSpans(IReadOnlyList<StyledSpan> spans)
    {
        int length = 0;
        foreach (StyledSpan span in spans)
        {
            ArgumentNullException.ThrowIfNull(span.Text);
            length += span.Text.Length;
        }

        // One span (the common case: a system line) keeps its string.
        if (spans.Count == 1)
        {
            return (spans[0].Text, length == 0 ? [] : SingleRun(spans[0].Style));
        }

        var chars = new char[length];
        var runs = new (int Start, SpanStyle Style)[spans.Count];
        int position = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            runs[i] = (position, spans[i].Style);
            spans[i].Text.CopyTo(chars.AsSpan(position));
            position += spans[i].Text.Length;
        }

        return Build(chars, length, runs, runs.Length);
    }

    /// <summary>The line text and its normalized runs: empty runs dropped, adjacent equal styles merged. Copies what it keeps.</summary>
    private static (string Text, StyleRun[] Runs) Build(char[] chars, int length, (int Start, SpanStyle Style)[] runs, int runCount)
    {
        if (runCount == 0)
        {
            return (string.Empty, []);
        }

        int offset = runs[0].Start;
        string text = new(chars, offset, length - offset);

        // Two passes with the same rule: count the kept runs, then fill an array of exactly that size.
        int kept = 0;
        SpanStyle last = default;
        for (int i = 0; i < runCount; i++)
        {
            if (Keeps(runs, runCount, length, i, kept, last))
            {
                kept++;
                last = runs[i].Style;
            }
        }

        if (kept == 0)
        {
            return (text, []);
        }

        if (kept == 1)
        {
            return (text, SingleRun(last));
        }

        var result = new StyleRun[kept];
        int n = 0;
        for (int i = 0; i < runCount; i++)
        {
            if (Keeps(runs, runCount, length, i, n, n > 0 ? result[n - 1].Style : default))
            {
                result[n++] = new StyleRun(runs[i].Start - offset, runs[i].Style);
            }
        }

        return (text, result);
    }

    /// <summary>Whether run <paramref name="i"/> starts a run of the line: it is not empty and differs from the last kept style.</summary>
    private static bool Keeps((int Start, SpanStyle Style)[] runs, int runCount, int length, int i, int keptSoFar, SpanStyle lastKept)
    {
        int end = i + 1 < runCount ? runs[i + 1].Start : length;
        return end != runs[i].Start && (keptSoFar == 0 || runs[i].Style != lastKept);
    }

    private static StyleRun[] SingleRun(SpanStyle style) =>
        style == SpanStyle.Default ? s_defaultRun : [new StyleRun(0, style)];

    private sealed record Materialized(string PlainText, StyleRun[] Runs);

    /// <summary>The text of a partial-line snapshot, built on first use. Safe for concurrent readers.</summary>
    private sealed class DeferredText
    {
        private readonly Lazy<Materialized> _value;

        public DeferredText(char[] chars, int length, (int Start, SpanStyle Style)[] runs, int runCount)
        {
            _value = new Lazy<Materialized>(
                () =>
                {
                    (string text, StyleRun[] styleRuns) = Build(chars, length, runs, runCount);
                    return new Materialized(text, styleRuns);
                },
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Materialized Value => _value.Value;
    }
}
