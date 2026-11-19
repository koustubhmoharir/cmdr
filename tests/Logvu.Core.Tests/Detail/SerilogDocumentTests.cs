using System.Text.Json.Nodes;
using Logvu.Core.Detail;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Detail;

/// <summary>A Serilog JSON event: the rendered message line, the multi-line Exception, and the default expansion of the tree.</summary>
public sealed class SerilogDocumentTests
{
    /// <summary>
    /// The fixture's failed-order event, with a multi-line exception of three consecutive framework frames, and a <c>Context</c> object
    /// whose <c>Inner</c> object holds an error (so it is expanded by the error path) beside an unrelated <c>Tags</c> object.
    /// </summary>
    internal static string Event()
    {
        string fixture = DetailRig.JsonEvents("serilog.json").Single(line => line.Contains("Failed to process", StringComparison.Ordinal));
        var root = (JsonObject)JsonNode.Parse(fixture)!;
        root["Exception"] = "System.InvalidOperationException: boom\r\n"
            + "   at SheetKraft.Orders.Pipeline.Run(Order order) in C:\\src\\Orders\\Pipeline.cs:line 42\r\n"
            + "   at System.Linq.Enumerable.Select(IEnumerable source, Func selector)\r\n"
            + "   at System.Collections.Generic.List`1.ForEach(Action action)\r\n"
            + "   at Microsoft.Extensions.Hosting.Host.Start()\r\n"
            + "   --- End of inner exception stack trace ---";
        var properties = (JsonObject)root["Properties"]!;
        properties["Context"] = new JsonObject
        {
            ["Inner"] = new JsonObject
            {
                ["ExceptionDetail"] = new JsonObject { ["Type"] = "System.TimeoutException" },
                ["Bag"] = new JsonObject { ["attempt"] = 3 },
            },
            ["Tags"] = new JsonObject { ["tier"] = "gold" },
        };
        return root.ToJsonString();
    }

    private static DetailDocument Document(DetailOptions? options = null) =>
        DetailRig.Build(DetailRig.Loaded(Event(), BuiltInSchemes.SerilogJson), options: options);

    private static DetailDocument Of(string text) => DetailRig.Build(DetailRig.Loaded(text, BuiltInSchemes.SerilogJson));

    [Fact]
    public void The_rendered_message_is_a_message_line_and_the_exception_has_no_summary_line()
    {
        IReadOnlyList<DetailLine> lines = Document().Lines;

        DetailLine message = lines.Single(l => l.Kind == LineKind.Message && l.Text.StartsWith("Message: ", StringComparison.Ordinal));
        Assert.Equal("Message: Failed to process 42 for {\"Name\":\"Asha\"} {literal}", message.Text);
        Assert.Equal("message", message.Scope);
        Assert.DoesNotContain(lines, l => l.Text.StartsWith("Error: ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_document_starts_with_the_message_and_not_a_header()
    {
        DetailLine first = Document().Lines[0];

        Assert.Equal(LineKind.Message, first.Kind);
        Assert.Equal("Message: Failed to process 42 for {\"Name\":\"Asha\"} {literal}", first.Text);
    }

    [Fact]
    public void Event_with_a_rendered_message_starts_with_the_tree_and_has_no_message_line()
    {
        string text = DetailRig.JsonEvents("serilog.json").Single(l => l.Contains("\"RenderedMessage\":\"Retry 2 of 5\"", StringComparison.Ordinal));

        IReadOnlyList<DetailLine> lines = Of(text).Lines;

        Assert.Equal(LineKind.Json, lines[0].Kind);
        Assert.StartsWith("  Timestamp: ", lines[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Kind == LineKind.Message);
        Assert.Contains(lines, l => l.Text.StartsWith("  RenderedMessage: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Template_that_renders_differently_starts_with_the_message_then_a_blank_line_then_the_tree()
    {
        string text = DetailRig.JsonEvents("serilog.json").Single(l => l.Contains("Request {Method:l} took", StringComparison.Ordinal));

        IReadOnlyList<DetailLine> lines = Of(text).Lines;

        Assert.Equal(LineKind.Message, lines[0].Kind);
        Assert.Equal("Message: Request GET took 14.2 ms", lines[0].Text);
        Assert.Equal("message", lines[0].Scope);
        Assert.Equal(string.Empty, lines[1].Text);
        Assert.Equal(LineKind.Json, lines[2].Kind);
        Assert.StartsWith("  Timestamp: ", lines[2].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Template_without_holes_renders_to_itself_so_there_is_no_message_line()
    {
        const string text =
            """{"Timestamp":"2026-11-18T10:20:30+05:30","Level":"Information","MessageTemplate":"Server started","Properties":{"SourceContext":"SampleDotNetApp.Host"}}""";

        IReadOnlyList<DetailLine> lines = Of(text).Lines;

        Assert.Equal(LineKind.Json, lines[0].Kind);
        Assert.StartsWith("  Timestamp: ", lines[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Kind == LineKind.Message);
    }

    [Fact]
    public void Exception_is_a_multi_line_value_with_stack_styling_and_its_scope()
    {
        IReadOnlyList<DetailLine> lines = Document().Lines;

        DetailLine first = lines.Single(l => l.Text.StartsWith("  Exception: ", StringComparison.Ordinal));
        Assert.Equal(LineKind.Json, first.Kind);
        Assert.Equal("Exception", first.Scope);
        Assert.Equal(13, first.Indent);

        DetailLine frame = lines.Single(l => l.Text.Contains("Pipeline.Run(", StringComparison.Ordinal));
        Assert.Equal(13, frame.Indent);
        Assert.Equal("Exception", frame.Scope);
        Assert.Contains((SpanStyle.Method, "SheetKraft.Orders.Pipeline.Run"), DetailRig.SpansOf(frame));
        Assert.Contains((SpanStyle.Directory, @"C:\src\Orders\"), DetailRig.SpansOf(frame));
        Assert.Contains((SpanStyle.FileLocation, "Pipeline.cs:line 42"), DetailRig.SpansOf(frame));
    }

    [Fact]
    public void Three_consecutive_framework_frames_of_the_exception_fold_under_a_marker()
    {
        DetailDocument doc = Document();

        Assert.Contains("fold:Exception#2", doc.FoldIds);
        int marker = doc.Lines.ToList().FindIndex(l => l.NodeId == "fold:Exception#2");
        Assert.Equal(LineKind.Fold, doc.Lines[marker].Kind);
        Assert.Equal("… 3 framework frames", doc.Lines[marker].Text.TrimStart());
        Assert.Contains("System.Linq.Enumerable.Select", doc.Lines[marker + 1].Text);
        Assert.Contains("Microsoft.Extensions.Hosting.Host.Start", doc.Lines[marker + 3].Text);

        DetailDocument collapsed = Document(doc.Toggle("fold:Exception#2"));
        Assert.DoesNotContain(collapsed.Lines, l => l.Text.Contains("Enumerable.Select", StringComparison.Ordinal));
        Assert.Contains(collapsed.Lines, l => l.Text.Contains("Pipeline.Run(", StringComparison.Ordinal));
    }

    [Fact]
    public void Properties_is_expanded_and_its_depth_two_members_are_expanded_by_default()
    {
        DetailDocument doc = Document();

        Assert.True(doc.IsExpanded("Properties"));
        Assert.True(doc.IsExpanded("Properties.Customer"));
        Assert.True(doc.IsExpanded("Properties.Scope"));
        Assert.True(doc.IsExpanded("Properties.ExceptionDetail"));
        Assert.Contains(doc.Lines, l => l.Text == "▾ Properties");
        Assert.Contains(doc.Lines, l => l.Text == "    OrderId: 42");
    }

    [Fact]
    public void Error_path_expands_a_depth_three_node_and_its_error_key_while_unrelated_depth_three_nodes_collapse()
    {
        DetailDocument doc = Document();

        Assert.True(doc.IsExpanded("Properties.Context"));
        Assert.True(doc.IsExpanded("Properties.Context.Inner"));
        Assert.True(doc.IsExpanded("Properties.Context.Inner.ExceptionDetail"));
        Assert.False(doc.IsExpanded("Properties.Context.Inner.Bag"));
        Assert.False(doc.IsExpanded("Properties.Context.Tags"));

        Assert.Contains(doc.Lines, l => l.Text.Contains("▸ Tags", StringComparison.Ordinal));
        Assert.DoesNotContain(doc.Lines, l => l.Text.Contains("tier:", StringComparison.Ordinal));
        Assert.Contains(doc.Lines, l => l.Text.Contains("▸ Bag", StringComparison.Ordinal));
        Assert.Contains(doc.Lines, l => l.Text.Contains("Type: \"System.TimeoutException\"", StringComparison.Ordinal));
        Assert.DoesNotContain(doc.Lines, l => l.Text.Contains("attempt: 3", StringComparison.Ordinal));
    }

    [Fact]
    public void Toggling_a_node_hides_its_children_and_toggling_again_restores_them()
    {
        DetailOptions options = Document().Toggle("Properties");
        DetailDocument collapsed = Document(options);

        Assert.False(collapsed.IsExpanded("Properties"));
        Assert.Contains(collapsed.Lines, l => l.Text == "▸ Properties");
        Assert.DoesNotContain(collapsed.Lines, l => l.Text == "    OrderId: 42");

        DetailDocument restored = Document(collapsed.Toggle("Properties"));
        Assert.Contains(restored.Lines, l => l.Text == "    OrderId: 42");
    }

    [Fact]
    public void Ids_are_stable_across_rebuilds_and_are_the_json_paths()
    {
        DetailDocument first = Document();
        DetailDocument second = Document(first.Toggle("Properties.Context.Inner.Bag"));

        Assert.Equal(first.NodeIds, second.NodeIds);
        Assert.Equal(first.FoldIds, second.FoldIds);
        Assert.Contains("Properties", first.NodeIds);
        Assert.Contains("Properties.Scope", first.NodeIds);
        Assert.Contains("Properties.Context.Inner.Bag", first.NodeIds);
        Assert.True(second.Options.Toggled.Contains("Properties.Context.Inner.Bag"));
        Assert.True(second.IsExpanded("Properties.Context.Inner.Bag"));
    }

    [Fact]
    public void Scalars_are_styled_as_json_and_their_scope_is_the_dotted_path()
    {
        DetailLine sourceContext = Document().Lines.Single(l => l.Text.Contains("SourceContext:", StringComparison.Ordinal));

        Assert.Equal("Properties.SourceContext", sourceContext.Scope);
        Assert.Equal(LineKind.Json, sourceContext.Kind);
        Assert.Contains((SpanStyle.JsonKey, "SourceContext"), DetailRig.SpansOf(sourceContext));
        Assert.Contains((SpanStyle.JsonString, "\"SampleDotNetApp.Orders\""), DetailRig.SpansOf(sourceContext));
    }

    [Fact]
    public void Node_markers_show_their_state_and_no_counts()
    {
        IReadOnlyList<DetailLine> lines = Document().Lines;

        Assert.Contains(lines, l => l.Text == "  ▾ Customer");
        Assert.Contains(lines, l => l.Text == "  ▾ Scope");
        Assert.Contains(lines, l => l.Text == "  ▾ ExceptionDetail");
    }

}
