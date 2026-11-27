using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>The line ending a structure edit writes: the first line break of the text, or a line feed.</summary>
public sealed class LineEndingTests
{
    [Theory]
    [InlineData("a\nb", "\n")]
    [InlineData("a\r\nb", "\r\n")]
    [InlineData("a\rb", "\r")]
    [InlineData("ab", "\n")]
    [InlineData("\r\n\n", "\r\n")]
    [InlineData("a\n\r\n", "\n")]
    public void The_line_ending_is_the_first_line_break_of_the_text(string text, string expected)
    {
        Assert.Equal(expected, LineEnding.Of(text));
    }

    [Fact]
    public void A_split_in_a_crlf_list_writes_crlf()
    {
        Assert.Equal(new EditCase.Outcome("- a\r\n- b\r\n- c", 12), EditCase.Enter("- a\r\n- bc", 8));
    }

    [Fact]
    public void A_pending_below_a_quote_in_a_crlf_document_uses_crlf()
    {
        Assert.Equal(new ViewEditResult.Pending(3, "\r\n>\r\n> ", PendingPlacement.Below, "│ "), EditCase.Held(EditCase.EnterAt("> a\r\n\r\n> b", 3)));
    }

    [Fact]
    public void The_first_line_break_decides_the_line_ending_of_a_split()
    {
        Assert.Equal(new EditCase.Outcome("x\n\na\n\nb\r\n\r\ny", 6), EditCase.Enter("x\n\nab\r\n\r\ny", 4));
    }

    [Fact]
    public void A_crlf_first_line_break_makes_the_split_write_crlf()
    {
        Assert.Equal(new EditCase.Outcome("x\r\n\r\na\r\n\r\nb", 10), EditCase.Enter("x\r\n\r\nab", 6));
    }
}
