using Logvu.Core.Detail;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Detail;

/// <summary>A Go slog JSON event: the tree with no message line, the <c>err</c> object, top-level cause chains and Go stack arrays.</summary>
public sealed class SlogDocumentTests
{
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
        Assert.Contains(doc.Lines, l => l.Text == "▾ err {4}" && l.NodeId == "err");
        Assert.Contains(doc.Lines, l => l.Text == "    message: \"db down\"" && l.Scope == "err.message");
        Assert.Contains(doc.Lines, l => l.Text == "  ▾ attrs {1}");
        Assert.Contains(doc.Lines, l => l.Text == "      table: \"orders\"");
    }

    [Fact]
    public void Cause_array_is_numbered_and_its_strings_are_unquoted_messages()
    {
        DetailLine cause = Document(ErrEvent()).Lines.Single(l => l.Text.Contains("cause[0]:", StringComparison.Ordinal));

        Assert.Equal("      cause[0]: dial tcp 10.0.0.9:1433: connect: connection refused", cause.Text);
        Assert.Equal("err.cause[0]", cause.Scope);
        Assert.Contains((SpanStyle.JsonString, "dial tcp 10.0.0.9:1433: connect: connection refused"), DetailRig.SpansOf(cause));
    }

    [Fact]
    public void Go_stack_frames_are_styled_and_the_framework_frame_is_dimmed()
    {
        IReadOnlyList<DetailLine> lines = Document(ErrEvent()).Lines;

        DetailLine handler = lines.Single(l => l.Text.Contains("main.handler", StringComparison.Ordinal));
        Assert.Equal("err.stack[0]", handler.Scope);
        Assert.Contains((SpanStyle.Method, "main.handler"), DetailRig.SpansOf(handler));
        Assert.Contains((SpanStyle.Directory, "/src/app/"), DetailRig.SpansOf(handler));
        Assert.Contains((SpanStyle.FileLocation, "main.go:42"), DetailRig.SpansOf(handler));

        DetailLine framework = lines.Single(l => l.Text.Contains("net/http.HandlerFunc.ServeHTTP", StringComparison.Ordinal));
        Assert.Equal<(SpanStyle, string)>([(SpanStyle.Framework, framework.Text.TrimStart())], DetailRig.SpansOf(framework).Select(s => (s.Style, s.Text.TrimStart())));
    }

    [Fact]
    public void Top_level_cause_gives_a_numbered_chain_with_attrs()
    {
        DetailDocument doc = Document(CauseEvent());
        IReadOnlyList<DetailLine> lines = doc.Lines;

        DetailLine head = lines.Single(l => l.Text.Contains("cause[0]: inner", StringComparison.Ordinal));
        Assert.Equal("cause[0].message", head.Scope);
        Assert.Equal("cause[0]", head.NodeId);
        Assert.Equal(head.Text.IndexOf("inner", StringComparison.Ordinal), head.Indent);
        Assert.True(doc.IsExpanded("cause[0]"));

        Assert.Contains(lines, l => l.Text == "    ▾ attrs {1}" && l.NodeId == "cause[0].attrs");
        Assert.Contains(lines, l => l.Text == "        attempt: 2" && l.Scope == "cause[0].attrs.attempt");
        Assert.Contains(lines, l => l.Text == "    cause[1]: io: EOF" && l.Scope == "cause[1]");
    }

    [Fact]
    public void Top_level_stack_frame_is_a_plain_frame_when_its_package_is_not_a_framework_prefix()
    {
        DetailLine frame = Document(CauseEvent()).Lines.Single(l => l.Text.Contains("pkg.F", StringComparison.Ordinal));

        Assert.Equal("pkg.F (pkg/f.go:10)", frame.Text.TrimStart());
        Assert.Contains((SpanStyle.Directory, "pkg/"), DetailRig.SpansOf(frame));
        Assert.Contains((SpanStyle.FileLocation, "f.go:10"), DetailRig.SpansOf(frame));
    }

    [Fact]
    public void Consecutive_framework_frames_in_a_go_stack_fold_and_collapse()
    {
        const string text =
            "{\"time\":\"2026-11-18T10:20:40.000000000+05:30\",\"level\":\"ERROR\",\"msg\":\"handler panic\",\"stack\":["
            + "\"main.run (/src/app/main.go:9)\","
            + "\"net/http.HandlerFunc.ServeHTTP (net/http/server.go:2220)\","
            + "\"net/http.serverHandler.ServeHTTP (net/http/server.go:3301)\","
            + "\"net/http.(*conn).serve (net/http/server.go:2000)\"]}";

        DetailDocument doc = Document(text);
        Assert.Equal(["fold:stack#1"], doc.FoldIds);
        int marker = doc.Lines.ToList().FindIndex(l => l.NodeId == "fold:stack#1");
        Assert.Equal("… 3 framework frames", doc.Lines[marker].Text.TrimStart());
        Assert.Contains("net/http.(*conn).serve", doc.Lines[marker + 3].Text);

        DetailDocument collapsed = Document(text, doc.Toggle("fold:stack#1"));
        Assert.DoesNotContain(collapsed.Lines, l => l.Text.Contains("net/http.", StringComparison.Ordinal));
        Assert.Contains(collapsed.Lines, l => l.Text.Contains("main.run", StringComparison.Ordinal));
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
