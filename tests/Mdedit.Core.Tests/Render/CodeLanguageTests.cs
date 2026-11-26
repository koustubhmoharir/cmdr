using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The alias table of <see cref="CodeLanguages.Normalize"/>: one row per alias, case-insensitive, first word only.</summary>
public sealed class CodeLanguageTests
{
    [Theory]
    [InlineData("cs", "csharp")]
    [InlineData("c#", "csharp")]
    [InlineData("csharp", "csharp")]
    [InlineData("go", "go")]
    [InlineData("golang", "go")]
    [InlineData("py", "python")]
    [InlineData("python", "python")]
    [InlineData("ts", "typescript")]
    [InlineData("tsx", "typescript")]
    [InlineData("typescript", "typescript")]
    [InlineData("js", "javascript")]
    [InlineData("jsx", "javascript")]
    [InlineData("mjs", "javascript")]
    [InlineData("cjs", "javascript")]
    [InlineData("javascript", "javascript")]
    [InlineData("html", "html")]
    [InlineData("htm", "html")]
    [InlineData("css", "css")]
    [InlineData("xml", "xml")]
    [InlineData("xaml", "xml")]
    [InlineData("csproj", "xml")]
    [InlineData("svg", "xml")]
    [InlineData("json", "json")]
    [InlineData("jsonc", "json")]
    [InlineData("json5", "json")]
    [InlineData("sh", "shell")]
    [InlineData("bash", "shell")]
    [InlineData("shell", "shell")]
    [InlineData("zsh", "shell")]
    [InlineData("ps1", "powershell")]
    [InlineData("powershell", "powershell")]
    [InlineData("pwsh", "powershell")]
    [InlineData("yaml", "yaml")]
    [InlineData("yml", "yaml")]
    [InlineData("sql", "sql")]
    [InlineData("md", "markdown")]
    [InlineData("markdown", "markdown")]
    public void Each_alias_maps_to_its_canonical_id(string alias, string id)
    {
        Assert.Equal(id, CodeLanguages.Normalize(alias));
        Assert.Equal(id, CodeLanguages.Normalize(alias.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("csharp {.class}", "csharp")]
    [InlineData("  Py  extra", "python")]
    [InlineData("Rust", "rust")]
    [InlineData("{.python}", "{.python}")]
    public void Only_the_first_word_counts_and_an_unknown_word_is_lower_cased(string info, string id)
    {
        Assert.Equal(id, CodeLanguages.Normalize(info));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_or_blank_info_string_has_no_language(string? info)
    {
        Assert.Null(CodeLanguages.Normalize(info));
    }
}
