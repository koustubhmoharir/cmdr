using Logvu.Core.Detail;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;

namespace Logvu.Core.Tests.Detail;

/// <summary>The raw view, the unavailable notice, and the truncation notice.</summary>
public sealed class RawUnavailableTests
{
    [Fact]
    public void Raw_view_lines_are_exactly_the_event_text_lines()
    {
        string text = TextEventDocumentTests.ErrorEvent();

        DetailDocument doc = DetailRig.Build(DetailRig.Loaded(text, BuiltInSchemes.Log4NetCore), options: new DetailOptions { Raw = true });

        Assert.Equal(text.Split('\n'), doc.Lines.Select(l => l.Text));
        Assert.All(doc.Lines, l => Assert.Equal(LineKind.Raw, l.Kind));
        Assert.All(doc.Lines, l => Assert.Empty(l.Spans));
    }

    [Fact]
    public void Raw_view_of_a_json_event_is_its_one_line()
    {
        string text = DetailRig.JsonEvents("serilog.json")[0];

        DetailDocument doc = DetailRig.Build(DetailRig.Loaded(text, BuiltInSchemes.SerilogJson), options: new DetailOptions { Raw = true });

        Assert.Equal([text], doc.Lines.Select(l => l.Text));
    }

    [Fact]
    public void The_raw_view_keeps_the_ids_of_the_structured_view_for_toggling()
    {
        DetailDocument raw = DetailRig.Build(
            DetailRig.Loaded(SerilogDocumentTests.Event(), BuiltInSchemes.SerilogJson),
            options: new DetailOptions { Raw = true });
        DetailDocument structured = DetailRig.Build(DetailRig.Loaded(SerilogDocumentTests.Event(), BuiltInSchemes.SerilogJson));

        Assert.Equal(structured.NodeIds, raw.NodeIds);
        Assert.Equal(structured.FoldIds, raw.FoldIds);
    }

    [Fact]
    public void Text_that_starts_with_a_marker_glyph_is_shown_unchanged()
    {
        string text = "INFO  2026-11-18 10:20:30,123 [12] SheetKraft.Engine.Runner - \u25B8 not a node";

        DetailDocument doc = DetailRig.Build(DetailRig.Loaded(text, BuiltInSchemes.Log4NetCore));

        Assert.Equal("message: \u25B8 not a node", doc.FullLines.Single(l => l.Kind == LineKind.Message).Text);
        Assert.Equal("message: \u25B8 not a node", doc.Lines.Single(l => l.Kind == LineKind.Message).Text);
    }

    [Fact]
    public void Unavailable_detail_is_a_single_notice_with_the_reason()
    {
        var detail = new EventDetail.Unavailable(new RowKey(1, 0), "Source file expired");

        DetailDocument doc = DetailRig.Build(detail);

        DetailLine line = Assert.Single(doc.Lines);
        Assert.Equal(LineKind.Notice, line.Kind);
        Assert.Equal("Source file expired", line.Text);
        Assert.Empty(doc.NodeIds);
        Assert.Empty(doc.FoldIds);
        Assert.False(doc.Truncated);
    }

    [Fact]
    public void Unavailable_detail_is_the_same_in_raw_view()
    {
        var detail = new EventDetail.Unavailable(new RowKey(1, 0), "boom while reading");

        DetailDocument doc = DetailRig.Build(detail, options: new DetailOptions { Raw = true });

        Assert.Equal(["boom while reading"], doc.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Truncated_event_ends_with_a_notice_in_the_structured_view()
    {
        DetailDocument doc = DetailRig.Build(
            DetailRig.Loaded(TextEventDocumentTests.ErrorEvent(), BuiltInSchemes.Log4NetCore, truncated: true));

        Assert.True(doc.Truncated);
        DetailLine last = doc.Lines[^1];
        Assert.Equal(LineKind.Notice, last.Kind);
        Assert.Equal($"… truncated at {EventText.DefaultCap} bytes", last.Text);
    }

    [Fact]
    public void Truncated_event_ends_with_the_same_notice_in_raw_view()
    {
        DetailDocument doc = DetailRig.Build(
            DetailRig.Loaded(DetailRig.JsonEvents("serilog.json")[0], BuiltInSchemes.SerilogJson, truncated: true),
            options: new DetailOptions { Raw = true });

        Assert.Equal(LineKind.Notice, doc.Lines[^1].Kind);
        Assert.Equal($"… truncated at {EventText.DefaultCap} bytes", doc.Lines[^1].Text);
    }

    [Fact]
    public void Event_that_is_not_truncated_has_no_notice()
    {
        DetailDocument doc = DetailRig.Build(DetailRig.Loaded(DetailRig.JsonEvents("serilog.json")[0], BuiltInSchemes.SerilogJson));

        Assert.False(doc.Truncated);
        Assert.DoesNotContain(doc.Lines, l => l.Kind == LineKind.Notice);
    }
}
