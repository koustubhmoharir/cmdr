using Logvu.Core.Detail;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Detail;

/// <summary>A Go slog JSON event: the tree with no message line, the <c>err</c> object, top-level cause chains and Go stack arrays.</summary>
public sealed class SlogDocumentTests
{
    private const string FoldedStack =
        "{\"time\":\"2026-11-18T10:20:40.000000000+05:30\",\"level\":\"ERROR\",\"msg\":\"handler panic\",\"stack\":["
        + "\"main.run (/src/app/main.go:9)\","
        + "\"net/http.HandlerFunc.ServeHTTP (net/http/server.go:2220)\","
        + "\"net/http.serverHandler.ServeHTTP (net/http/server.go:3301)\","
        + "\"net/http.(*conn).serve (net/http/server.go:2000)\"]}";

    private const string TenItems =
        "{\"time\":\"2026-11-18T10:20:31.5+05:30\",\"level\":\"INFO\",\"msg\":\"m\",\"items\":[1,2,3,4,5,6,7,8,9,10],\"bag\":{\"a\":1}}";

    private static string ErrEvent() => DetailRig.JsonEvents("slog.json").Single(l => l.Contains("\"err\":{", StringComparison.Ordinal));

    private static string CauseEvent() => DetailRig.JsonEvents("slog.json").Single(l => l.Contains("\"msg\":\"retrying\"", StringComparison.Ordinal));

    private static DetailDocument Document(string text, DetailOptions? options = null) =>
        DetailRig.Build(DetailRig.Loaded(text, BuiltInSchemes.SlogJson), options: options);

    [Fact]
    public void The_document_starts_with_the_first_tree_line_and_has_no_message_or_error_line()
    {
        IReadOnlyList<DetailLine> lines = Document(ErrEvent()).Lines;

        Assert.Equal(LineKind.Json, lines[0].Kind);
        Assert.StartsWith("  time: ", lines[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Kind == LineKind.Message);
        Assert.DoesNotContain(lines, l => l.Text.StartsWith("Error: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Err_object_is_an_expanded_node_with_its_attrs_cause_and_stack()
    {
        DetailDocument doc = Document(ErrEvent());

        Assert.Contains("err", doc.NodeIds);
        Assert.Contains("err.attrs", doc.NodeIds);
        Assert.Contains("err.cause", doc.NodeIds);
        Assert.Contains("err.stack", doc.NodeIds);
        Assert.True(doc.IsExpanded("err"));
        Assert.True(doc.IsExpanded("err.attrs"));
        Assert.Contains(doc.Lines, l => l.Text == "▾ err" && l.NodeId == "err");
        Assert.Contains(doc.Lines, l => l.Text == "    message: \"db down\"" && l.Scope == "err.message");
        Assert.Contains(doc.Lines, l => l.Text == "  ▾ attrs");
        Assert.Contains(doc.Lines, l => l.Text == "      table: \"orders\"");
    }

    [Fact]
    public void Cause_array_items_are_labelled_from_one_and_its_strings_are_quoted()
    {
        DetailLine cause = Document(ErrEvent()).Lines.Single(l => l.Scope == "err.cause[0]");

        Assert.Equal("      [1]: \"dial tcp 10.0.0.9:1433: connect: connection refused\"", cause.Text);
        Assert.Contains((SpanStyle.Muted, "[1]"), DetailRig.SpansOf(cause));
        Assert.Contains((SpanStyle.JsonString, "\"dial tcp 10.0.0.9:1433: connect: connection refused\""), DetailRig.SpansOf(cause));
    }

    [Fact]
    public void Go_stack_frames_are_styled_and_the_framework_frame_is_dimmed()
    {
        IReadOnlyList<DetailLine> lines = Document(ErrEvent()).Lines;

        DetailLine handler = lines.Single(l => l.Text.Contains("main.handler", StringComparison.Ordinal));
        Assert.Equal("      [1]: main.handler (/src/app/main.go:42)", handler.Text);
        Assert.Equal("err.stack[0]", handler.Scope);
        Assert.Contains((SpanStyle.Method, "main.handler"), DetailRig.SpansOf(handler));
        Assert.Contains((SpanStyle.Directory, "/src/app/"), DetailRig.SpansOf(handler));
        Assert.Contains((SpanStyle.FileLocation, "main.go:42"), DetailRig.SpansOf(handler));

        DetailLine framework = lines.Single(l => l.Text.Contains("net/http.HandlerFunc.ServeHTTP", StringComparison.Ordinal));
        Assert.Equal<(SpanStyle, string)>(
            [(SpanStyle.Muted, "[2]"), (SpanStyle.Punctuation, ": "), (SpanStyle.Framework, framework.Text[framework.Indent..])],
            DetailRig.SpansOf(framework).Select(s => (s.Style, s.Text.TrimStart())));
    }

    [Fact]
    public void Top_level_cause_items_are_numbered_nodes_with_their_message_and_attrs()
    {
        DetailDocument doc = Document(CauseEvent());
        IReadOnlyList<DetailLine> lines = doc.Lines;

        DetailLine head = lines.Single(l => l.NodeId == "cause[0]");
        Assert.Equal("  ▾ [1]", head.Text);
        Assert.True(doc.IsExpanded("cause[0]"));

        DetailLine message = lines.Single(l => l.Scope == "cause[0].message");
        Assert.Equal("      message: \"inner\"", message.Text);
        Assert.Equal(message.Text.IndexOf("\"inner\"", StringComparison.Ordinal), message.Indent);

        Assert.Contains(lines, l => l.Text == "    ▾ attrs" && l.NodeId == "cause[0].attrs");
        Assert.Contains(lines, l => l.Text == "        attempt: 2" && l.Scope == "cause[0].attrs.attempt");
        Assert.Contains(lines, l => l.Text == "    [2]: \"io: EOF\"" && l.Scope == "cause[1]");
    }

    [Fact]
    public void Top_level_stack_frame_is_a_plain_frame_when_its_package_is_not_a_framework_prefix()
    {
        DetailLine frame = Document(CauseEvent()).Lines.Single(l => l.Text.Contains("pkg.F", StringComparison.Ordinal));

        Assert.Equal("[1]: pkg.F (pkg/f.go:10)", frame.Text.TrimStart());
        Assert.Contains((SpanStyle.Directory, "pkg/"), DetailRig.SpansOf(frame));
        Assert.Contains((SpanStyle.FileLocation, "f.go:10"), DetailRig.SpansOf(frame));
    }

    [Fact]
    public void Consecutive_framework_frames_in_a_go_stack_fold_and_collapse()
    {
        DetailDocument doc = Document(FoldedStack);
        Assert.Equal(["fold:stack#1"], doc.FoldIds);
        int marker = doc.Lines.ToList().FindIndex(l => l.NodeId == "fold:stack#1");
        Assert.Equal("… 3 framework frames", doc.Lines[marker].Text.TrimStart());
        Assert.Contains("net/http.(*conn).serve", doc.Lines[marker + 3].Text);

        DetailDocument collapsed = Document(FoldedStack, doc.Toggle("fold:stack#1"));
        Assert.DoesNotContain(collapsed.Lines, l => l.Text.Contains("net/http.", StringComparison.Ordinal));
        Assert.Contains(collapsed.Lines, l => l.Text.Contains("main.run", StringComparison.Ordinal));
    }

    [Fact]
    public void Go_stack_frames_are_labelled_from_one_and_the_fold_marker_sits_at_the_frame_text_column()
    {
        DetailDocument doc = Document(FoldedStack);
        int marker = doc.Lines.ToList().FindIndex(l => l.NodeId == "fold:stack#1");

        DetailLine run = doc.Lines.Single(l => l.Text.Contains("main.run", StringComparison.Ordinal));
        Assert.Equal("    [1]: main.run (/src/app/main.go:9)", run.Text);
        Assert.Equal("stack[0]", run.Scope);
        Assert.Equal("    [4]: net/http.(*conn).serve (net/http/server.go:2000)", doc.Lines[marker + 3].Text);
        Assert.Equal(run.Text.IndexOf("main.run", StringComparison.Ordinal), doc.Lines[marker].Text.IndexOf('…'));
    }

    [Fact]
    public void Array_labels_are_right_aligned_to_the_digit_width_of_the_array()
    {
        IReadOnlyList<DetailLine> lines = Document(TenItems).Lines;

        Assert.Contains(lines, l => l.Text == "▾ items" && l.NodeId == "items");
        Assert.Contains(lines, l => l.Text == "    [ 1]: 1");
        Assert.Contains(lines, l => l.Text == "    [ 9]: 9");
        Assert.Contains(lines, l => l.Text == "    [10]: 10");
        Assert.Contains(lines, l => l.Text == "▾ bag");
    }

    [Fact]
    public void Index_labels_are_dimmed_and_member_keys_are_not()
    {
        DetailDocument doc = Document(CauseEvent());

        DetailLine item = doc.Lines.Single(l => l.NodeId == "cause[0]");
        Assert.Contains((SpanStyle.Muted, "[1]"), DetailRig.SpansOf(item));
        Assert.DoesNotContain((SpanStyle.JsonKey, "[1]"), DetailRig.SpansOf(item));

        DetailLine scalar = doc.Lines.Single(l => l.Scope == "cause[1]");
        Assert.Contains((SpanStyle.Muted, "[2]"), DetailRig.SpansOf(scalar));
        Assert.Contains((SpanStyle.Punctuation, ": "), DetailRig.SpansOf(scalar));

        DetailLine key = doc.Lines.Single(l => l.Scope == "cause[0].message");
        Assert.Contains((SpanStyle.JsonKey, "message"), DetailRig.SpansOf(key));
    }

    [Fact]
    public void Paths_count_array_items_from_zero_while_the_labels_count_from_one()
    {
        const string text =
            "{\"time\":\"2026-11-18T10:20:31.5+05:30\",\"level\":\"ERROR\",\"msg\":\"m\",\"error\":{\"cause\":[{\"message\":\"inner\"}]}}";
        DetailDocument doc = Document(text);

        Assert.Contains(doc.Lines, l => l.Text == "  ▾ cause" && l.NodeId == "error.cause");
        Assert.Contains(doc.Lines, l => l.Text == "    ▾ [1]" && l.NodeId == "error.cause[0]");
        DetailLine message = doc.Lines.Single(l => l.Scope == "error.cause[0].message");
        Assert.Equal("        message: \"inner\"", message.Text);
    }

    [Fact]
    public void Unparsable_json_line_is_a_notice_and_its_raw_text_is_the_message()
    {
        string text = DetailRig.JsonEvents("slog.json").Single(l => l.EndsWith("\"truncated", StringComparison.Ordinal));

        IReadOnlyList<DetailLine> lines = Document(text).Lines;

        DetailLine notice = lines.Single(l => l.Kind == LineKind.Notice);
        Assert.StartsWith("Not a parsed record: ", notice.Text, StringComparison.Ordinal);
        Assert.Equal(text, lines.Single(l => l.Kind == LineKind.Message && l.Text.StartsWith("Message: ", StringComparison.Ordinal))
            .Text.Substring("Message: ".Length));
    }
}
