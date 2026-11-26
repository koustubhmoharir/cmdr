namespace Mdedit.Core.Render;

/// <summary>
/// Maps the language named by a fenced code block's info string to a canonical id, so that a colorizer needs one name per
/// language and the header shows the name as written.
/// </summary>
public static class CodeLanguages
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "csharp",
        ["c#"] = "csharp",
        ["csharp"] = "csharp",
        ["go"] = "go",
        ["golang"] = "go",
        ["py"] = "python",
        ["python"] = "python",
        ["ts"] = "typescript",
        ["tsx"] = "typescript",
        ["typescript"] = "typescript",
        ["js"] = "javascript",
        ["jsx"] = "javascript",
        ["mjs"] = "javascript",
        ["cjs"] = "javascript",
        ["javascript"] = "javascript",
        ["html"] = "html",
        ["htm"] = "html",
        ["css"] = "css",
        ["xml"] = "xml",
        ["xaml"] = "xml",
        ["csproj"] = "xml",
        ["svg"] = "xml",
        ["json"] = "json",
        ["jsonc"] = "json",
        ["json5"] = "json",
        ["sh"] = "shell",
        ["bash"] = "shell",
        ["shell"] = "shell",
        ["zsh"] = "shell",
        ["ps1"] = "powershell",
        ["powershell"] = "powershell",
        ["pwsh"] = "powershell",
        ["yaml"] = "yaml",
        ["yml"] = "yaml",
        ["sql"] = "sql",
        ["md"] = "markdown",
        ["markdown"] = "markdown",
    };

    /// <summary>
    /// The canonical id of the language named by an info string. Only the first word of the info string counts, so
    /// <c>csharp {.class}</c> is <c>csharp</c>. The match is case-insensitive. A word that is not in the alias table is returned
    /// lower-cased, as written.
    /// </summary>
    /// <param name="info">The info string of a fenced code block, or null.</param>
    /// <returns>The id, or null when the info string is missing, empty or only white space.</returns>
    public static string? Normalize(string? info)
    {
        string? word = FirstWord(info);
        if (word is null)
        {
            return null;
        }

        return Aliases.TryGetValue(word, out string? id) ? id : word.ToLowerInvariant();
    }

    /// <summary>
    /// The first word of an info string as written: the text up to the first white space character, or null when there is none.
    /// </summary>
    /// <param name="info">The info string, or null.</param>
    internal static string? FirstWord(string? info)
    {
        if (string.IsNullOrWhiteSpace(info))
        {
            return null;
        }

        string trimmed = info.TrimStart();
        int end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
        {
            end++;
        }

        return trimmed[..end];
    }
}
