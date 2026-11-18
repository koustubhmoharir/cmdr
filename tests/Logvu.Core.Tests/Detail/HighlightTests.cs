using System.Text.RegularExpressions;
using Logvu.Core.Config;
using Logvu.Core.Detail;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Detail;

/// <summary>Highlight rules: field scopes, ordering, splitting of structural spans, and regex timeouts.</summary>
public sealed class HighlightTests
{
    private const string RunFailedInfo = "INFO  2026-11-18 10:20:30,123 [12] SheetKraft.Engine.Runner - Run failed";

    private static List<(string Style, string Text)> Highlights(DetailLine line) =>
        line.Spans
            .Where(span => span.Style == SpanStyle.Highlight)
            .Select(span => (span.HighlightStyle!, line.Text.Substring(span.Start, span.Length)))
            .ToList();

    private static DetailDocument Document(string text, ILogScheme scheme, LogvuConfig config, DetailOptions? options = null) =>
        DetailRig.Build(DetailRig.Loaded(text, scheme), config, options);

    [Fact]
    public void On_overlapping_spans_the_first_configured_rule_wins_and_the_later_one_keeps_what_is_left()
    {
        LogvuConfig config = DetailRig.Config(
        [
            DetailRig.Rule("message", @"Run\w*", "error"),
            DetailRig.Rule("message", "Run failed", "warning"),
        ]);

        DetailLine message = Document(RunFailedInfo, BuiltInSchemes.Log4NetCore, config).Lines
            .Single(l => l.Kind == LineKind.Message);

        Assert.Equal([("error", "Run"), ("warning", " failed")], Highlights(message));
    }

    [Fact]
    public void Reversing_the_rules_reverses_the_winner()
    {
        LogvuConfig config = DetailRig.Config(
        [
            DetailRig.Rule("message", "Run failed", "warning"),
            DetailRig.Rule("message", @"Run\w*", "error"),
        ]);

        DetailLine message = Document(RunFailedInfo, BuiltInSchemes.Log4NetCore, config).Lines
            .Single(l => l.Kind == LineKind.Message);

        Assert.Equal([("warning", "Run failed")], Highlights(message));
    }

    [Fact]
    public void Highlight_splits_the_structural_span_it_overlaps()
    {
        LogvuConfig config = DetailRig.Config([DetailRig.Rule("message", "not found", "warning")]);
        string text = DetailRig.TextEvents("log4net-core.log", BuiltInSchemes.Log4NetCore)
            .Single(e => e.StartsWith("ERROR ", StringComparison.Ordinal));

        DetailLine exceptionMessage = Document(text, BuiltInSchemes.Log4NetCore, config).Lines
            .Single(l => l.Text.Contains("Sheet not found", StringComparison.Ordinal));

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.ExceptionType, "System.InvalidOperationException"),
            (SpanStyle.Punctuation, ": "),
            (SpanStyle.ExceptionMessage, "Sheet "),
            (SpanStyle.Highlight, "not found"),
        ], DetailRig.SpansOf(exceptionMessage));
    }

    [Fact]
    public void Dotted_path_rule_applies_only_to_that_node_value()
    {
        LogvuConfig config = DetailRig.Config([DetailRig.Rule("Properties.SourceContext", "Orders", "accent")]);
        string json = DetailRig.JsonEvents("serilog.json").Single(l => l.Contains("Failed to process", StringComparison.Ordinal));

        IReadOnlyList<DetailLine> lines = Document(json, BuiltInSchemes.SerilogJson, config).Lines;

        DetailLine sourceContext = lines.Single(l => l.Text.Contains("SourceContext:", StringComparison.Ordinal));
        Assert.Equal([("accent", "Orders")], Highlights(sourceContext));
        Assert.All(lines.Where(l => l != sourceContext), l => Assert.Empty(Highlights(l)));
    }

    [Fact]
    public void Field_rule_highlights_the_value_and_not_the_field_name()
    {
        LogvuConfig config = DetailRig.Config([DetailRig.Rule("thread", @"\d+", "info")]);

        IReadOnlyList<DetailLine> lines = Document(RunFailedInfo, BuiltInSchemes.Log4NetCore, config).Lines;

        DetailLine thread = lines.Single(l => l.Kind == LineKind.Field && l.Scope == "thread");
        Assert.Equal([("info", "12")], Highlights(thread));
    }

    [Fact]
    public void Raw_scope_applies_only_in_the_raw_view()
    {
        LogvuConfig config = DetailRig.Config([DetailRig.Rule("raw", "Runner", "info")]);

        DetailDocument structured = Document(RunFailedInfo, BuiltInSchemes.Log4NetCore, config);
        Assert.All(structured.Lines, l => Assert.Empty(Highlights(l)));

        DetailDocument raw = Document(RunFailedInfo, BuiltInSchemes.Log4NetCore, config, new DetailOptions { Raw = true });
        Assert.Equal([("info", "Runner")], Highlights(raw.Lines.Single()));
    }

    [Fact]
    public void Timed_out_rule_contributes_nothing_and_later_rules_still_apply()
    {
        // The catastrophic pattern runs with a 1 ms limit: it times out on this input, which the premise assertion checks.
        var slow = new HighlightRule("message", new Regex(@"(x+x+)+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(1)), "error");
        string message = "Run " + new string('x', 60) + "!";
        Assert.Throws<RegexMatchTimeoutException>(() => slow.Pattern.Match(message));

        LogvuConfig config = DetailRig.Config([slow, DetailRig.Rule("message", "Run", "info")]);
        string text = "INFO  2026-11-18 10:20:30,123 [12] SheetKraft.Engine.Runner - " + message;

        DetailLine line = Document(text, BuiltInSchemes.Log4NetCore, config).Lines.Single(l => l.Kind == LineKind.Message);

        Assert.Equal([("info", "Run")], Highlights(line));
    }

    [Fact]
    public void Highlight_spans_are_inside_the_text_and_name_their_style()
    {
        LogvuConfig config = DetailRig.Config([DetailRig.Rule("message", "Run", "emphasis")]);

        DetailDocument doc = Document(RunFailedInfo, BuiltInSchemes.Log4NetCore, config);

        DetailRig.AssertInvariants(doc.Lines);
        Assert.All(doc.Lines.SelectMany(l => l.Spans).Where(s => s.Style == SpanStyle.Highlight), s => Assert.Equal("emphasis", s.HighlightStyle));
    }
}
