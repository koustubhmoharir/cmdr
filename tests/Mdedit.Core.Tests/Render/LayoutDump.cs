using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>Builds layouts for the tests and renders their lines as text, one string per display line.</summary>
internal static class LayoutDump
{
    /// <summary>The text of every line of a document laid out at a width, with a fresh cache.</summary>
    public static string[] Lines(string text, int width = 40)
    {
        DocumentLayout layout = Build(text, width);
        return Enumerable.Range(0, layout.LineCount).Select(layout.TextOf).ToArray();
    }

    /// <summary>The document layout of a text at a width, with a fresh cache.</summary>
    public static DocumentLayout Build(string text, int width = 40, LayoutCache? cache = null) =>
        DocumentLayout.Build(RenderDocument.Parse(text), width, cache ?? new LayoutCache());

    /// <summary>A line as <c>text@offset</c> cells, using the document's absolute offsets, <c>-</c> when synthetic, and <c>/Role</c> for the roles other than body.</summary>
    public static string Cells(DocumentLayout layout, int line)
    {
        int baseOffset = layout.BaseOffset(line);
        return string.Join("|", layout.Line(line).Cells.Select(cell =>
            cell.Text + "@" + (!cell.IsMapped ? "-" : (baseOffset + cell.Offset).ToString()) +
            (cell.Style.Role == Mdedit.Core.Render.StyleRole.Body ? string.Empty : "/" + cell.Style.Role)));
    }
}
