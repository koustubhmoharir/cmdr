using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Drawing;
using Xunit;

namespace Mdedit.Tests;

/// <summary>
/// The code colouring adapter: the walk of the highlighter's segments into spans, the role mapping, and the languages the highlighter
/// colours. The language samples need the native Oniguruma library; without it they are visible skips, not passes.
/// </summary>
public sealed class TextMateColorizerTests
{
    private static bool HighlighterAvailable => TextMateColorizer.Shared.Colorize("var x = 1;", "csharp") is not null;

    [Fact]
    public void Segments_are_walked_by_their_lengths_from_the_start_and_plain_code_adds_no_span()
    {
        var segments = new List<StyledSegment>
        {
            new("if", MarkdownStyleRole.CodeBlock, role: VisualRole.CodeKeyword),
            new(" ", MarkdownStyleRole.CodeBlock, role: VisualRole.Code),
            new("x", MarkdownStyleRole.CodeBlock, role: VisualRole.CodeIdentifier),
            new("\"s\"", MarkdownStyleRole.CodeBlock, role: VisualRole.CodeString),
            new("", MarkdownStyleRole.CodeBlock, role: VisualRole.CodeNumber),
            new(";", MarkdownStyleRole.CodeBlock, role: null),
        };
        var spans = new List<CodeSpan>();

        TextMateColorizer.AddSpans(spans, segments, 10);

        Assert.Equal(
            new[]
            {
                new CodeSpan(10, 2, CodeToken.Keyword),
                new CodeSpan(13, 1, CodeToken.Identifier),
                new CodeSpan(14, 3, CodeToken.String),
            },
            spans);
    }

    [Theory]
    [InlineData(VisualRole.CodeKeyword, CodeToken.Keyword)]
    [InlineData(VisualRole.CodeString, CodeToken.String)]
    [InlineData(VisualRole.CodeNumber, CodeToken.Number)]
    [InlineData(VisualRole.CodeComment, CodeToken.Comment)]
    [InlineData(VisualRole.CodeType, CodeToken.Type)]
    [InlineData(VisualRole.CodeFunctionName, CodeToken.Function)]
    [InlineData(VisualRole.CodeOperator, CodeToken.Operator)]
    [InlineData(VisualRole.CodePunctuation, CodeToken.Punctuation)]
    [InlineData(VisualRole.CodePreprocessor, CodeToken.Preprocessor)]
    [InlineData(VisualRole.CodeConstant, CodeToken.Constant)]
    [InlineData(VisualRole.CodeAttribute, CodeToken.Attribute)]
    [InlineData(VisualRole.CodeIdentifier, CodeToken.Identifier)]
    [InlineData(VisualRole.Code, CodeToken.None)]
    public void Each_highlighter_role_maps_to_its_token(VisualRole role, CodeToken token) =>
        Assert.Equal(token, TextMateColorizer.TokenOf(role));

    [Fact]
    public void A_language_without_a_grammar_is_not_coloured()
    {
        Assert.Null(TextMateColorizer.Shared.Colorize("anything at all", "no-such-language"));
    }

    [Fact]
    public void A_comment_that_runs_over_a_line_colours_the_next_line_as_a_comment()
    {
        Skip.IfNot(HighlighterAvailable, "The native Oniguruma library (onigwrap) is not available here, so code is not coloured.");
        const string code = "/* first\nsecond */ int n;";

        IReadOnlyList<CodeSpan>? spans = TextMateColorizer.Shared.Colorize(code, "csharp");

        Assert.NotNull(spans);
        // The second line is a comment only because the first one left the tokenizer inside the comment.
        Assert.Contains(spans, span => span.Token == CodeToken.Comment && code.Substring(span.Start, span.Length) == "second ");
    }

    [SkippableTheory]
    [InlineData("csharp", "int F() { return 42; }", CodeToken.Keyword, "return")]
    [InlineData("go", "package main\n\nfunc main() {}", CodeToken.Keyword, "package")]
    [InlineData("python", "def f(x):\n    return 'a'", CodeToken.Keyword, "return")]
    [InlineData("typescript", "function f() { return 1; }", CodeToken.Keyword, "return")]
    [InlineData("javascript", "function f() { return 'a'; }", CodeToken.Keyword, "return")]
    [InlineData("html", "<div class=\"a\">t</div>", CodeToken.Attribute, "class")]
    [InlineData("css", "p { color: red; }", CodeToken.Type, "color")]
    [InlineData("xml", "<a b=\"c\"/>", CodeToken.Attribute, "b")]
    [InlineData("json", "{\"a\": \"x\"}", CodeToken.String, "x")]
    [InlineData("shell", "echo \"hi\"", CodeToken.String, "hi")]
    public void A_language_sample_has_the_token_its_text_is_known_to_have(string language, string code, CodeToken token, string text)
    {
        Skip.IfNot(HighlighterAvailable, "The native Oniguruma library (onigwrap) is not available here, so code is not coloured.");

        IReadOnlyList<CodeSpan>? spans = TextMateColorizer.Shared.Colorize(code, language);

        Assert.NotNull(spans);
        Assert.Contains(spans, span => span.Token == token && code.Substring(span.Start, span.Length) == text);
    }
}
