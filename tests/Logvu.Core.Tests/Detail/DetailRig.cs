using System.Text;
using System.Text.RegularExpressions;
using Logvu.Core.Config;
using Logvu.Core.Detail;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Parsing;

namespace Logvu.Core.Tests.Detail;

/// <summary>Shared helpers for the detail tests: configurations, loaded details, rendering and the span invariants.</summary>
internal static class DetailRig
{
    /// <summary>The path every test event is reported from.</summary>
    public const string EventPath = "/var/log/SheetKraft1.log";

    /// <summary>
    /// A configuration with the given highlight rules, framework prefixes, columns and message fields. It is built directly, not from a
    /// file, so a rule can carry its own regex timeout.
    /// </summary>
    public static LogvuConfig Config(
        IReadOnlyList<HighlightRule>? highlights = null,
        IReadOnlyList<string>? frameworkPrefixes = null,
        IReadOnlyList<ColumnSpec>? columns = null,
        IReadOnlyList<string>? messageFields = null) =>
        new(
            configPath: "/etc/logvu/logvu.json",
            logsDirectory: "/var/log",
            filePrefix: "SheetKraft",
            fileNamePattern: new Regex(@"\ASheetKraft(?:[0-9]+)?[.]log\z", RegexOptions.CultureInvariant),
            activeFile: null,
            format: null,
            schemes: BuiltInSchemes.All,
            timeZone: TimeZoneInfo.Utc,
            recentCapacity: LogvuConfig.DefaultRecentCapacity,
            minLevel: null,
            columns: columns ?? [],
            highlights: highlights ?? [],
            styles: new Dictionary<string, StyleSpec>(StringComparer.OrdinalIgnoreCase),
            frameworkPrefixes: frameworkPrefixes ?? LogvuConfig.DefaultFrameworkPrefixes,
            messageFields: messageFields ?? []);

    /// <summary>A highlight rule with the configuration's 250 ms timeout.</summary>
    public static HighlightRule Rule(string field, string pattern, string style) =>
        new(field, new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)), style);

    /// <summary>
    /// A loaded detail of an event whose text is <paramref name="text"/>, parsed with <paramref name="scheme"/> as the store would.
    /// </summary>
    public static EventDetail.Loaded Loaded(string text, ILogScheme scheme, bool truncated = false, long offset = 4096)
    {
        LogEvent e = scheme.Parse(text, new ParseContext(TimeZoneInfo.Utc));
        long length = Encoding.UTF8.GetByteCount(text) + 1;
        string preview = text.Split('\n')[0];
        var row = new EventRow(
            new RowKey(1, offset),
            new EventExtent(offset, offset + length, offset + Encoding.UTF8.GetByteCount(preview) + 1, Provisional: false, Orphan: false),
            e.Time,
            e.TimeText,
            e.Level,
            e.LevelText,
            preview,
            e.IsRaw,
            Orphan: false,
            Provisional: false);
        return new EventDetail.Loaded(row, e, text, truncated, EventPath, offset, length);
    }

    /// <summary>The text events of a fixture, as the store frames them.</summary>
    public static List<string> TextEvents(string fixture, ILogScheme scheme) => ParsingFixtures.TextEvents(fixture, scheme);

    /// <summary>The JSON events of a fixture, one per non-blank line.</summary>
    public static List<string> JsonEvents(string fixture) => ParsingFixtures.JsonEvents(fixture);

    /// <summary>Builds a document and checks the span invariants of both its full and visible lines.</summary>
    public static DetailDocument Build(EventDetail detail, LogvuConfig? config = null, DetailOptions? options = null)
    {
        DetailDocument document = DetailDocument.Build(detail, config ?? Config(), options ?? DetailOptions.Default);
        AssertInvariants(document.FullLines);
        AssertInvariants(document.Lines);
        return document;
    }

    /// <summary>
    /// Asserts that every line's spans are ordered, non-overlapping, non-empty and inside the line's text, that the indent is inside the
    /// text, and that a highlight span names its style.
    /// </summary>
    public static void AssertInvariants(IEnumerable<DetailLine> lines)
    {
        foreach (DetailLine line in lines)
        {
            Assert.InRange(line.Indent, 0, line.Text.Length);
            int previousEnd = 0;
            foreach (StyledSpan span in line.Spans)
            {
                Assert.True(span.Length > 0, $"empty span in '{line.Text}'");
                Assert.True(span.Start >= previousEnd, $"span overlaps or is unordered in '{line.Text}'");
                Assert.True(span.Start + span.Length <= line.Text.Length, $"span outside '{line.Text}'");
                Assert.Equal(span.Style == SpanStyle.Highlight, span.HighlightStyle is not null);
                previousEnd = span.Start + span.Length;
            }
        }
    }

    /// <summary>One line as <c>Kind|indent|text</c>, followed by its spans as <c>[Style:text]</c> when it has any.</summary>
    public static string Render(DetailLine line)
    {
        string row = $"{line.Kind}|{line.Indent}|{line.Text}";
        if (line.Spans.Count == 0)
        {
            return row;
        }

        string spans = string.Concat(line.Spans.Select(span =>
        {
            string style = span.HighlightStyle is null ? span.Style.ToString() : $"Highlight={span.HighlightStyle}";
            return $"[{style}:{line.Text.Substring(span.Start, span.Length)}]";
        }));
        return row + "  " + spans;
    }

    /// <summary>Every line rendered, in order.</summary>
    public static List<string> Rows(IEnumerable<DetailLine> lines) => lines.Select(Render).ToList();

    /// <summary>The first line whose text starts with <paramref name="prefix"/>. Fails the test when there is none.</summary>
    public static DetailLine Line(IReadOnlyList<DetailLine> lines, string prefix)
    {
        DetailLine? line = lines.FirstOrDefault(l => l.Text.StartsWith(prefix, StringComparison.Ordinal));
        Assert.NotNull(line);
        return line;
    }

    /// <summary>The spans of a line, as <c>(Style, text)</c> pairs, in order.</summary>
    public static List<(SpanStyle Style, string Text)> SpansOf(DetailLine line) =>
        line.Spans.Select(span => (span.Style, line.Text.Substring(span.Start, span.Length))).ToList();

    /// <summary>The spans of a stack line, as <c>(Style, text)</c> pairs, in order.</summary>
    public static List<(SpanStyle Style, string Text)> SpansOf(StackLine line) =>
        line.Spans.Select(span => (span.Style, line.Text.Substring(span.Start, span.Length))).ToList();
}
