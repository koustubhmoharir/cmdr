using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The styled wrap: its plain text is the plain wrap, and each span keeps its style across breaks.</summary>
public sealed class StyledTextTests
{
    /// <summary>Texts split into spans, one span per part; the part's index is its style, so a span shows where it came from.</summary>
    public static TheoryData<string[]> Parts => new()
    {
        new[] { "hello ", "world foo" },
        new[] { "hello", " ", "world foo" },
        new[] { "ab", " cd" },
        new[] { "ab   ", "cd" },
        new[] { "abc def", "g" },
        new[] { "ab ", "abcdefgh" },
        new[] { "The quick brown fox", " jumps over", " the lazy dog" },
        new[] { "abcdefghij" },
        new[] { "   lead", " space" },
        new[] { "日本", "語テ", "キスト" },
        new[] { "日本 ", "語", " テキスト" },
        new[] { "日", "本" },
        new[] { "a😀 b", "c" },
        new[] { "", "abc", "" },
        new[] { string.Empty },
        Array.Empty<string>(),
    };

    [Theory]
    [MemberData(nameof(Parts))]
    public void Plain_text_of_the_styled_wrap_is_the_plain_wrap_at_every_width(string[] parts)
    {
        var spans = parts.Select((part, index) => (Text: part, Style: index)).ToList();
        string plain = string.Concat(parts);

        for (int width = -1; width <= 12; width++)
        {
            IReadOnlyList<IReadOnlyList<(string Text, int Style)>> styled = TextWidth.WrapSpans(spans, width);

            Assert.Equal(
                TextWidth.Wrap(plain, width),
                styled.Select(line => string.Concat(line.Select(span => span.Text))).ToList());
        }
    }

    [Theory]
    [MemberData(nameof(Parts))]
    public void Every_span_is_a_piece_of_the_part_it_came_from(string[] parts)
    {
        var spans = parts.Select((part, index) => (Text: part, Style: index)).ToList();

        for (int width = 1; width <= 12; width++)
        {
            foreach (IReadOnlyList<(string Text, int Style)> line in TextWidth.WrapSpans(spans, width))
            {
                Assert.All(line, span =>
                {
                    Assert.NotEmpty(span.Text);
                    Assert.Contains(span.Text, parts[span.Style]);
                });
            }
        }
    }

    [Fact]
    public void A_break_between_spans_keeps_each_span_with_its_own_style()
    {
        var spans = new[] { ("ab", 0), (" cd", 1) };

        Assert.Equal(
            new[]
            {
                new[] { ("ab", 0) },
                new[] { ("cd", 1) },
            },
            TextWidth.WrapSpans(spans, 2).Select(line => line.ToArray()).ToArray());
    }

    [Fact]
    public void A_span_split_over_lines_continues_with_its_style_on_the_next_line()
    {
        var spans = new[] { ("hello wor", 0), ("ld", 1) };

        Assert.Equal(
            new[]
            {
                new[] { ("hello", 0) },
                new[] { ("wor", 0), ("ld", 1) },
            },
            TextWidth.WrapSpans(spans, 5).Select(line => line.ToArray()).ToArray());
    }

    [Fact]
    public void Adjacent_pieces_with_the_same_style_form_one_span()
    {
        var spans = new[] { ("ab", "x"), ("cd", "x") };

        Assert.Equal(
            new[] { new[] { ("abcd", "x") } },
            TextWidth.WrapSpans(spans, 10).Select(line => line.ToArray()).ToArray());
    }

    [Fact]
    public void Empty_spans_give_one_empty_line_and_a_width_that_is_not_positive_gives_none()
    {
        var empty = Array.Empty<(string Text, int Style)>();

        Assert.Single(TextWidth.WrapSpans(empty, 4));
        Assert.Empty(TextWidth.WrapSpans(empty, 0));
        Assert.Empty(TextWidth.WrapSpans(new[] { ("abc", 0) }, 0));
    }

    [Fact]
    public void Details_lines_keep_their_styles_through_the_wrap()
    {
        var lines = new[]
        {
            new DetailsLine(new[]
            {
                new DetailsSpan("cwd: ", DetailsStyle.Label),
                new DetailsSpan("/a/long path", DetailsStyle.Path),
            }),
        };

        IReadOnlyList<DetailsLine> wrapped = DetailsText.Wrap(lines, 8);

        Assert.Equal(new[] { "cwd:", "/a/long", "path" }, DetailsText.Plain(wrapped));
        Assert.Equal(
            new[] { ("cwd:", DetailsStyle.Label) },
            wrapped[0].Spans.Select(span => (span.Text, span.Style)).ToArray());
        Assert.Equal(
            new[] { ("/a/long", DetailsStyle.Path) },
            wrapped[1].Spans.Select(span => (span.Text, span.Style)).ToArray());
        Assert.Equal(
            new[] { ("path", DetailsStyle.Path) },
            wrapped[2].Spans.Select(span => (span.Text, span.Style)).ToArray());
    }
}
