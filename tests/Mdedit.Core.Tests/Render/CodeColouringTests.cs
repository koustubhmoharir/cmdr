using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The token of code cells: the colorizer's calls, the spans landing on cells across lines, and the cases with no colouring.</summary>
public sealed class CodeColouringTests
{
    private const string Block = "```cs\nint x;\nreturn 1;\n```\n";

    /// <summary>The code of <see cref="Block"/> joined with line feeds: "int x;" then "return 1;" after a line feed at index 6.</summary>
    private static readonly CodeSpan[] Spans =
    [
        new CodeSpan(0, 3, CodeToken.Keyword),
        new CodeSpan(7, 6, CodeToken.Keyword),
        new CodeSpan(14, 1, CodeToken.Number),
    ];

    [Fact]
    public void Tokens_land_on_the_cells_across_lines()
    {
        var colorizer = new FakeColorizer(Spans);
        RenderBlock block = RenderDocument.Parse(Block, colorizer).Blocks[0];

        Assert.Equal(CodeToken.Keyword, block.Runs[0].Cells[0].Style.Token);
        Assert.Equal(CodeToken.Keyword, block.Runs[0].Cells[2].Style.Token);
        Assert.Equal(CodeToken.None, block.Runs[0].Cells[3].Style.Token);
        Assert.Equal(CodeToken.Keyword, block.Runs[1].Cells[0].Style.Token);
        Assert.Equal(CodeToken.Keyword, block.Runs[1].Cells[5].Style.Token);
        Assert.Equal(CodeToken.None, block.Runs[1].Cells[6].Style.Token);
        Assert.Equal(CodeToken.Number, block.Runs[1].Cells[7].Style.Token);
        Assert.Equal(CodeToken.None, block.Runs[1].Cells[8].Style.Token);
    }

    [Fact]
    public void The_colorizer_is_called_once_per_block_with_the_lines_joined_and_the_normalized_language()
    {
        var colorizer = new FakeColorizer(Spans);

        RenderDocument.Parse(Block, colorizer);

        CallRecord call = Assert.Single(colorizer.Calls);
        Assert.Equal("int x;\nreturn 1;", call.Code);
        Assert.Equal("csharp", call.Language);
    }

    [Fact]
    public void Crlf_code_blocks_are_joined_with_line_feeds_and_coloured_the_same_way()
    {
        var colorizer = new FakeColorizer(Spans);
        string crlf = Block.Replace("\n", "\r\n");

        RenderBlock block = RenderDocument.Parse(crlf, colorizer).Blocks[0];

        Assert.Equal("int x;\nreturn 1;", colorizer.Calls[0].Code);
        Assert.Equal(CodeToken.Keyword, block.Runs[1].Cells[0].Style.Token);
        Assert.Equal(CodeToken.Number, block.Runs[1].Cells[7].Style.Token);
    }

    [Fact]
    public void A_span_that_starts_inside_a_grapheme_does_not_colour_it_and_one_that_starts_on_its_first_character_does()
    {
        // "é" is e and a combining acute accent: one cell of two UTF-16 units, then "x".
        const string code = "```cs\néx\n```\n";

        RenderBlock covered = RenderDocument.Parse(code, new FakeColorizer([new CodeSpan(1, 1, CodeToken.Keyword)])).Blocks[0];
        RenderBlock starting = RenderDocument.Parse(code, new FakeColorizer([new CodeSpan(0, 1, CodeToken.String)])).Blocks[0];

        Assert.Equal("é", covered.Runs[0].Cells[0].Text);
        Assert.Equal(CodeToken.None, covered.Runs[0].Cells[0].Style.Token);
        Assert.Equal("é", starting.Runs[0].Cells[0].Text);
        Assert.Equal(CodeToken.String, starting.Runs[0].Cells[0].Style.Token);
        Assert.Equal(CodeToken.None, starting.Runs[0].Cells[1].Style.Token);
    }

    [Fact]
    public void A_null_result_leaves_every_token_none_and_the_colorizer_was_still_called()
    {
        var colorizer = new FakeColorizer(null);

        RenderBlock block = RenderDocument.Parse(Block, colorizer).Blocks[0];

        Assert.Single(colorizer.Calls);
        Assert.All(block.Runs, run => Assert.All(run.Cells, cell => Assert.Equal(CodeToken.None, cell.Style.Token)));
    }

    [Fact]
    public void A_code_block_with_no_info_string_is_not_given_to_the_colorizer()
    {
        var colorizer = new FakeColorizer(Spans);

        RenderBlock block = RenderDocument.Parse("```\nint x;\n```\n", colorizer).Blocks[0];

        Assert.Empty(colorizer.Calls);
        Assert.Null(block.Language);
        Assert.Null(block.LanguageId);
        Assert.All(block.Runs, run => Assert.All(run.Cells, cell => Assert.Equal(CodeToken.None, cell.Style.Token)));
    }

    [Fact]
    public void An_indented_code_block_and_a_paragraph_are_not_given_to_the_colorizer()
    {
        var colorizer = new FakeColorizer(Spans);

        RenderDocument.Parse("    int x;\n\ntext *here*\n\n# Title\n", colorizer);

        Assert.Empty(colorizer.Calls);
    }

    [Fact]
    public void Only_the_first_word_of_the_info_string_names_the_language()
    {
        var colorizer = new FakeColorizer(null);

        RenderBlock block = RenderDocument.Parse("```csharp {.class}\nint x;\n```\n", colorizer).Blocks[0];

        Assert.Equal("csharp", block.Language);
        Assert.Equal("csharp", block.LanguageId);
        Assert.Equal("csharp", colorizer.Calls[0].Language);
    }

    [Fact]
    public void The_language_id_is_the_alias_table_entry_and_the_header_keeps_the_word_as_written()
    {
        var colorizer = new FakeColorizer(null);

        RenderBlock block = RenderDocument.Parse("```Py\nx = 1\n```\n", colorizer).Blocks[0];

        Assert.Equal("Py", block.Language);
        Assert.Equal("python", block.LanguageId);
        Assert.Equal("python", colorizer.Calls[0].Language);
        Assert.Equal("▾ Py", LayoutDump.Lines("```Py\nx = 1\n```\n")[0]);
    }

    [Fact]
    public void Lowering_without_a_colorizer_leaves_the_tokens_none_and_the_language_id_set()
    {
        RenderBlock block = RenderDocument.Parse(Block).Blocks[0];

        Assert.Equal("csharp", block.LanguageId);
        Assert.All(block.Runs, run => Assert.All(run.Cells, cell => Assert.Equal(CodeToken.None, cell.Style.Token)));
    }

    /// <summary>The arguments of one call to the fake colorizer.</summary>
    private sealed record CallRecord(string Code, string Language);

    /// <summary>A colorizer that records its calls and returns fixed spans, or null when it has none.</summary>
    private sealed class FakeColorizer : ICodeColorizer
    {
        private readonly IReadOnlyList<CodeSpan>? _spans;

        /// <summary>Makes a colorizer that returns <paramref name="spans"/> for every call.</summary>
        /// <param name="spans">The spans to return, or null.</param>
        public FakeColorizer(IReadOnlyList<CodeSpan>? spans) => _spans = spans;

        /// <summary>The calls made so far, in order.</summary>
        public List<CallRecord> Calls { get; } = [];

        /// <summary>Records the call and returns the fixed spans.</summary>
        public IReadOnlyList<CodeSpan>? Colorize(string code, string language)
        {
            Calls.Add(new CallRecord(code, language));
            return _spans;
        }
    }
}
