using System.Text.RegularExpressions;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The document layout: separators, the tight rule, the anchors of the separator lines, and the round trip of the source map.</summary>
public sealed class DocumentLayoutTests
{
    private static readonly string[] Widths = ["20", "80", "200"];

    [Fact]
    public void Blocks_are_separated_by_one_blank_line_unless_both_are_tight()
    {
        Assert.Equal(new[] { "• a", "• b", string.Empty, "c" }, LayoutDump.Lines("- a\n- b\n\nc\n"));
        Assert.Equal(new[] { "• a", string.Empty, "• b" }, LayoutDump.Lines("- a\n\n- b\n"));
    }

    [Fact]
    public void Separator_between_quotes_carries_the_bar()
    {
        Assert.Equal(new[] { "│ a", "│ ", "│ b" }, LayoutDump.Lines("> a\n>\n> b\n"));
    }

    [Fact]
    public void Separator_line_has_the_end_of_the_previous_block_as_its_anchor_on_the_previous_block_base()
    {
        DocumentLayout layout = LayoutDump.Build("one\n\ntwo\n");

        Assert.Equal(string.Empty, layout.TextOf(1));
        Assert.Equal(0, layout.BaseOffset(1));
        Assert.Equal(3, layout.Line(1).Anchor);
        Assert.Equal(5, layout.BaseOffset(2));
    }

    [Fact]
    public void Line_count_and_width_are_reported()
    {
        DocumentLayout layout = LayoutDump.Build("x\n", 17);

        Assert.Equal(17, layout.Width);
        Assert.Equal(1, layout.LineCount);
    }

    [Fact]
    public void An_empty_document_has_no_lines_and_its_position_is_the_origin()
    {
        DocumentLayout layout = LayoutDump.Build(string.Empty);

        Assert.Equal(0, layout.LineCount);
        Assert.Equal((0, 0), layout.PositionOf(0));
    }

    [Fact]
    public void Offset_at_a_synthetic_cell_is_the_next_mapped_cell_on_the_line()
    {
        DocumentLayout layout = LayoutDump.Build("- item\n");

        // Column 0 is the bullet and column 1 its space; the first mapped cell is the 'i' at offset 2.
        Assert.Equal(2, layout.OffsetAt(0, 0));
        Assert.Equal(2, layout.OffsetAt(0, 1));
    }

    [Fact]
    public void Offset_past_the_end_of_a_line_is_its_anchor()
    {
        DocumentLayout layout = LayoutDump.Build("abc\n\ndef\n");

        Assert.Equal(3, layout.OffsetAt(0, 40));
        Assert.Equal(3, layout.OffsetAt(1, 0));
    }

    [Fact]
    public void Offset_inside_a_wide_cell_is_that_cell()
    {
        DocumentLayout layout = LayoutDump.Build("中文\n");

        Assert.Equal(0, layout.OffsetAt(0, 1));
        Assert.Equal(1, layout.OffsetAt(0, 2));
    }

    [Fact]
    public void Offset_on_a_separator_line_is_the_end_of_the_previous_block()
    {
        DocumentLayout layout = LayoutDump.Build("one\n\ntwo\n");

        Assert.Equal(3, layout.OffsetAt(1, 0));
    }

    [Fact]
    public void Position_of_an_exact_offset_is_its_cell()
    {
        DocumentLayout layout = LayoutDump.Build("ab cd\n");

        Assert.Equal((0, 3), layout.PositionOf(3));
    }

    [Fact]
    public void Position_of_an_escapes_backslash_lands_on_the_escaped_character()
    {
        // The backslash at offset 0 is unmapped; the '*' at offset 1 is the next mapped cell.
        DocumentLayout layout = LayoutDump.Build("\\*x\n");

        Assert.Equal((0, 0), layout.PositionOf(0));
        Assert.Equal("*x", layout.TextOf(0));
    }

    [Fact]
    public void Position_inside_strong_delimiters_lands_on_the_next_text()
    {
        // "**bold**": the opening delimiter is offsets 0 and 1, the text starts at 2.
        DocumentLayout layout = LayoutDump.Build("**bold**\n");

        Assert.Equal((0, 0), layout.PositionOf(0));
        Assert.Equal((0, 0), layout.PositionOf(1));
        Assert.Equal((0, 0), layout.PositionOf(2));
    }

    [Fact]
    public void Position_inside_an_entity_lands_on_the_cell_after_it()
    {
        // "&amp;x": the entity is offsets 0 to 4 and every cell of it is at 0; the 'x' is at 5.
        DocumentLayout layout = LayoutDump.Build("&amp;x\n");

        Assert.Equal((0, 1), layout.PositionOf(3));
        Assert.Equal((0, 1), layout.PositionOf(5));
    }

    [Fact]
    public void Position_past_the_end_of_the_document_is_the_end_of_the_last_line()
    {
        DocumentLayout layout = LayoutDump.Build("ab\n\ncd\n");

        Assert.Equal((2, 2), layout.PositionOf(1000));
    }

    [Fact]
    public void Position_in_the_gap_between_blocks_is_the_next_mapped_cell()
    {
        // "one" ends at 3, "two" starts at 5: offset 4 (the blank line) is the 't' at 5.
        DocumentLayout layout = LayoutDump.Build("one\n\ntwo\n");

        Assert.Equal((2, 0), layout.PositionOf(4));
    }

    [Fact]
    public void Position_of_a_synthetic_bullet_offset_is_the_first_text_of_its_item()
    {
        DocumentLayout layout = LayoutDump.Build("- item\n");

        Assert.Equal((0, 2), layout.PositionOf(0));
    }

    [Fact]
    public void Round_trip_holds_for_every_mapped_cell_of_the_repository_markdown_at_every_width()
    {
        foreach (string file in RepositoryMarkdownFiles())
        {
            string text = File.ReadAllText(file);
            foreach (string width in Widths)
            {
                DocumentLayout layout = LayoutDump.Build(text, int.Parse(width));
                int checkedCells = 0;
                string? problem = FirstRoundTripProblem(layout, ref checkedCells);
                Assert.True(problem is null, $"{Path.GetFileName(file)} at width {width}: {problem}");
                Assert.True(checkedCells > 500, $"{Path.GetFileName(file)} at width {width}: only {checkedCells} mapped cells checked");
            }
        }
    }

    [Fact]
    public void Round_trip_holds_for_the_nasty_inputs()
    {
        string[] nasty =
        [
            "# T\r\n\r\nPara *em* `c`\r\nline2\r\n\r\n- a\r\n- b\r\n\r\n> q\r\n> r\r\n\r\n```\r\nx\r\n```\r\n",
            "👨\u200D👩\u200D👧 *x* 👍🏽 e\u0301 *a*\u0301 b\n\n- ✨ item\n",
            "中文 **粗体** 字符 [链接](https://例.example)\n\n| 名 | 字 |\n|---|---|\n| 1 | 2 |\n",
            "&amp;&copy;&#x1F600; &notanentity; \\*\\_\\`\\\\ \\a\n",
            "> > > deep\n> >\n> > - item\n> >   more\n",
            "1. one\n1) two\n\n- [ ] t\n- [x] u\n  > q\n",
        ];
        foreach (string text in nasty)
        {
            foreach (string width in Widths)
            {
                int checkedCells = 0;
                string? problem = FirstRoundTripProblem(LayoutDump.Build(text, int.Parse(width)), ref checkedCells);
                Assert.True(problem is null, $"'{text}' at width {width}: {problem}");
                Assert.True(checkedCells > 0, $"'{text}' at width {width}: no mapped cell was checked");
            }
        }
    }

    /// <summary>
    /// The first mapped cell whose position does not come back to itself, or null when every one does. A zero-width cell has
    /// the column of the cell before it, so it has no column of its own and is not checked.
    /// </summary>
    private static string? FirstRoundTripProblem(DocumentLayout layout, ref int checkedCells)
    {
        for (int line = 0; line < layout.LineCount; line++)
        {
            DisplayLine row = layout.Line(line);
            int baseOffset = layout.BaseOffset(line);
            int column = 0;
            foreach (Cell cell in row.Cells)
            {
                if (cell.Width == 0)
                {
                    continue;
                }

                if (cell.IsMapped)
                {
                    checkedCells++;
                    int offset = layout.OffsetAt(line, column);
                    if (offset != baseOffset + cell.Offset)
                    {
                        return $"line {line} column {column} gives {offset}, cell '{cell.Text}' is at {baseOffset + cell.Offset}";
                    }

                    (int positionLine, int positionColumn) = layout.PositionOf(offset);
                    if (positionLine != line || positionColumn != column)
                    {
                        return $"offset {offset} of '{cell.Text}' on line {line} returns line {positionLine} column {positionColumn}";
                    }
                }

                column += cell.Width;
            }
        }

        return null;
    }

    private static IEnumerable<string> RepositoryMarkdownFiles()
    {
        string root = RepositoryRoot();
        IEnumerable<string> docs = Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories);
        return docs.Append(Path.Combine(root, "README.md"));
    }

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Cmdr.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("Cmdr.slnx was not found above the test assembly.");
    }
}
