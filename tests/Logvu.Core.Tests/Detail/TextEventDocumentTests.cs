using Logvu.Core.Detail;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Detail;

/// <summary>A log4net-core text event: header, named fields, the multi-line message and its framework-frame folds.</summary>
public sealed class TextEventDocumentTests
{
    private const string RunnerFrame = "   at SheetKraft.Engine.Runner.Execute(Int32 id) in C:\\src\\Engine\\Runner.cs:line 88";
    private const string FirstSystemFrame = "   at System.Threading.Tasks.Task.Execute()";

    /// <summary>
    /// The fixture's ERROR event, with three consecutive System frames after the Runner frame: the fixture itself has none.
    /// </summary>
    internal static string ErrorEvent()
    {
        string fixture = DetailRig.TextEvents("log4net-core.log", BuiltInSchemes.Log4NetCore)
            .Single(e => e.StartsWith("ERROR ", StringComparison.Ordinal));
        Assert.Contains(RunnerFrame, fixture);
        return fixture.Replace(
            RunnerFrame,
            RunnerFrame + "\n" + FirstSystemFrame
                + "\n   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext)"
                + "\n   at System.Threading.ExecutionContext.Run(ExecutionContext executionContext)",
            StringComparison.Ordinal);
    }

    private static DetailDocument Document(DetailOptions? options = null) =>
        DetailRig.Build(DetailRig.Loaded(ErrorEvent(), BuiltInSchemes.Log4NetCore), options: options);

    [Fact]
    public void Fields_are_listed_in_their_order_with_their_names_as_scope()
    {
        IReadOnlyList<DetailLine> fields = Document().Lines.Where(l => l.Kind == LineKind.Field).ToList();

        Assert.Equal(["level", "time", "thread", "logger"], fields.Select(f => f.Scope));
        Assert.Equal(
        [
            "level: ERROR",
            "time: 2026-11-18 10:20:31,007",
            "thread: Worker 3",
            "logger: SheetKraft.Engine.Runner",
        ], fields.Select(f => f.Text));
        Assert.All(fields, f => Assert.Equal([SpanStyle.FieldName, SpanStyle.Punctuation], f.Spans.Select(s => s.Style)));
    }

    [Fact]
    public void Time_level_and_logger_are_field_lines_whose_values_are_as_written()
    {
        IReadOnlyList<DetailLine> fields = Document().Lines.Where(l => l.Kind == LineKind.Field).ToList();

        DetailLine level = fields.Single(f => f.Scope == "level");
        DetailLine time = fields.Single(f => f.Scope == "time");
        DetailLine logger = fields.Single(f => f.Scope == "logger");
        Assert.Equal("ERROR", level.Value);
        Assert.Equal("2026-11-18 10:20:31,007", time.Value);
        Assert.Equal("SheetKraft.Engine.Runner", logger.Value);
    }

    [Fact]
    public void Message_starts_with_its_label_and_continuation_lines_sit_at_the_value_column()
    {
        IReadOnlyList<DetailLine> message = Document().Lines.Where(l => l.Kind == LineKind.Message).ToList();

        Assert.Equal("message: Run failed", message[0].Text);
        Assert.Equal(9, message[0].Indent);
        Assert.All(message, line => Assert.Equal("message", line.Scope));
        Assert.All(message.Skip(1), line => Assert.Equal(9, line.Indent));
    }

    [Fact]
    public void The_message_is_the_last_field_in_pattern_order_and_there_is_no_separate_Message_line()
    {
        IReadOnlyList<DetailLine> lines = Document().Lines;

        Assert.DoesNotContain(lines, l => l.Text.StartsWith("Message:", StringComparison.Ordinal));
        int logger = IndexOf(lines, l => l.Kind == LineKind.Field && l.Scope == "logger");
        int message = IndexOf(lines, l => l.Kind == LineKind.Message);
        Assert.True(logger < message, "The message follows the logger field.");
        Assert.Equal("message", lines[message].Scope);
    }

    [Fact]
    public void The_message_value_is_analyzed_as_a_stack_trace_under_its_field_label()
    {
        IReadOnlyList<DetailLine> lines = Document().Lines;

        DetailLine header = lines.Single(l => l.Text.TrimStart().StartsWith("System.InvalidOperationException:", StringComparison.Ordinal));
        Assert.Equal(9, header.Indent);
        DetailLine runner = lines.Single(l => l.Text.Contains("SheetKraft.Engine.Runner.Execute", StringComparison.Ordinal));
        Assert.Equal(9, runner.Indent);
        Assert.All(lines.Where(l => l.Kind == LineKind.Message), line => Assert.Equal("message", line.Scope));
        Assert.Null(lines.First(l => l.Kind == LineKind.Message).Value);
    }

    [Fact]
    public void Exception_header_and_inner_header_in_the_message_are_styled()
    {
        IReadOnlyList<DetailLine> lines = Document().Lines;

        DetailLine header = lines.Single(l => l.Text.TrimStart().StartsWith("System.InvalidOperationException:", StringComparison.Ordinal));
        Assert.Equal(
        [
            (SpanStyle.ExceptionType, "System.InvalidOperationException"),
            (SpanStyle.Punctuation, ": "),
            (SpanStyle.ExceptionMessage, "Sheet not found"),
        ], DetailRig.SpansOf(header));

        DetailLine inner = lines.Single(l => l.Text.Contains("---> System.IO.IOException", StringComparison.Ordinal));
        Assert.Contains((SpanStyle.ExceptionType, "System.IO.IOException"), DetailRig.SpansOf(inner));
        Assert.Contains((SpanStyle.ExceptionMessage, "locked"), DetailRig.SpansOf(inner));
    }

    [Fact]
    public void Application_frame_is_styled_and_its_directory_dimmed()
    {
        DetailLine frame = Document().Lines.Single(l => l.Text.Contains("SheetKraft.Engine.Runner.Execute", StringComparison.Ordinal));

        Assert.Contains((SpanStyle.Method, "SheetKraft.Engine.Runner.Execute"), DetailRig.SpansOf(frame));
        Assert.Contains((SpanStyle.Directory, @"C:\src\Engine\"), DetailRig.SpansOf(frame));
        Assert.Contains((SpanStyle.FileLocation, "Runner.cs:line 88"), DetailRig.SpansOf(frame));
    }

    [Fact]
    public void Three_framework_frames_get_a_fold_marker_above_them_and_stay_visible_by_default()
    {
        DetailDocument doc = Document();

        Assert.Equal(["fold:message#3"], doc.FoldIds);
        Assert.True(doc.IsExpanded("fold:message#3"));

        int fold = IndexOf(doc.Lines, l => l.Kind == LineKind.Fold);
        DetailLine marker = doc.Lines[fold];
        Assert.Equal("… 3 framework frames", marker.Text.TrimStart());
        Assert.Equal(9, marker.Indent);
        Assert.Equal("fold:message#3", marker.NodeId);
        Assert.Equal<(SpanStyle, string)>([(SpanStyle.FoldMarker, "… 3 framework frames")], DetailRig.SpansOf(marker));

        Assert.Equal(FirstSystemFrame.Trim(), doc.Lines[fold + 1].Text.Trim());
        Assert.True(doc.Lines[fold + 1].Spans.Single().Style == SpanStyle.Framework);
        Assert.Contains(doc.Lines, l => l.Text.Contains("RunInternal", StringComparison.Ordinal));
        Assert.Contains(doc.Lines, l => l.Text.Contains("ExecutionContext.Run(", StringComparison.Ordinal));
    }

    [Fact]
    public void Collapsing_a_fold_removes_its_frames_and_keeps_the_marker()
    {
        DetailOptions options = Document().Toggle("fold:message#3");
        DetailDocument doc = Document(options);

        Assert.False(doc.IsExpanded("fold:message#3"));
        Assert.Contains(doc.Lines, l => l.Kind == LineKind.Fold && l.NodeId == "fold:message#3");
        Assert.DoesNotContain(doc.Lines, l => l.Text.Contains("System.Threading", StringComparison.Ordinal));
        Assert.Contains(doc.Lines, l => l.Text.Contains("SheetKraft.Engine.Runner.Execute", StringComparison.Ordinal));
        Assert.Contains(doc.Lines, l => l.Text.Contains("---> System.IO.IOException", StringComparison.Ordinal));
    }

    [Fact]
    public void Toggle_all_folds_collapses_when_all_are_expanded_and_expands_when_none_are()
    {
        DetailDocument expanded = Document();

        DetailOptions collapsed = expanded.ToggleAllFolds();
        Assert.False(Document(collapsed).IsExpanded("fold:message#3"));
        Assert.DoesNotContain(Document(collapsed).Lines, l => l.Text.Contains("RunInternal", StringComparison.Ordinal));

        DetailOptions reopened = Document(collapsed).ToggleAllFolds();
        Assert.True(Document(reopened).IsExpanded("fold:message#3"));
        Assert.Contains(Document(reopened).Lines, l => l.Text.Contains("RunInternal", StringComparison.Ordinal));
    }

    private static int IndexOf(IReadOnlyList<DetailLine> lines, Func<DetailLine, bool> predicate)
    {
        int index = -1;
        for (int i = 0; i < lines.Count && index < 0; i++)
        {
            if (predicate(lines[i]))
            {
                index = i;
            }
        }

        Assert.True(index >= 0, "no line matched");
        return index;
    }
}
