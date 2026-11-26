using Mdedit.Core.Render;

namespace Mdedit.Core.Tables;

/// <summary>Helpers that take the text of a source span.</summary>
internal static class TableText
{
    /// <summary>The text of <paramref name="range"/> in <paramref name="text"/>.</summary>
    public static string Slice(this SourceRange range, string text) => text.Substring(range.Start, range.End - range.Start);

    /// <summary>The plain text of a table cell as the document shows it: its runs' text, concatenated (as the plain text copy does).</summary>
    public static string PlainOf(Run cell) => string.Concat(cell.Cells.Select(c => c.Text));
}
