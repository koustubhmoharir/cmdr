using Markdig.Extensions.Alerts;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Mdedit.Core;

namespace Mdedit.Core.Tests;

public sealed class MdPipelineTests
{
    [Fact]
    public void Pipe_table_parses_to_a_table()
    {
        var document = MdPipeline.Parse("| a | b |\n|---|---|\n| 1 | 2 |\n");

        Assert.Single(document.Descendants<Table>());
    }

    [Fact]
    public void Task_list_item_gives_a_checked_task_list_inline()
    {
        var document = MdPipeline.Parse("- [x] done\n");

        var task = Assert.Single(document.Descendants<TaskList>());
        Assert.True(task.Checked);
    }

    [Fact]
    public void Double_tilde_gives_a_strikethrough_emphasis_with_delimiter_count_2()
    {
        var document = MdPipeline.Parse("~~gone~~\n");

        var emphasis = Assert.Single(document.Descendants<EmphasisInline>());
        Assert.Equal('~', emphasis.DelimiterChar);
        Assert.Equal(2, emphasis.DelimiterCount);
    }

    [Fact]
    public void Angle_bracket_autolink_gives_an_autolink_inline()
    {
        var document = MdPipeline.Parse("<https://example.com>\n");

        var autolink = Assert.Single(document.Descendants<AutolinkInline>());
        Assert.Equal("https://example.com", autolink.Url);
    }

    [Fact]
    public void Bare_url_gives_a_link_inline_marked_as_autolink()
    {
        var document = MdPipeline.Parse("https://example.com\n");

        var link = Assert.Single(document.Descendants<LinkInline>());
        Assert.True(link.IsAutoLink);
        Assert.Equal("https://example.com", link.Url);
    }

    [Fact]
    public void Note_alert_gives_an_alert_block()
    {
        var document = MdPipeline.Parse("> [!NOTE]\n> text\n");

        Assert.Single(document.Descendants<AlertBlock>());
    }

    [Fact]
    public void Grid_table_does_not_give_a_table()
    {
        var document = MdPipeline.Parse("+---+\n| a |\n+---+\n");

        Assert.Empty(document.Descendants<Table>());
    }

    [Fact]
    public void Quotes_stay_literal_text()
    {
        var document = MdPipeline.Parse("\"quotes\"\n");

        var literal = Assert.Single(document.Descendants<LiteralInline>());
        Assert.Equal("\"quotes\"", literal.Content.ToString());
    }

    [Fact]
    public void Literal_inline_span_selects_its_text_in_the_source()
    {
        const string source = "Some *text* here\n";
        var document = MdPipeline.Parse(source);

        var literal = document.Descendants<LiteralInline>().First();
        Assert.Equal("Some ", literal.Content.ToString());
        Assert.Equal(0, literal.Span.Start);
        Assert.Equal(4, literal.Span.End);
        Assert.Equal("Some ", source.Substring(literal.Span.Start, literal.Span.End - literal.Span.Start + 1));
    }
}
