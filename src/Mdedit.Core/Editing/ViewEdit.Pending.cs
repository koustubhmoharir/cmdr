using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// Typing into a pending block: the pending's source and the typed character are written as one edit, and the block they make is verified against
/// the form the pending was made from (see <see cref="ViewEdit.Enter"/>).
/// </summary>
public static partial class ViewEdit
{
    /// <summary>
    /// Types into a pending block below or above the caret's block. A below pending writes its source at the caret and then the escaped text. An
    /// above pending writes the escaped text at the block's start, then its tail, which carries the separator and the block's own line prefix.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="caret">The caret's offset, where the pending is held.</param>
    /// <param name="typed">The typed character.</param>
    /// <param name="held">The pending block.</param>
    /// <remarks>A typed space would not show, so the pending block is returned unchanged: nothing is written and nothing is refused.</remarks>
    private static ViewEditResult InsertBlock(RenderDocument doc, int caret, string typed, ViewEditResult.Pending held)
    {
        if (typed == " ")
        {
            return held;
        }

        if (EditSite.Find(doc, caret) is not { } site)
        {
            return Refuse(EditMessages.NoPlace);
        }

        int index = site.BlockIndex;
        RenderBlock block = doc.Blocks[index];
        int n = block.Containers.Length;
        bool item = n > 0 && IsItem(block.Containers[^1]);
        bool quote = n > 0 && IsQuote(block.Containers[^1]);
        List<BlockShape> expected = BlockShape.Of(doc).ToList();

        if (held.Placement == PendingPlacement.Below)
        {
            Form? match = new[] { item ? BelowForm(doc, index, true, n - 1) : (Form?)null, BelowForm(doc, index, false, n), item || quote ? BelowForm(doc, index, false, n - 1) : null }
                .FirstOrDefault(form => form is { } candidate && candidate.Source == held.SourceText);
            string escaped = Escaper.Escape(typed, LastLine(held.SourceText), raw: false);
            int end = caret + held.SourceText.Length + escaped.Length;
            if (match is not { } form)
            {
                return LenientInsert(doc, caret, held.SourceText + escaped, index + 1, typed, expected, end);
            }

            expected.Insert(index + 1, new BlockShape(BlockKind.Paragraph, 0, form.Chain, typed));
            return Commit(doc, caret, 0, held.SourceText + escaped, end, expected);
        }

        Form? above = held.SourceText.Length > 0 ? null : new[] { item && block.IsFirstInItem ? AboveForm(doc, index, true) : null, (Form?)AboveForm(doc, index, false) }
            .FirstOrDefault(form => form is { } candidate && candidate.Tail == held.Tail);
        int at = block.Start;
        string escapedAbove = Escaper.Escape(typed, EditSite.LinePrefix(doc.Text, at) + held.SourceText, raw: false);
        int caretAbove = at + held.SourceText.Length + escapedAbove.Length;
        if (above is not { } shape)
        {
            return LenientInsert(doc, at, held.SourceText + escapedAbove + held.Tail, index, typed, expected, caretAbove);
        }

        expected.Insert(index, new BlockShape(BlockKind.Paragraph, 0, shape.Chain, typed));
        return Commit(doc, at, 0, held.SourceText + escapedAbove + held.Tail, caretAbove, expected);
    }

    /// <summary>The last line of a source text: the text after its last line break.</summary>
    private static string LastLine(string text)
    {
        int break_ = text.LastIndexOfAny(['\r', '\n']);
        return break_ < 0 ? text : text[(break_ + 1)..];
    }

    /// <summary>
    /// Types into the held block of a document that has no block (its only block was removed): the held source and the escaped text are written,
    /// and the result must be one block showing exactly the typed text. A typed space leaves the pending unchanged, as it does in a block.
    /// </summary>
    /// <param name="doc">The document, which has no block.</param>
    /// <param name="caret">The offset the held block is at.</param>
    /// <param name="typed">The typed character.</param>
    /// <param name="held">The inline pending the removal held.</param>
    private static ViewEditResult InsertIntoEmpty(RenderDocument doc, int caret, string typed, ViewEditResult.Pending held)
    {
        if (typed == " ")
        {
            return held;
        }

        string marker = Regex.Replace(held.SourceText, @"^#{1,6} ", string.Empty);
        string written = held.SourceText + Escaper.Escape(typed, marker, raw: false);
        string after = doc.Text.Insert(caret, written);
        ImmutableArray<BlockShape> shapes = BlockShape.Of(RenderDocument.Parse(after));
        if (shapes.Length != 1 || shapes[0].Text != typed)
        {
            return Refuse(EditMessages.CantEdit);
        }

        (int start, int length, string text) = TextChange.Of(doc.Text, after);
        return new ViewEditResult.Replacement(start, length, text, caret + written.Length);
    }
}
