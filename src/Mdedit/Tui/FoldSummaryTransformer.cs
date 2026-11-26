using Mdedit.Core.Render;
using Terminal.Gui.Drawing;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Editor.Document.Folding;
using Terminal.Gui.Editor.Rendering;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Mdedit.Tui;

/// <summary>
/// Draws the fold summary of the editor: <c>… 3 lines</c> after the text of a folded heading or code block's fence line, as the view draws it
/// (see <c>BlockLayouter.Summary</c>). The editor's own marker, <c>FoldingTransformer</c>, replaces the text from a folding's start, and a
/// folding starts at the end of its head line, so it has nothing to replace there and draws no marker; this transformer adds one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where.</b> A visual line gets the marker when a folded folding starts at the end of its document line and the visual line reaches that
/// end, so that with word wrap the marker is on the last segment of the line only. A line that already has a fold marker is left alone.
/// </para>
/// <para>
/// <b>Count.</b> The count is the number of lines the view hides for the same unit, given by <paramref name="hiddenLines"/>; it is not the
/// number of lines the editor hides, which for a code block includes the closing fence.
/// </para>
/// </remarks>
/// <param name="folds">The editor's folding manager.</param>
/// <param name="hiddenLines">The number of lines the view hides for the unit of a folding, or 0 when it is not known.</param>
/// <param name="normal">The editor's normal attribute, whose background the summary keeps.</param>
internal sealed class FoldSummaryTransformer(FoldingManager folds, Func<FoldingSection, int> hiddenLines, Func<Attribute> normal) : IVisualLineTransformer
{
    /// <summary>Adds the summary of a folded folding that starts at the end of the line, when the line reaches that end.</summary>
    /// <param name="line">The visual line, which may be one segment of a wrapped document line.</param>
    public void Transform(CellVisualLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        DocumentLine documentLine = line.DocumentLine;
        int lineEnd = documentLine.Offset + documentLine.Length;
        IReadOnlyList<CellVisualLineElement> elements = line.Elements;
        if (elements.Any(element => element is FoldingMarkerElement))
        {
            return;
        }

        // A segment that stops before the line's end is not the last one, and the summary belongs to the last.
        if (elements.Count > 0 && elements[^1].DocumentEndOffset != lineEnd)
        {
            return;
        }

        FoldingSection? folding = folds.AllFoldings.FirstOrDefault(candidate => candidate.IsFolded && candidate.StartOffset == lineEnd);
        if (folding is null)
        {
            return;
        }

        int hidden = hiddenLines(folding);
        if (hidden <= 0)
        {
            return;
        }

        int column = elements.Count > 0 ? elements[^1].VisualEndColumn : 0;
        string title = $" … {hidden} {(hidden == 1 ? "line" : "lines")}";
        Attribute attribute = MdColors.For(new CellStyle(StyleRole.FoldSummary), normal());
        line.AddElement(new FoldingMarkerElement(folding.StartOffset, folding.EndOffset - folding.StartOffset, column, attribute, title));
    }
}
