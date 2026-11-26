using Mdedit.Core.Render;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Editor.Document.Folding;

namespace Mdedit.Tui;

/// <summary>
/// The editor's folding strategy: one folding for each fold unit of the Markdown in the buffer, as the view folds them. A section folds from
/// the end of its heading's first line to the end of its last block, so the heading line stays visible; a code block folds from the end of
/// its fence line to the end of its last line (its closing fence, or the end of the block when it has none).
/// </summary>
/// <remarks>
/// <para>
/// <b>State.</b> A folding is identified by the unit's start, held as an <see cref="AnchorOffset"/> in its <see cref="FoldingSection.Tag"/>:
/// the anchor moves with the edits, so a unit whose start survives keeps its folded state across an update. A unit whose start is gone is
/// dropped with its state; a new unit starts unfolded.
/// </para>
/// <para>
/// <b>Parsing.</b> <see cref="UpdateFoldings"/> parses the buffer's text (without colouring), so its units are current. The window decides when
/// to call it (see <see cref="EditorFolds"/>); the editor does not call it by itself.
/// </para>
/// </remarks>
internal sealed class MarkdownFoldingStrategy : IFoldingStrategy
{
    /// <summary>Recomputes the foldings of the buffer's Markdown and keeps the state of the units whose starts survive.</summary>
    /// <param name="manager">The folding manager of the editor, which receives the new foldings.</param>
    /// <param name="document">The buffer.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(document);

        var previous = new Dictionary<int, AnchorOffset>();
        var folded = new HashSet<int>();
        foreach (FoldingSection folding in manager.AllFoldings)
        {
            if (folding.Tag is AnchorOffset { IsDeleted: false } anchor)
            {
                previous.TryAdd(anchor.Offset, anchor);
                if (folding.IsFolded)
                {
                    folded.Add(anchor.Offset);
                }
            }
        }

        RenderDocument render = RenderDocument.Parse(document.Text);
        var newFoldings = new List<NewFolding>();
        var units = new Dictionary<int, (AnchorOffset Anchor, bool Folded)>();
        foreach (FoldUnit unit in render.FoldUnits)
        {
            (int start, int end) = Range(render, unit);
            if (end <= start)
            {
                continue;
            }

            AnchorOffset anchor = previous.TryGetValue(unit.Start, out AnchorOffset? kept)
                ? kept
                : new AnchorOffset(document.CreateAnchor(unit.Start));
            bool isFolded = folded.Contains(unit.Start);
            newFoldings.Add(new NewFolding(start, end) { Name = Name(render, unit), DefaultClosed = isFolded });
            units[start] = (anchor, isFolded);
        }

        manager.UpdateFoldings(newFoldings, -1);
        foreach (FoldingSection folding in manager.AllFoldings)
        {
            if (units.TryGetValue(folding.StartOffset, out (AnchorOffset Anchor, bool Folded) unit))
            {
                folding.Tag = unit.Anchor;
                folding.IsFolded = unit.Folded;
            }
        }
    }

    /// <summary>
    /// Whether a change may add or remove a folding: any change that is not only letters. A letter cannot start or end a block, a container or a
    /// fence, so typing words needs no update, while a space, a marker or a line break may.
    /// </summary>
    /// <param name="args">The change.</param>
    public bool ChangeMayAffectFoldings(DocumentChangeEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return MayChangeStructure(args.InsertedText.Text, args.RemovedText.Text);
    }

    /// <summary>
    /// Whether a change that inserts and removes the given text may add or remove a folding (see <see cref="ChangeMayAffectFoldings"/>). The
    /// editor's text changes are read as text, so the window can ask it before the change is applied.
    /// </summary>
    /// <param name="inserted">The inserted text.</param>
    /// <param name="removed">The removed text.</param>
    public static bool MayChangeStructure(string inserted, string removed) => HasNonLetter(inserted) || HasNonLetter(removed);

    /// <summary>The offsets a folding covers: from the end of the unit's head line to the end of its last line, without the line breaks.</summary>
    private static (int Start, int End) Range(RenderDocument render, FoldUnit unit)
    {
        RenderBlock head = render.Blocks[unit.HeadBlock];
        RenderBlock last = render.Blocks[unit.EndBlock - 1];
        int start = LineEnd(render.Text, head.Start, head.End);
        int end = last.End;
        while (end > start && end <= render.Text.Length && (render.Text[end - 1] == '\n' || render.Text[end - 1] == '\r'))
        {
            end--;
        }

        return (start, end);
    }

    /// <summary>The text of the unit's head line: the heading, or the code block's fence line.</summary>
    private static string Name(RenderDocument render, FoldUnit unit)
    {
        RenderBlock head = render.Blocks[unit.HeadBlock];
        return render.Text.Substring(head.Start, LineEnd(render.Text, head.Start, head.End) - head.Start);
    }

    /// <summary>The offset of the first line break in a range, or the end of the range when it has none.</summary>
    private static int LineEnd(string text, int start, int end)
    {
        int limit = Math.Min(end, text.Length);
        int index = text.IndexOfAny(['\r', '\n'], start, Math.Max(0, limit - start));
        return index < 0 ? limit : index;
    }

    /// <summary>Whether a text has a character that is not a letter.</summary>
    private static bool HasNonLetter(string text)
    {
        foreach (char ch in text)
        {
            if (!char.IsLetter(ch))
            {
                return true;
            }
        }

        return false;
    }
}
