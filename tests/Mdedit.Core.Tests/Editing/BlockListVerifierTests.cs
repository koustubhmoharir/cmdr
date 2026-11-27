using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>The verifier's comparison of the whole block list: kinds, levels, containers with their identity, and text.</summary>
public sealed class BlockListVerifierTests
{
    [Fact]
    public void A_document_holds_against_its_own_shapes()
    {
        Assert.True(EditVerifier.Holds(BlockShape.Of(RenderDocument.Parse("a\n\n- b")), RenderDocument.Parse("a\n\n- b")));
    }

    [Fact]
    public void A_change_of_text_in_one_block_does_not_hold()
    {
        Assert.False(EditVerifier.Holds(BlockShape.Of(RenderDocument.Parse("a\n\nb")), RenderDocument.Parse("a\n\nc")));
    }

    [Fact]
    public void A_change_of_heading_level_does_not_hold()
    {
        Assert.False(EditVerifier.Holds(BlockShape.Of(RenderDocument.Parse("# a")), RenderDocument.Parse("## a")));
    }

    [Fact]
    public void Two_quotes_do_not_hold_against_one_quote_of_the_same_blocks()
    {
        Assert.False(EditVerifier.Holds(BlockShape.Of(RenderDocument.Parse("> a\n>\n> b")), RenderDocument.Parse("> a\n\n> b")));
    }

    [Fact]
    public void One_quote_holds_against_one_quote_whatever_its_identity_numbers()
    {
        Assert.True(EditVerifier.Holds(BlockShape.Of(RenderDocument.Parse("> a\n>\n> b")), RenderDocument.Parse("> a\n>\n> b")));
    }

    [Fact]
    public void Tightness_is_not_compared_so_a_loose_list_holds_against_a_tight_one()
    {
        Assert.True(EditVerifier.Holds(BlockShape.Of(RenderDocument.Parse("- a\n- b")), RenderDocument.Parse("- a\n\n- b")));
    }

    [Fact]
    public void A_block_list_of_a_different_length_does_not_hold()
    {
        Assert.False(EditVerifier.Holds(BlockShape.Of(RenderDocument.Parse("a")), RenderDocument.Parse("a\n\nb")));
    }
}
