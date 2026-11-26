using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Mdedit.Tests;

/// <summary>
/// The colours of the rendered cells, read back from the drawn screen. The expected colours are the named constants of
/// <see cref="MdColors"/>, and the styles are the Terminal.Gui text styles the spec names.
/// </summary>
[Collection("Terminal GUI")]
public sealed class MdColorsTests
{
    private const string Doc = "# Title\n\nSome *emphasis* and `code` here.\n\n> quoted\n\n## Sub\n\n> [!WARNING]\n> Careful.\n";

    [Fact]
    public void A_heading_is_bold_in_its_level_colour()
    {
        TerminalScreen screen = Draw(Doc)[0];

        Attribute title = screen.At("Title");
        Assert.Equal(MdColors.HeadingOne, title.Foreground);
        Assert.True(title.Style.HasFlag(TextStyle.Bold));
        Assert.Equal(MdColors.HeadingTwo, screen.At("Sub").Foreground);
    }

    [Fact]
    public void An_emphasis_cell_is_italic_in_the_normal_foreground()
    {
        TerminalScreen screen = Draw(Doc)[0];

        Attribute emphasis = screen.At("emphasis");
        Assert.True(emphasis.Style.HasFlag(TextStyle.Italic));
        Assert.Equal(screen.Normal.Foreground, emphasis.Foreground);
        Assert.Equal(screen.Normal.Background, emphasis.Background);
    }

    [Fact]
    public void An_inline_code_cell_is_on_the_code_background()
    {
        TerminalScreen screen = Draw(Doc)[0];

        Attribute code = screen.At("code");
        Assert.Equal(MdColors.CodeBackground, code.Background);
        Assert.Equal(MdColors.CodeText, code.Foreground);
    }

    [Fact]
    public void A_quote_bar_is_dim_and_faint()
    {
        TerminalScreen screen = Draw(Doc)[0];

        Attribute bar = screen.At("│");
        Assert.Equal(MdColors.Dim, bar.Foreground);
        Assert.True(bar.Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public void An_alert_title_is_bold_in_the_colour_of_its_kind()
    {
        TerminalScreen screen = Draw(Doc)[0];

        Attribute title = screen.At("Warning");
        Assert.Equal(MdColors.AlertWarning, title.Foreground);
        Assert.True(title.Style.HasFlag(TextStyle.Bold));
    }

    [Fact]
    public void The_colours_of_a_style_keep_the_view_background_except_for_code()
    {
        var normal = new Attribute(Color.White, Color.Black);

        Attribute plain = MdColors.For(new CellStyle(StyleRole.Body), normal);

        Assert.Equal(normal.Foreground, plain.Foreground);
        Assert.Equal(normal.Background, plain.Background);
        Assert.Equal(TextStyle.None, plain.Style);
    }

    [Fact]
    public void A_code_cell_with_a_keyword_token_is_drawn_in_the_keyword_colour_and_a_cell_without_one_in_the_code_colour()
    {
        var view = new RenderedView(RenderDocument.Parse("```csharp\nvar x = 1;\n```\n", new KeywordColorizer()));

        TerminalScreen screen = TerminalHarness.Run(view, 40, 12)[0];

        int row = screen.RowOf("var x");
        Assert.Equal(MdColors.TokenKeyword, screen.Cells[row][0].Attribute.Foreground);
        Assert.Equal(MdColors.CodeText, screen.Cells[row][4].Attribute.Foreground);
    }

    [Fact]
    public void Tokens_have_their_palette_colour_and_identifiers_have_none()
    {
        Assert.Equal(MdColors.TokenKeyword, MdColors.ForToken(CodeToken.Keyword));
        Assert.Equal(MdColors.TokenAttribute, MdColors.ForToken(CodeToken.Tag));
        Assert.Null(MdColors.ForToken(CodeToken.None));
        Assert.Null(MdColors.ForToken(CodeToken.Identifier));
    }

    /// <summary>A colorizer that gives the first three characters of a block that starts with <c>var</c> the keyword token.</summary>
    private sealed class KeywordColorizer : ICodeColorizer
    {
        public IReadOnlyList<CodeSpan>? Colorize(string code, string language) =>
            code.StartsWith("var", StringComparison.Ordinal) ? [new CodeSpan(0, 3, CodeToken.Keyword)] : null;
    }

    private static List<TerminalScreen> Draw(string markdown)
    {
        var view = new RenderedView(RenderDocument.Parse(markdown));
        return TerminalHarness.Run(view, 40, 12);
    }
}
