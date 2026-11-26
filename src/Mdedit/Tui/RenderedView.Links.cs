using System.Collections.Immutable;
using Mdedit.Core.Render;
using Cell = Mdedit.Core.Render.Cell;

namespace Mdedit.Tui;

/// <summary>
/// The links of the view: the link under the caret, Tab and Shift+Tab between the links, the Ctrl+click that follows one
/// (<see cref="LinkActivated"/>), and the placement of a caret and a top line that an anchor or a Back asks for (<see cref="GoTo"/>).
/// A link is found through its cells: a mapped cell whose <see cref="Cell.LinkId"/> names a link of its block. Only the lines of the layout
/// are looked at, so a link that a fold hides is not a stop.
/// </summary>
internal sealed partial class RenderedView
{
    // The place that GoTo was asked for before the view had a width, taken when the first layout is built.
    private (int Caret, int Top)? _pendingGoTo;

    /// <summary>
    /// Raised by a Ctrl+click on a link cell, after the caret went to the clicked cell. The window follows the link. A plain click does not
    /// raise it.
    /// </summary>
    public event Action<LinkInfo>? LinkActivated;

    /// <summary>The link under the caret, or null when the caret is not on a link cell.</summary>
    public LinkInfo? CaretLink => HasCursor ? LinkAt(_line, _cell) : null;

    /// <summary>
    /// Moves the caret to the first cell of the next link (or of the previous one, when <paramref name="forward"/> is false) in document
    /// order. From a link, the next link is the one after it, and the previous one the one before it. Links that folds hide are skipped.
    /// Past the document's end the search wraps to the other end, and the result says so. The selection is cleared.
    /// </summary>
    /// <param name="forward">True for the next link, false for the previous one.</param>
    public LinkMove MoveToLink(bool forward)
    {
        if (!SyncLayout() || !HasCursor)
        {
            return LinkMove.NoLinks;
        }

        List<LinkStop> stops = LinkStops();
        if (stops.Count == 0)
        {
            return LinkMove.NoLinks;
        }

        (int Line, int Cell) reference = CaretLinkStart(stops) ?? (_line, _cell);
        int index = forward
            ? stops.FindIndex(stop => Before(reference, stop.Position))
            : stops.FindLastIndex(stop => Before(stop.Position, reference));
        bool wrapped = index < 0;
        LinkStop target = wrapped ? (forward ? stops[0] : stops[^1]) : stops[index];

        int before = _offset;
        int topBefore = _top;
        _anchor = null;
        _range = null;
        PlaceCell(target.Line, target.Cell, keepGoal: false);
        Settle(before, topBefore);
        return wrapped ? LinkMove.Wrapped : LinkMove.Moved;
    }

    /// <summary>
    /// Places the caret at a source offset and the top line at the line of another offset, as an anchor link and a Back do. A unit that hides
    /// the caret is unfolded (see <see cref="UnfoldHiding"/>), the top line is the line of <paramref name="topOffset"/> as far as the end of the
    /// document allows, and the caret's column is kept on screen. The caret's line is not scrolled into view: the top line holds it, or the
    /// caret stays where a Back left it. Before the view has a width the place is kept and taken by the first layout.
    /// </summary>
    /// <param name="caretOffset">The caret's absolute source offset.</param>
    /// <param name="topOffset">The absolute source offset whose line is the top line.</param>
    public void GoTo(int caretOffset, int topOffset)
    {
        if (!SyncLayout())
        {
            _pendingGoTo = (caretOffset, topOffset);
            return;
        }

        ApplyGoTo(caretOffset, topOffset);
    }

    // Places the caret and the top line of a GoTo in the built layout.
    private void ApplyGoTo(int caretOffset, int topOffset)
    {
        int before = _offset;
        int topBefore = _top;
        if (UnfoldHiding(caretOffset))
        {
            Reflow(Built.Width);
        }

        PlaceAt(caretOffset);
        _goal = _column;
        _anchor = null;
        _range = null;
        int line = ResolveCaret(topOffset) is { } stop ? stop.Line : Built.PositionOf(topOffset).Line;
        _top = Math.Clamp(line, 0, MaxTop());
        EnsureCaretColumnVisible();
        SyncTracking();
        UpdateCursor();
        SetNeedsDraw();
        if (_offset != before || _top != topBefore)
        {
            PositionChanged?.Invoke();
        }
    }

    // Applies the place that GoTo kept, when the first layout is built; returns false when there is none.
    private bool TakePendingGoTo()
    {
        if (_pendingGoTo is not { } pending)
        {
            return false;
        }

        _pendingGoTo = null;
        ApplyGoTo(pending.Caret, pending.Top);
        return true;
    }

    /// <summary>The link of a line's cell, or null when the cell is not a link cell of the document.</summary>
    private LinkInfo? LinkAt(int line, int cell)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        int block = BlockOfLine(line);
        if (cell < 0 || cell >= cells.Length || block < 0 || !IsLinkCell(cells[cell]))
        {
            return null;
        }

        return _document.Blocks[block].Links.FirstOrDefault(link => link.Id == cells[cell].LinkId);
    }

    /// <summary>
    /// The first caret stop of every link that the layout shows, in document order. A link across lines has its stop on its first line.
    /// </summary>
    private List<LinkStop> LinkStops()
    {
        var stops = new List<LinkStop>();
        var seen = new HashSet<(int Block, int Id)>();
        for (int line = 0; line < LineCount; line++)
        {
            int block = BlockOfLine(line);
            ImmutableArray<Cell> cells = Built.Line(line).Cells;
            for (int cell = 0; cell < cells.Length; cell++)
            {
                if (IsLinkCell(cells[cell]) && seen.Add((block, cells[cell].LinkId)))
                {
                    stops.Add(new LinkStop(line, cell, block, cells[cell].LinkId));
                }
            }
        }

        return stops;
    }

    /// <summary>The first stop of the link under the caret, or null when the caret is on no link.</summary>
    private (int Line, int Cell)? CaretLinkStart(List<LinkStop> stops)
    {
        if (CaretLinkKey() is not { } key)
        {
            return null;
        }

        foreach (LinkStop stop in stops)
        {
            if (stop.Block == key.Block && stop.Id == key.Id)
            {
                return stop.Position;
            }
        }

        return null;
    }

    // The block and link id of the caret's cell, or null when the caret is not on a link cell.
    private (int Block, int Id)? CaretLinkKey()
    {
        if (!HasCursor)
        {
            return null;
        }

        ImmutableArray<Cell> cells = Built.Line(_line).Cells;
        if (_cell < 0 || _cell >= cells.Length || !IsLinkCell(cells[_cell]))
        {
            return null;
        }

        return (BlockOfLine(_line), cells[_cell].LinkId);
    }

    /// <summary>Whether a cell is a caret stop of a link: mapped, and in a link.</summary>
    private static bool IsLinkCell(Cell cell) => cell.IsMapped && cell.LinkId >= 0;

    /// <summary>Whether a caret position comes before another in document order (by line, then by cell).</summary>
    private static bool Before((int Line, int Cell) a, (int Line, int Cell) b) => a.Line < b.Line || (a.Line == b.Line && a.Cell < b.Cell);

    /// <summary>The first caret stop of a link in the layout: the link's cell, in its block.</summary>
    /// <param name="Line">The display line.</param>
    /// <param name="Cell">The index of the cell in the line.</param>
    /// <param name="Block">The index of the block the link belongs to.</param>
    /// <param name="Id">The link's id in its block.</param>
    private readonly record struct LinkStop(int Line, int Cell, int Block, int Id)
    {
        /// <summary>The stop's position: its line and cell.</summary>
        public (int Line, int Cell) Position => (Line, Cell);
    }
}
