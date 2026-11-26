using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The classification of a link's destination: anchors, external URLs, and file paths with their fragments.</summary>
public sealed class LinkTargetsTests
{
    [Theory]
    [InlineData("#a", "a")]
    [InlineData("#", "")]
    [InlineData("#caf%C3%A9", "caf%C3%A9")]
    public void Fragment_only_URL_is_an_anchor_with_its_fragment_as_written(string url, string fragment)
    {
        Assert.Equal(new LinkTarget.Anchor(fragment), LinkTargets.Classify(url));
    }

    [Theory]
    [InlineData("a.md", "a.md", null)]
    [InlineData("a.md#b", "a.md", "b")]
    [InlineData("../docs/a%20b.md", "../docs/a b.md", null)]
    [InlineData("a.md?x=1#y", "a.md", "y")]
    [InlineData("a.md#b?c", "a.md", "b?c")]
    [InlineData("C:\\x.md", "C:\\x.md", null)]
    [InlineData("C:/x.md", "C:/x.md", null)]
    public void Relative_or_drive_path_is_a_file_with_its_path_decoded_and_its_fragment(string url, string path, string? fragment)
    {
        Assert.Equal(new LinkTarget.File(path, fragment), LinkTargets.Classify(url));
    }

    [Theory]
    [InlineData("http://x")]
    [InlineData("https://x")]
    [InlineData("mailto:a@b")]
    [InlineData("ftp:x")]
    [InlineData("file:///x")]
    [InlineData("//host/x")]
    public void URL_with_a_scheme_or_a_network_path_is_external(string url)
    {
        Assert.Equal(new LinkTarget.External(url), LinkTargets.Classify(url));
    }

    [Fact]
    public void Empty_URL_is_external_and_empty()
    {
        Assert.Equal(new LinkTarget.External(""), LinkTargets.Classify(""));
    }
}
