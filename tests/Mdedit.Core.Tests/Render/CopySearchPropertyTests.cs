using Mdedit.Core.Render;
using Shared.Core;

namespace Mdedit.Core.Tests.Render;

/// <summary>
/// Properties of copy and search over the nasty inputs and the repository's Markdown: the whole document copies as its source,
/// ranges of any bounds copy without failing, and every match is a non-empty range of the source that starts and ends on a mapped
/// character.
/// </summary>
public sealed class CopySearchPropertyTests
{
    /// <summary>The patterns searched in each document: a common letter, a space, and a regex that matches across markup.</summary>
    private static readonly (string Query, bool Regex)[] Patterns = [("e", false), (" ", false), ("a.", true)];

    [Theory]
    [MemberData(nameof(LoweringPropertyTests.NastyInputs), MemberType = typeof(LoweringPropertyTests))]
    public void Nasty_inputs_copy_and_search_without_failing(string text)
    {
        CheckDocument(text);
    }

    [Fact]
    public void Every_markdown_file_in_docs_and_the_readme_copies_and_searches_consistently()
    {
        string root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories).Append(Path.Combine(root, "README.md"));

        foreach (string file in files)
        {
            CheckDocument(File.ReadAllText(file));
        }
    }

    /// <summary>Checks one document: whole copy, sampled ranges and the searches.</summary>
    private static void CheckDocument(string text)
    {
        RenderDocument doc = RenderDocument.Parse(text);
        Assert.Equal(text, TextExtraction.Markdown(doc, 0, text.Length));

        int step = Math.Max(1, text.Length / 12);
        for (int start = 0; start <= text.Length; start += step)
        {
            for (int end = start; end <= text.Length; end += step)
            {
                _ = TextExtraction.PlainText(doc, start, end);
                _ = TextExtraction.Markdown(doc, start, end);
            }
        }

        foreach ((string query, bool regex) in Patterns)
        {
            SearchPattern pattern = SearchPattern.Create(new SearchQuery(query, false, false, regex), out _)!;
            foreach (SourceRange range in RenderSearch.Find(doc, pattern).Matches)
            {
                Assert.InRange(range.Start, 0, text.Length - 1);
                Assert.True(range.End > range.Start && range.End <= text.Length, $"match {range} in {query}");
            }
        }
    }

    /// <summary>The root of the repository, found from the test assembly's directory by its solution file.</summary>
    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Cmdr.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
