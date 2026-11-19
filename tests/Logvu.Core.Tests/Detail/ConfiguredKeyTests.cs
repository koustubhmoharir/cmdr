using Logvu.Core.Config;
using Logvu.Core.Detail;
using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Detail;

/// <summary>
/// The labels of values the timeline shows as columns or message fields: <see cref="SpanStyle.ConfiguredKey"/> for the label of the value
/// at a configured path, and the ordinary label style for every other.
/// </summary>
public sealed class ConfiguredKeyTests
{
    private const string Slog =
        "{\"time\":\"2026-11-18T10:20:30.5+05:30\",\"level\":\"ERROR\",\"msg\":\"request failed\",\"status\":500,\"status\":503,"
        + "\"req\":{\"path\":\"/a\",\"method\":\"GET\"},\"tags\":[],\"meta\":{},"
        + "\"err\":{\"message\":\"boom\",\"attrs\":[{\"code\":\"E1\",\"detail\":\"x\"},{\"code\":\"E2\"}],"
        + "\"stack\":[\"main.handler (/src/app/main.go:42)\",\"main.main (/src/app/main.go:9)\"]}}";

    private const string Serilog =
        "{\"Timestamp\":\"2026-11-18T10:20:30.1234567+05:30\",\"Level\":\"Error\",\"MessageTemplate\":\"Order {OrderId} failed\","
        + "\"Properties\":{\"OrderId\":42}}";

    private static DetailDocument Json(string text, LogvuConfig config, ILogScheme? scheme = null) =>
        DetailRig.Build(DetailRig.Loaded(text, scheme ?? BuiltInSchemes.SlogJson), config);

    private static DetailLine Scoped(DetailDocument document, string scope) => document.FullLines.Single(line => line.Scope == scope);

    /// <summary>The style of the span whose text is <paramref name="label"/>, on the line.</summary>
    private static SpanStyle LabelStyle(DetailLine line, string label) =>
        line.Spans.Single(span => line.Text.Substring(span.Start, span.Length) == label).Style;

    private static LogvuConfig Paths(IReadOnlyList<string>? columns = null, IReadOnlyList<string>? messageFields = null) =>
        DetailRig.Config(
            columns: columns?.Select(path => new ColumnSpec(path)).ToList(),
            messageFields: messageFields);

    [Fact]
    public void Keys_of_configured_column_and_message_field_paths_are_configured_keys_and_the_rest_keep_their_style()
    {
        LogvuConfig config = Paths(["status", "req.path"], ["err.attrs[0].code"]);
        DetailDocument document = Json(Slog, config);

        // The column shows the last of repeated names, as JsonFieldPath resolves it, so that one is marked.
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "status"), "status"));
        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "status#2"), "status"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "req"), "req"));
        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "req.path"), "path"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "req.method"), "method"));
        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "err.attrs[0].code"), "code"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "err.attrs[1].code"), "code"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "err.attrs[0].detail"), "detail"));
    }

    [Fact]
    public void Index_labels_of_the_item_at_a_configured_path_are_configured_keys_and_other_items_stay_muted()
    {
        DetailDocument document = Json(Slog, Paths(messageFields: ["err.attrs[0]"]));

        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "err.attrs[0]"), "[1]"));
        Assert.Equal(SpanStyle.Muted, LabelStyle(Scoped(document, "err.attrs[1]"), "[2]"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "err.attrs"), "attrs"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "err.attrs[0].code"), "code"));
    }

    [Fact]
    public void Nodes_and_empty_nodes_at_configured_paths_are_configured_keys()
    {
        DetailDocument document = Json(Slog, Paths(["req", "tags", "meta"]));

        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "req"), "req"));
        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "tags"), "tags"));
        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "meta"), "meta"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "req.path"), "path"));
    }

    [Fact]
    public void Multi_line_value_head_at_a_configured_path_is_a_configured_key()
    {
        const string text =
            "{\"time\":\"2026-11-18T10:20:30.5+05:30\",\"level\":\"ERROR\",\"msg\":\"failed\",\"trace\":\"first\\nsecond\"}";
        DetailDocument document = Json(text, Paths(["trace"]));

        DetailLine head = document.FullLines.First(line => line.Scope == "trace");
        Assert.StartsWith("  trace: first", head.Text);
        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(head, "trace"));
    }

    [Fact]
    public void Go_stack_item_labels_at_a_configured_path_are_configured_keys()
    {
        DetailDocument document = Json(Slog, Paths(messageFields: ["err.stack[1]"]));

        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "err.stack[1]"), "[2]"));
        Assert.Equal(SpanStyle.Muted, LabelStyle(Scoped(document, "err.stack[0]"), "[1]"));
    }

    [Fact]
    public void Only_the_last_of_repeated_names_and_no_name_with_a_dot_match_a_configured_path_with_their_subtrees()
    {
        const string text =
            "{\"time\":\"2026-11-18T10:20:30.5+05:30\",\"level\":\"ERROR\",\"msg\":\"x\","
            + "\"req\":{\"path\":\"/a\"},\"req\":{\"path\":\"/b\"},\"a.b\":{\"c\":1}}";
        DetailDocument document = Json(text, Paths(["req.path", "a.b.c"]));

        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "req.path"), "path"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "req#2"), "req"));
        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "req#2.path"), "path"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "a.b"), "a.b"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "a.b.c"), "c"));
    }

    [Fact]
    public void Repeated_names_at_two_levels_match_through_the_last_occurrence_of_each()
    {
        const string text =
            "{\"time\":\"2026-11-18T10:20:30.5+05:30\",\"level\":\"ERROR\",\"msg\":\"x\","
            + "\"o\":{\"r\":{\"p\":1},\"r\":{\"p\":2}},\"o\":{\"r\":{\"p\":3},\"r\":{\"p\":4}}}";
        DetailDocument document = Json(text, Paths(["o.r.p"]));

        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "o#2.r#2.p"), "p"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "o#2.r.p"), "p"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "o.r#2.p"), "p"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "o.r.p"), "p"));
    }

    [Fact]
    public void A_configured_path_is_written_in_its_canonical_form_so_leading_zero_indices_match()
    {
        DetailDocument document = Json(Slog, Paths(messageFields: ["err.attrs[00].code"]));

        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "err.attrs[0].code"), "code"));
    }

    [Fact]
    public void Text_event_fields_named_by_a_column_are_configured_keys_and_message_fields_do_not_apply()
    {
        DetailDocument document = DetailRig.Build(
            DetailRig.Loaded(TextEventDocumentTests.ErrorEvent(), BuiltInSchemes.Log4NetCore),
            DetailRig.Config(columns: [new ColumnSpec("logger")], messageFields: ["thread"]));

        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(Scoped(document, "logger"), "logger"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "thread"), "thread"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "level"), "level"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "time"), "time"));
    }

    [Fact]
    public void A_message_column_colours_the_message_label_of_a_text_event()
    {
        DetailDocument document = DetailRig.Build(
            DetailRig.Loaded(TextEventDocumentTests.ErrorEvent(), BuiltInSchemes.Log4NetCore),
            DetailRig.Config(columns: [new ColumnSpec("message")]));

        DetailLine message = document.FullLines.First(line => line.Text.StartsWith("message: ", StringComparison.Ordinal));
        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(message, "message"));
        Assert.Equal(SpanStyle.JsonKey, LabelStyle(Scoped(document, "logger"), "logger"));
    }

    [Fact]
    public void A_message_column_colours_the_rendered_message_label_of_a_serilog_event_and_its_absence_leaves_it_bold()
    {
        DetailDocument configured = Json(Serilog, Paths(["message"]), BuiltInSchemes.SerilogJson);
        DetailDocument plain = Json(Serilog, Paths(), BuiltInSchemes.SerilogJson);

        DetailLine ofConfigured = configured.FullLines.First(line => line.Text.StartsWith("Message: ", StringComparison.Ordinal));
        DetailLine ofPlain = plain.FullLines.First(line => line.Text.StartsWith("Message: ", StringComparison.Ordinal));
        Assert.Equal(SpanStyle.ConfiguredKey, LabelStyle(ofConfigured, "Message"));
        Assert.Equal(SpanStyle.FieldName, LabelStyle(ofPlain, "Message"));
    }

    [Fact]
    public void Without_configured_paths_no_label_is_a_configured_key_and_the_document_is_as_before()
    {
        DetailDocument none = Json(Slog, Paths());
        DetailDocument unrelated = Json(Slog, Paths(["absent", "req.absent"], ["nothing[0]"]));

        Assert.DoesNotContain(
            none.FullLines,
            line => line.Spans.Any(span => span.Style == SpanStyle.ConfiguredKey));
        Assert.Equal(DetailRig.Rows(none.FullLines), DetailRig.Rows(unrelated.FullLines));
    }
}
