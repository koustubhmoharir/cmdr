using System.Globalization;
using System.Text.RegularExpressions;
using Mdedit.Core.Render;
using Shared.Core;

namespace Mdedit.Core.Tests.Render;

/// <summary>
/// Properties that hold for every document, checked over the repository's own Markdown and over synthetic inputs that
/// stress the offsets: every mapped cell's offset holds that cell's first character in the source (an entity's cells hold
/// the ampersand, and a space that stands for a line break holds the line break), every cell is one grapheme cluster, and
/// the cell's width is its display width.
/// </summary>
public sealed class LoweringPropertyTests
{
    private static readonly Regex EntityAt = new(@"\G&(?:#[0-9]{1,7}|#[xX][0-9a-fA-F]{1,6}|[A-Za-z][A-Za-z0-9]{1,31});", RegexOptions.CultureInvariant);

    /// <summary>The nasty inputs: CRLF, lone CR, ZWJ emoji, CJK, combining marks, NUL, a BOM, escapes, hard breaks, and every block kind.</summary>
    public static IEnumerable<object[]> NastyInputs() => new[]
    {
        new object[] { "# T\r\n\r\nPara *em* `c`\r\nline2\r\n\r\n- a\r\n- b\r\n\r\n> q\r\n> r\r\n\r\n```\r\nx\r\n```\r\n" },
        new object[] { "a\rb *c*\r\rd  \re\\\rf\r\n> [!TIP]\r> t\r" },
        new object[] { "👨\u200D👩\u200D👧 *x* 👍🏽 e\u0301 *a*\u0301 b\n\n- ✨ item\n" },
        new object[] { "中文 **粗体** 字符 [链接](https://例.example)\n\n| 名 | 字 |\n|---|---|\n| 1 | 2 |\n" },
        new object[] { "e\u0301\u0302 é\u0301 x\u200Dy\n\n`ab\u0301`\n" },
        new object[] { "a\0b\n\n    c\0d\n\n`x\0y`\n\n\0\n" },
        new object[] { "&amp;&copy;&#x1F600; &notanentity; \\*\\_\\`\\\\ \\a\n" },
        new object[] { "a\\\r\nb  \r\nc\n" },
        new object[] { "<b>x</b> <!-- c -->\n\n<div>\n</div>\n\n![i *j*](k)\n\n- [ ] t\n- [x] u\n  > q\n\n1. one\n1) two\n" },
        new object[] { "\uFEFFBOM text *x*\n" },
        new object[] { "[x]: http://y\n\n[x] and [y][x]\n" },
        new object[] { "Tab\there  \n\tindented tab code\n" },
        new object[] { "~~a~~ ~b~ __c__ _d_ `` ` `` ``x``\n" },
        new object[] { "> > > deep\n> >\n> > - item\n> >   more\n" },
        new object[] { "    code with \ttab\r\n\r\n\t\tdeeper\n" },
        new object[] { "" },
    };

    [Theory]
    [MemberData(nameof(NastyInputs))]
    public void Nasty_inputs_keep_every_mapped_cell_on_its_source_character(string text)
    {
        Assert.Empty(Violations(RenderDocument.Parse(text)));
    }

    [Fact]
    public void Every_markdown_file_in_docs_and_the_readme_keeps_every_mapped_cell_on_its_source_character()
    {
        string root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories).Append(Path.Combine(root, "README.md")).ToList();
        int mapped = 0;

        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            RenderDocument document = RenderDocument.Parse(File.ReadAllText(file));
            mapped += document.Blocks.Sum(block => block.Runs.Sum(run => run.Cells.Count(cell => cell.Offset >= 0)));

            List<string> violations = Violations(document);
            Assert.True(violations.Count == 0, $"{Path.GetFileName(file)}: {string.Join("; ", violations.Take(5))}");
        }

        // The check is not vacuous: the files have a great many mapped cells.
        Assert.True(mapped > 1000, $"only {mapped} mapped cells");
    }

    /// <summary>The first few violations of the mapping rules in a document; empty when the document keeps them all.</summary>
    private static List<string> Violations(RenderDocument document)
    {
        var violations = new List<string>();
        string text = document.Text;
        foreach (RenderBlock block in document.Blocks)
        {
            foreach (Run run in block.Runs)
            {
                foreach (Cell cell in run.Cells)
                {
                    if (StringInfo.GetNextTextElementLength(cell.Text) != cell.Text.Length)
                    {
                        violations.Add($"cell '{cell.Text}' is not one grapheme cluster");
                    }

                    if (cell.Width != DisplayWidth.OfGrapheme(cell.Text.AsSpan()))
                    {
                        violations.Add($"cell '{cell.Text}' has width {cell.Width}");
                    }

                    if (cell.Offset < 0)
                    {
                        continue;
                    }

                    int offset = block.Start + cell.Offset;
                    if (offset < 0 || offset >= text.Length)
                    {
                        violations.Add($"cell '{cell.Text}' at offset {offset} is outside the source");
                        continue;
                    }

                    char source = text[offset];
                    if (source == '&' && EntityAt.IsMatch(text, offset))
                    {
                        continue;
                    }

                    if (cell.Text[0] == source || (cell.Text == " " && source is '\r' or '\n'))
                    {
                        continue;
                    }

                    violations.Add($"cell '{cell.Text}' at offset {offset} but the source has '{source}'");
                }
            }
        }

        return violations;
    }

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Cmdr.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("Cmdr.slnx was not found above the test assembly.");
    }
}
