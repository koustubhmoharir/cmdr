using System.Text.Json;
using Logvu.Core.Filter;

namespace Logvu.Core.Tests.Filter;

public sealed class JsonFieldPathTests
{
    private static readonly JsonElement Root = JsonDocument.Parse(
        """{"status":503,"req":{"path":"/api","tags":["a","b"]},"ok":true,"nil":null}""").RootElement.Clone();

    [Theory]
    [InlineData("a")]
    [InlineData("a.b")]
    [InlineData("a.b.c")]
    [InlineData("a[0]")]
    [InlineData("a[0][12]")]
    [InlineData("[0]")]
    [InlineData("[0][1]")]
    [InlineData("a.b[3].c")]
    [InlineData("items[0].[1]")]
    public void Well_formed_paths_are_accepted(string path)
    {
        Assert.True(JsonFieldPath.IsWellFormed(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData(".a")]
    [InlineData("a.")]
    [InlineData("a..b")]
    [InlineData("a[]")]
    [InlineData("[]")]
    [InlineData("a[x]")]
    [InlineData("a[-1]")]
    [InlineData("a[+1]")]
    [InlineData("a[ 1]")]
    [InlineData("a[0")]
    [InlineData("a[")]
    [InlineData("a[0]b")]
    [InlineData("a[0]]")]
    [InlineData("a[0][1")]
    [InlineData("a[0[1]]")]
    public void Malformed_paths_are_rejected(string path)
    {
        Assert.False(JsonFieldPath.IsWellFormed(path));
    }

    [Theory]
    [InlineData("status", "503")]
    [InlineData("req.path", "/api")]
    [InlineData("req.tags[1]", "b")]
    [InlineData("req", """{"path":"/api","tags":["a","b"]}""")]
    [InlineData("req.tags", """["a","b"]""")]
    [InlineData("ok", "true")]
    public void Resolved_paths_give_their_text(string path, string text)
    {
        Assert.True(JsonFieldPath.TryGetText(Root, path, out string value));
        Assert.Equal(text, value);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("req.missing")]
    [InlineData("req.path.x")]
    [InlineData("req.tags[2]")]
    [InlineData("status[0]")]
    [InlineData("nil.x")]
    public void Unresolved_paths_are_false(string path)
    {
        Assert.False(JsonFieldPath.TryGetText(Root, path, out _));
    }
}
