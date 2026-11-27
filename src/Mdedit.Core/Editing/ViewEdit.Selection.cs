using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// A selection across the lines of one block: the cells it covers are removed with the line breaks between them, and typed text takes the place
/// where it starts.
/// </summary>
public static partial class ViewEdit
{
    /// <summary>
    /// Replaces a selection that spans several runs (lines) of one block. The source of the cells is removed with the line breaks between the
    /// runs, the typed text (escaped as typing there would be) takes its place, and the verification compares the rendered text with the text the
    /// selection leaves.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="index">The block the selection is in.</param>
    /// <param name="start">The selection's start offset.</param>
    /// <param name="end">The selection's end offset.</param>
    /// <param name="selected">The selected cells, as (run, cell), in order.</param>
    /// <param name="typed">The text that takes the selection's place; empty deletes it.</param>
    private static ViewEditResult ReplaceAcrossLines(RenderDocument doc, int index, int start, int end, List<(int Run, int Cell)> selected, string typed)
    {
        RenderBlock block = doc.Blocks[index];
        int firstRun = selected[0].Run;
        int lastRun = selected[^1].Run;
        var breaks = new List<SourceRange>();
        for (int run = firstRun; run < lastRun; run++)
        {
            breaks.Add(new SourceRange(RunEnd(doc, block, run), NextLineStart(doc, block, run + 1)));
        }

        RemovalSet removals = RemovalSet.Of(doc, block, selected, trimEdges: typed.Length == 0, breaks);
        int from = EditSite.EscapeStart(doc, block, block.Start + block.Runs[firstRun].Cells[selected[0].Cell].Offset);
        string before = removals.Apply(doc.Text);
        int anchor = removals.Shift(from);
        string inserted = typed.Length == 0 ? string.Empty : Escaper.Escape(typed, EditSite.LinePrefix(before, anchor), EditSite.IsRaw(block, from));
        string after = before.Insert(anchor, inserted);

        string rendered = BlockText.Without(block, start, end, typed);
        if (typed.Length == 0 && rendered.Length == 0)
        {
            return RemoveBlock(doc, index);
        }

        if (typed.Length == 0 && block.Kind is BlockKind.Paragraph or BlockKind.Heading)
        {
            rendered = rendered.Trim(' ');
        }

        var expected = BlockShape.Of(doc).ToList();
        expected[index] = expected[index] with { Text = rendered };
        return CommitText(doc, after, anchor + inserted.Length, expected);
    }

    /// <summary>The offset of the first character of a run's first text cell, or of the run's start when it has no text.</summary>
    private static int NextLineStart(RenderDocument doc, RenderBlock block, int run)
    {
        int cell = FirstTextCell(block.Runs[run]);
        return cell < block.Runs[run].Cells.Length ? block.Start + block.Runs[run].Cells[cell].Offset : block.Start + block.Runs[run].Start;
    }
}
