using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The styled wrap of the details strip: each span keeps its style across breaks.</summary>
public sealed class StyledTextTests
{
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
