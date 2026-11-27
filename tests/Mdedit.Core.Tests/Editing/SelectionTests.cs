using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>Replacing or deleting a selection inside one block, and the selections that are refused.</summary>
public sealed class SelectionTests
{
    [Fact]
    public void Deleting_a_selection_inside_a_block_removes_its_text()
    {
        Assert.Equal(new EditCase.Outcome("ad", 1), EditCase.Replace("ab cd", 1, 4, ""));
    }

    [Fact]
    public void Deleting_a_selection_across_spans_removes_the_emptied_spans_with_their_delimiters()
    {
        // The emphasis "*a*" and the strong "**b**" have no text left, so their delimiters go; the spaces between go too, since they lead the text.
        Assert.Equal(new EditCase.Outcome("c", 0), EditCase.Replace("*a* **b** c", 1, 9, ""));
    }

    [Fact]
    public void Deleting_part_of_a_span_keeps_its_delimiters_balanced()
    {
        Assert.Equal(new EditCase.Outcome("**a**  *c*", 6), EditCase.Replace("**a** b *c*", 6, 7, ""));
    }

    [Fact]
    public void Replacing_a_selection_writes_the_typed_text_where_the_selection_started()
    {
        Assert.Equal(new EditCase.Outcome("x cd", 1), EditCase.Replace("ab cd", 0, 2, "x"));
    }

    [Fact]
    public void Replacing_a_selection_escapes_the_typed_text_as_typing_there_would()
    {
        Assert.Equal(new EditCase.Outcome("\\*b", 2), EditCase.Replace("ab", 0, 1, "*"));
    }

    [Fact]
    public void Replacing_a_selection_inside_a_code_span_is_raw()
    {
        Assert.Equal(new EditCase.Outcome("`a*c`", 3), EditCase.Replace("`abc`", 2, 3, "*"));
    }

    [Fact]
    public void A_selection_end_before_its_start_is_the_same_selection()
    {
        Assert.Equal(new EditCase.Outcome("ad", 1), EditCase.Replace("ab cd", 4, 1, ""));
    }

    [Fact]
    public void A_selection_across_blocks_is_refused_until_M6c()
    {
        Assert.Equal(EditMessages.AcrossBlocks, EditCase.Refusal(ViewEdit.ReplaceSelection(RenderDocument.Parse("a\n\nb"), 0, 3, "")));
    }

    [Fact]
    public void A_selection_across_a_hard_line_break_is_refused_until_M6b()
    {
        Assert.Equal(EditMessages.AcrossLines, EditCase.Refusal(ViewEdit.ReplaceSelection(RenderDocument.Parse("a  \nb"), 0, 5, "")));
    }

    [Fact]
    public void An_empty_selection_with_no_text_is_refused()
    {
        Assert.Equal("Nothing is selected", EditCase.Refusal(ViewEdit.ReplaceSelection(RenderDocument.Parse("ab"), 1, 1, "")));
    }

    [Fact]
    public void A_selection_in_a_table_is_refused_until_M6c()
    {
        const string source = "| a | b |\n|---|---|\n| 1 | 2 |\n";

        Assert.Equal(EditMessages.Table, EditCase.Refusal(ViewEdit.ReplaceSelection(RenderDocument.Parse(source), source.IndexOf('1'), source.IndexOf('2'), "")));
    }
}
