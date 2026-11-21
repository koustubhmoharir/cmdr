using Cfgedit.Tui;

namespace Cfgedit.Tests;

/// <summary>
/// The text mode's mapping between the view's rows and columns and the document's offsets, with the expected values written out by hand. The
/// view joins its lines with <see cref="Environment.NewLine"/>, so the views here are built with it.
/// </summary>
public sealed class TextModeRulesTests
{
    [Fact]
    public void WithNewline_replaces_the_view_line_breaks_with_the_documents()
    {
        string view = "a" + Environment.NewLine + "b";

        Assert.Equal("a\r\nb", TextModeRules.WithNewline(view, "\r\n"));
        Assert.Equal("a\rb", TextModeRules.WithNewline(view, "\r"));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void OffsetOf_counts_the_documents_own_line_break_on_each_row_before_the_cursor(string newline)
    {
        string view = "ab" + Environment.NewLine + "cde" + Environment.NewLine + "f";

        // Row 2 column 1: "ab" and "cde" with their breaks come first, then one character of the third row.
        int offset = TextModeRules.OffsetOf(view, 2, 1, newline);

        Assert.Equal(2 + newline.Length + 3 + newline.Length + 1, offset);
    }

    [Fact]
    public void OffsetOf_clamps_a_row_past_the_end_and_a_column_past_the_end_of_its_row()
    {
        string view = "ab" + Environment.NewLine + "c";

        Assert.Equal(2 + 2 + 1, TextModeRules.OffsetOf(view, 9, 9, "\r\n"));
        Assert.Equal(2, TextModeRules.OffsetOf(view, 0, 9, "\r\n"));
    }

    [Fact]
    public void Columns_are_counted_in_grapheme_clusters_so_a_combining_mark_is_one_column()
    {
        // "e" followed by a combining acute accent is one grapheme of two UTF-16 units, then "x".
        string view = "e" + (char)0x0301 + "x";

        Assert.Equal(2, TextModeRules.OffsetOf(view, 0, 1, "\n"));
        Assert.Equal((0, 1), TextModeRules.PositionOf(view, "\n", 2));
    }

    [Fact]
    public void PositionOf_is_the_inverse_of_OffsetOf_across_a_CRLF_document()
    {
        const string text = "{\r\n  \"a\": 1\r\n}";

        (int row, int column) = TextModeRules.PositionOf(text, "\r\n", 6);

        Assert.Equal((1, 3), (row, column));
        Assert.Equal(6, TextModeRules.OffsetOf(text.Replace("\r\n", Environment.NewLine, StringComparison.Ordinal), row, column, "\r\n"));
    }

    [Fact]
    public void PositionOf_places_the_start_of_a_line_after_a_CRLF_at_column_zero()
    {
        const string text = "ab\r\ncd";

        Assert.Equal((1, 0), TextModeRules.PositionOf(text, "\r\n", 4));
        Assert.Equal((0, 2), TextModeRules.PositionOf(text, "\r\n", 2));
    }
}
