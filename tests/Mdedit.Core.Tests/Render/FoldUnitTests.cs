using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The fold units of a document: which blocks form a section or a code unit, their ends, and their hidden line counts.</summary>
public sealed class FoldUnitTests
{
    [Fact]
    public void A_section_runs_to_the_next_heading_of_the_same_or_a_higher_level()
    {
        ImmutableArray<FoldUnit> units = RenderDocument.Parse("# A\n\npara\n\n## B\n\npara\n\n# C\n\npara\n").FoldUnits;

        Assert.Equal(new[] { FoldUnitKind.Section, FoldUnitKind.Section, FoldUnitKind.Section }, units.Select(unit => unit.Kind));
        Assert.Equal(new[] { 0, 2, 4 }, units.Select(unit => unit.HeadBlock));
        Assert.Equal(new[] { 4, 4, 6 }, units.Select(unit => unit.EndBlock));
        Assert.Equal(new[] { 0, 11, 23 }, units.Select(unit => unit.Start));
    }

    [Fact]
    public void Hidden_lines_run_from_the_line_after_the_head_through_the_last_line_of_the_last_block()
    {
        ImmutableArray<FoldUnit> units = RenderDocument.Parse("# A\n\npara\n\n## B\n\npara\n\n# C\n\npara\n").FoldUnits;

        Assert.Equal(new[] { 6, 2, 2 }, units.Select(unit => unit.HiddenLines));
    }

    [Fact]
    public void A_head_that_is_the_last_block_hides_nothing_and_a_single_line_head_hides_one()
    {
        Assert.Equal(0, RenderDocument.Parse("# A\n").FoldUnits.Single().HiddenLines);
        Assert.Equal(1, RenderDocument.Parse("# A\npara\n").FoldUnits.Single().HiddenLines);
    }

    [Fact]
    public void Trailing_blank_lines_after_the_last_block_of_the_unit_are_not_hidden()
    {
        Assert.Equal(2, RenderDocument.Parse("# A\n\npara\n").FoldUnits.Single().HiddenLines);
        Assert.Equal(2, RenderDocument.Parse("# A\n\npara\n\n\n").FoldUnits.Single().HiddenLines);
    }

    [Fact]
    public void Carriage_return_line_endings_each_end_one_line()
    {
        Assert.Equal(1, RenderDocument.Parse("# A\rpara\r").FoldUnits.Single().HiddenLines);
        Assert.Equal(2, RenderDocument.Parse("# A\r\n\r\npara\r\n\r\n# C\r\n").FoldUnits[0].HiddenLines);
    }

    [Fact]
    public void A_setext_heading_is_a_head_whose_hidden_lines_start_after_its_underline()
    {
        Assert.Equal(1, RenderDocument.Parse("Title\n===\npara\n").FoldUnits.Single().HiddenLines);
    }

    [Fact]
    public void A_heading_at_the_same_level_or_higher_ends_a_section_and_a_deeper_one_does_not()
    {
        ImmutableArray<FoldUnit> units = RenderDocument.Parse("# A\n## B\n# C\n").FoldUnits;

        Assert.Equal(2, units[0].EndBlock);
        Assert.Equal(1, units[0].HiddenLines);
        Assert.Equal(2, units[1].EndBlock);
        Assert.Equal(0, units[1].HiddenLines);
    }

    [Fact]
    public void A_heading_inside_a_quote_is_not_a_unit_and_does_not_end_a_section()
    {
        ImmutableArray<FoldUnit> units = RenderDocument.Parse("# A\n\n> # Q\n>\n> text\n\npara\n").FoldUnits;

        FoldUnit section = Assert.Single(units);
        Assert.Equal(FoldUnitKind.Section, section.Kind);
        Assert.Equal(0, section.HeadBlock);
        Assert.Equal(4, section.EndBlock);
    }

    [Fact]
    public void Every_code_block_is_a_code_unit_hiding_its_code_lines()
    {
        ImmutableArray<FoldUnit> units = RenderDocument.Parse("```cs\nint x;\nint y;\nint z;\n```\n\n> ```\n> q\n> ```\n").FoldUnits;

        Assert.Equal(new[] { FoldUnitKind.Code, FoldUnitKind.Code }, units.Select(unit => unit.Kind));
        Assert.Equal(new[] { 3, 1 }, units.Select(unit => unit.HiddenLines));
        Assert.Equal(new[] { 1, 2 }, units.Select(unit => unit.EndBlock));
        Assert.Equal(new[] { 0, 1 }, units.Select(unit => unit.HeadBlock));
    }

    [Fact]
    public void A_document_without_headings_or_code_has_no_units()
    {
        Assert.Empty(RenderDocument.Parse("para\n\n- item\n").FoldUnits);
    }
}
