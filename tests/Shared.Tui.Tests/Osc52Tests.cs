using System.Text;
using Shared.Tui;

namespace Shared.Tui.Tests;

public sealed class Osc52Tests
{
    [Fact]
    public void Build_writes_the_exact_bytes_for_ascii_text()
    {
        Assert.Equal("\u001b]52;c;aGk=\u0007", Osc52.Build("hi"));
    }

    [Fact]
    public void Build_encodes_non_ascii_text_as_utf8()
    {
        // "é€" is C3 A9 E2 82 AC in UTF-8, which is "w6nigqw=" in base64.
        Assert.Equal("\u001b]52;c;w6nigqw=\u0007", Osc52.Build("é€"));
    }

    [Fact]
    public void Build_of_empty_text_has_an_empty_payload()
    {
        Assert.Equal("\u001b]52;c;\u0007", Osc52.Build(string.Empty));
    }

    [Fact]
    public void Payload_decodes_back_to_the_text_including_newlines()
    {
        const string text = "line one\nline two 中";
        string sequence = Osc52.Build(text);
        string payload = sequence["\u001b]52;c;".Length..^1];

        Assert.Equal(text, Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
    }

    [Fact]
    public void Fits_allows_exactly_the_maximum_byte_count_and_refuses_one_more()
    {
        string atLimit = new('a', Osc52.MaxBytes);
        string overLimit = atLimit + "a";

        Assert.True(Osc52.Fits(atLimit));
        Assert.False(Osc52.Fits(overLimit));
    }

    [Fact]
    public void Fits_counts_utf8_bytes_not_characters()
    {
        // 3 bytes per character: this has fewer characters than the limit but more bytes.
        string text = new('中', Osc52.MaxBytes / 3 + 1);

        Assert.True(text.Length < Osc52.MaxBytes);
        Assert.False(Osc52.Fits(text));
    }
}
