using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The lines of single blocks: prefixes, wrapping, code and rules, anchors and hard breaks.</summary>
public sealed class BlockLayoutTests
{
    [Fact]
    public void Heading_paragraph_and_heading_are_separated_by_one_blank_line()
    {
        Assert.Equal(new[] { "▾ Title", string.Empty, "  Some text here." }, LayoutDump.Lines("# Title\n\nSome *text* here.\n"));
    }

    [Fact]
    public void Tight_bullets_have_no_blank_lines_and_nested_bullets_use_the_second_glyph()
    {
        Assert.Equal(new[] { "  • a", "  • b", "    ◦ c" }, LayoutDump.Lines("- a\n- b\n  - c\n"));
    }

    [Fact]
    public void Quote_lines_carry_the_bar_and_the_blank_quote_line_keeps_it()
    {
        Assert.Equal(new[] { "  │ quote", "  │ ", "  │ more" }, LayoutDump.Lines("> quote\n>\n> more\n"));
    }

    [Fact]
    public void Ordered_items_show_the_written_number_mapped_to_the_source()
    {
        DocumentLayout layout = LayoutDump.Build("3) x\n4) y\n");

        Assert.Equal(new[] { "  3) x", "  4) y" }, Enumerable.Range(0, layout.LineCount).Select(layout.TextOf));
        Assert.Equal(" @-| @-|3@0/ListMarker|)@1/ListMarker| @-/ListMarker|x@3", LayoutDump.Cells(layout, 0));
    }

    [Fact]
    public void Fenced_code_has_a_header_with_its_language()
    {
        Assert.Equal(new[] { "▾ cs", "  int x;" }, LayoutDump.Lines("```cs\nint x;\n```\n"));
    }

    [Fact]
    public void Fenced_code_without_a_language_has_the_code_header()
    {
        Assert.Equal(new[] { "▾ code", "  abc" }, LayoutDump.Lines("```\nabc\n```\n"));
    }

    [Fact]
    public void Fenced_code_header_label_maps_the_block_start_and_the_rest_of_the_header_stays_synthetic()
    {
        DocumentLayout layout = LayoutDump.Build("```cs\nint x;\n```\n");

        Assert.Equal("▾@-/FoldGlyph| @-/FoldGlyph|c@0/CodeHeader|s@-/CodeHeader", LayoutDump.Cells(layout, 0));
    }

    [Fact]
    public void Indented_code_header_label_maps_the_block_start_and_the_code_starts_after_the_indentation()
    {
        DocumentLayout layout = LayoutDump.Build("    int x;\n");

        Assert.Equal("▾@-/FoldGlyph| @-/FoldGlyph|c@0/CodeHeader|o@-/CodeHeader|d@-/CodeHeader|e@-/CodeHeader", LayoutDump.Cells(layout, 0));
        Assert.Equal(4, layout.Line(1).Cells.First(cell => cell.IsMapped).Offset);
    }

    [Fact]
    public void Words_wrap_at_the_width_and_spaces_that_do_not_fit_are_not_shown()
    {
        // The gutter takes two columns, so each width is two more than the content width the case is about.
        Assert.Equal(new[] { "  aaa bbb", "  ccc" }, LayoutDump.Lines("aaa bbb ccc", 9));
        Assert.Equal(new[] { "  aaa ", "  bbb ", "  ccc" }, LayoutDump.Lines("aaa bbb ccc", 7));
    }

    [Fact]
    public void Code_wraps_at_the_width_and_continuations_start_with_the_arrow()
    {
        Assert.Equal(new[] { "▾ code", "  abcdef", "  ↪ghij" }, LayoutDump.Lines("```\nabcdefghij\n```\n", 8));
    }

    [Fact]
    public void Alert_title_is_its_own_first_line()
    {
        Assert.Equal(new[] { "  │ Note", "  │ hi" }, LayoutDump.Lines("> [!NOTE]\n> hi\n"));
    }

    [Fact]
    public void Alert_title_line_has_the_alert_bar_and_the_title_role()
    {
        DocumentLayout layout = LayoutDump.Build("> [!WARNING]\n> hi\n");

        Assert.Equal("  │ Warning", layout.TextOf(0));
        Assert.Equal(StyleRole.AlertTitle, layout.Line(0).Cells[4].Style.Role);
        Assert.Equal(0, layout.Line(0).Anchor);
    }

    [Fact]
    public void Thematic_break_fills_the_content_width_with_rules()
    {
        Assert.Equal(new[] { "  ─────" }, LayoutDump.Lines("***\n", 7));
    }

    [Fact]
    public void Loose_list_item_puts_a_blank_line_between_its_paragraphs()
    {
        Assert.Equal(new[] { "  • one", string.Empty, "    two" }, LayoutDump.Lines("- one\n\n  two\n"));
    }

    [Fact]
    public void Hard_breaks_start_lines_that_are_not_continuations()
    {
        DocumentLayout layout = LayoutDump.Build("a  \nb\\\nc\n");

        Assert.Equal(new[] { "  a", "  b", "  c" }, Enumerable.Range(0, layout.LineCount).Select(layout.TextOf));
        Assert.All(Enumerable.Range(0, layout.LineCount), line => Assert.False(layout.Line(line).IsContinuation));
    }

    [Fact]
    public void Wrapped_lines_are_continuations_and_the_first_is_not()
    {
        DocumentLayout layout = LayoutDump.Build("aaa bbb ccc", 9);

        Assert.False(layout.Line(0).IsContinuation);
        Assert.True(layout.Line(1).IsContinuation);
    }

    [Fact]
    public void Wide_characters_wrap_by_their_display_width()
    {
        // Each CJK character is two columns: a content width of 5 fits two, so the pairs break after every second one.
        Assert.Equal(new[] { "  中文", "  字符" }, LayoutDump.Lines("中文字符", 7));
    }

    [Fact]
    public void A_zwj_emoji_is_one_cell_of_width_two_and_is_not_split()
    {
        DocumentLayout layout = LayoutDump.Build("ab 👨‍👩‍👧 cd", 6);

        Assert.Equal(new[] { "  ab ", "  👨‍👩‍👧 ", "  cd" }, Enumerable.Range(0, layout.LineCount).Select(layout.TextOf));
        Assert.Equal(2, layout.Line(1).Cells[2].Width);
    }

    [Fact]
    public void Combining_marks_stay_with_their_base_when_wrapping()
    {
        DocumentLayout layout = LayoutDump.Build("abcdéf", 6);

        Assert.Equal(new[] { "  abcd", "  éf" }, Enumerable.Range(0, layout.LineCount).Select(layout.TextOf));
    }

    [Fact]
    public void A_word_longer_than_the_line_breaks_between_graphemes()
    {
        Assert.Equal(new[] { "  abcd", "  efgh", "  ij" }, LayoutDump.Lines("abcdefghij", 6));
    }

    [Fact]
    public void A_width_smaller_than_the_prefix_gives_a_content_width_of_one()
    {
        // The quote bar is two columns, so the content width is clamped to one and each word breaks between cells.
        Assert.Equal(new[] { "  │ a", "  │ b", "  │ c" }, LayoutDump.Lines("> abc\n", 1));
    }

    [Fact]
    public void Crlf_source_lays_out_like_lf_source()
    {
        Assert.Equal(LayoutDump.Lines("# T\n\nSome *text*\nmore\n"), LayoutDump.Lines("# T\r\n\r\nSome *text*\r\nmore\r\n"));
    }

    [Fact]
    public void Empty_paragraph_is_one_line_with_its_prefix_and_anchor_zero()
    {
        DocumentLayout layout = LayoutDump.Build("- \n");

        Assert.Equal("  • ", layout.TextOf(0));
        Assert.Equal(0, layout.Line(0).Anchor);
    }

    [Fact]
    public void Empty_code_line_is_anchored_at_its_start()
    {
        DocumentLayout layout = LayoutDump.Build("```\na\n\nb\n```\n");

        Assert.Equal(new[] { "▾ code", "  a", string.Empty, "  b" }, Enumerable.Range(0, layout.LineCount).Select(layout.TextOf));
        Assert.Equal(6, layout.Line(2).Anchor);
    }

    [Fact]
    public void Block_layout_offsets_are_relative_to_the_block()
    {
        RenderDocument doc = RenderDocument.Parse("one\n\ntwo\n");
        BlockLayout layout = BlockLayout.Layout(doc.Blocks[1], 40);

        Assert.Equal("t@0|w@1|o@2", string.Join("|", layout.Lines[0].Cells.Skip(layout.Lines[0].PrefixCells).Select(cell => cell.Text + "@" + cell.Offset)));
    }

    [Fact]
    public void Layout_rejects_a_width_below_one()
    {
        RenderBlock block = RenderDocument.Parse("x\n").Blocks[0];

        Assert.Throws<ArgumentOutOfRangeException>(() => BlockLayout.Layout(block, 0));
    }
}
