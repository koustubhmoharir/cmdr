using Mdedit.Tui;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Editor.Document.Folding;

namespace Mdedit.Tests;

/// <summary>
/// The editor's foldings of a Markdown buffer: one folding per fold unit with the spec's offsets (a section from the end of its heading's first
/// line to the end of its last block, a code block from the end of its fence line to the end of its closing fence line), the state kept across
/// an update while the unit's start survives, and the change that may affect the foldings.
/// </summary>
public sealed class MarkdownFoldingStrategyTests
{
    // A section that holds a sub-section, and a list at the end of the document.
    private const string Sections = "# Install\n\nIntro text.\n\n## Linux\n\n- step one\n- step two\n";

    // A code block between two paragraphs.
    private const string Code = "Intro.\n\n```csharp\nint x = 1;\nint y = 2;\n```\n\nAfter.\n";

    [Fact]
    public void A_section_folds_from_the_end_of_its_heading_line_to_the_end_of_its_last_block()
    {
        List<(int Start, int End)> foldings = Foldings(Sections);

        // The top section holds the sub-section, so it runs to the end of "- step two"; the sub-section ends there too.
        int install = Sections.IndexOf("# Install", StringComparison.Ordinal) + "# Install".Length;
        int linux = Sections.IndexOf("## Linux", StringComparison.Ordinal) + "## Linux".Length;
        int end = Sections.Length - 1;
        Assert.Equal([(install, end), (linux, end)], foldings);
    }

    [Fact]
    public void A_code_block_folds_from_the_end_of_its_fence_line_to_the_end_of_its_closing_fence_line()
    {
        List<(int Start, int End)> foldings = Foldings(Code);

        int start = Code.IndexOf("```csharp", StringComparison.Ordinal) + "```csharp".Length;
        int closing = Code.IndexOf("\n```\n", StringComparison.Ordinal) + 1;
        Assert.Equal([(start, closing + 3)], foldings);
    }

    [Fact]
    public void A_heading_with_no_body_has_no_folding()
    {
        Assert.Empty(Foldings("# Alone\n"));
    }

    [Fact]
    public void A_fold_state_is_kept_by_an_update_while_the_unit_start_survives()
    {
        var document = new TextDocument(Sections);
        var manager = new FoldingManager(document);
        var strategy = new MarkdownFoldingStrategy();
        strategy.UpdateFoldings(manager, document);
        manager.AllFoldings.First().IsFolded = true;

        // Text typed after the top heading moves the unit's start with it, so the folded state stays.
        document.Insert(document.Text.IndexOf("Intro", StringComparison.Ordinal), "Hi there. ");
        strategy.UpdateFoldings(manager, document);

        List<FoldingSection> all = manager.AllFoldings.ToList();
        Assert.Equal(2, all.Count);
        Assert.True(all[0].IsFolded);
        Assert.False(all[1].IsFolded);
    }

    [Fact]
    public void Letters_do_not_affect_the_foldings_and_structural_characters_do()
    {
        Assert.False(MarkdownFoldingStrategy.MayChangeStructure("word", string.Empty));
        Assert.True(MarkdownFoldingStrategy.MayChangeStructure(" ", string.Empty));
        Assert.True(MarkdownFoldingStrategy.MayChangeStructure(string.Empty, "#"));
        Assert.True(MarkdownFoldingStrategy.MayChangeStructure("\n", string.Empty));
    }

    private static List<(int Start, int End)> Foldings(string text)
    {
        var document = new TextDocument(text);
        var manager = new FoldingManager(document);
        new MarkdownFoldingStrategy().UpdateFoldings(manager, document);
        return manager.AllFoldings.Select(folding => (folding.StartOffset, folding.EndOffset)).ToList();
    }
}
