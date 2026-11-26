using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The GitHub-style heading anchors: the slug of each heading's text, duplicates numbered, and the lookup by fragment.</summary>
public sealed class HeadingAnchorsTests
{
    private static ImmutableArray<HeadingAnchor> AnchorsOf(string source) =>
        HeadingAnchors.Of(RenderDocument.Parse(source));

    [Fact]
    public void Simple_heading_is_lower_cased_and_its_start_is_the_heading_block()
    {
        const string source = "text\n\n## Simple\n";

        Assert.Equal([new HeadingAnchor("simple", source.IndexOf('#'))], AnchorsOf(source).ToArray());
    }

    [Fact]
    public void Punctuation_is_removed_and_underscores_and_hyphens_are_kept()
    {
        Assert.Equal("whats-new", Assert.Single(AnchorsOf("# What's new?")).Anchor);
        Assert.Equal("a_b-c", Assert.Single(AnchorsOf("# a_b-c")).Anchor);
    }

    [Fact]
    public void Emphasis_and_code_in_a_heading_are_removed_with_their_markup()
    {
        Assert.Equal("bold-code", Assert.Single(AnchorsOf("# **Bold** `code`")).Anchor);
    }

    [Fact]
    public void Unicode_letters_are_kept_and_lower_cased()
    {
        Assert.Equal("café-été", Assert.Single(AnchorsOf("# Café Été")).Anchor);
    }

    [Fact]
    public void Each_space_becomes_a_hyphen_so_a_double_space_gives_two_hyphens()
    {
        Assert.Equal("a--b", Assert.Single(AnchorsOf("# A  B")).Anchor);
    }

    [Fact]
    public void Repeated_anchors_get_numbered_suffixes_in_document_order()
    {
        const string source = "# Intro\n\n# Intro\n\n# Intro\n";

        Assert.Equal(["intro", "intro-1", "intro-2"], AnchorsOf(source).Select(anchor => anchor.Anchor));
    }

    [Fact]
    public void Setext_heading_has_an_anchor()
    {
        const string source = "Title Here\n==========\n";

        Assert.Equal("title-here", Assert.Single(AnchorsOf(source)).Anchor);
    }

    [Fact]
    public void Headings_in_a_quote_are_anchored_too()
    {
        Assert.Equal("quoted", Assert.Single(AnchorsOf("> # Quoted\n")).Anchor);
    }

    [Fact]
    public void Find_gives_the_start_of_the_heading_for_a_percent_encoded_fragment()
    {
        const string source = "# Hello\n\n## Café\n";
        ImmutableArray<HeadingAnchor> anchors = AnchorsOf(source);

        Assert.Equal(source.IndexOf("## Café", StringComparison.Ordinal), HeadingAnchors.Find(anchors, "caf%C3%A9"));
    }

    [Fact]
    public void Find_ignores_case()
    {
        const string source = "# Hello World\n";

        Assert.Equal(0, HeadingAnchors.Find(AnchorsOf(source), "HELLO-WORLD"));
    }

    [Fact]
    public void Find_of_a_numbered_duplicate_gives_its_own_heading()
    {
        const string source = "# Intro\n\n# Intro\n";

        Assert.Equal(source.LastIndexOf('#'), HeadingAnchors.Find(AnchorsOf(source), "intro-1"));
    }

    [Fact]
    public void Find_gives_null_when_there_is_no_such_heading()
    {
        Assert.Null(HeadingAnchors.Find(AnchorsOf("# Hello\n"), "missing"));
    }
}
