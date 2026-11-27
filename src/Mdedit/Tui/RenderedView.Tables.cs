using Mdedit.Core.Render;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace Mdedit.Tui;

/// <summary>
/// The horizontal scrolling of the wide blocks (tables): each block whose lines are wider than the view keeps its own offset, keyed by its
/// absolute start. The offset moves the cells after a line's container prefix; the prefix stays in place. Screen columns are converted to
/// layout columns through the offset (<see cref="LayoutColumn"/>), so that the mouse and the caret work on the cells.
/// </summary>
/// <remarks>
/// <para>
/// <b>Range.</b> A block scrolls when one of its lines has more content (the cells after the prefix) than the view has columns after the
/// prefix. Its range is the widest such line's excess plus one: the one column of the left edge marker, so that the last cell is reached
/// with the edge marker in the first column.
/// </para>
/// <para>
/// <b>Markers.</b> A scrolled line with content hidden on the left shows <c>◂</c> in the first content column; with content hidden on the
/// right, <c>▸</c> in the last column. The cell under a marker is hidden.
/// </para>
/// <para>
/// <b>Lifetime.</b> A block that stops scrolling (it fits, or it is folded away) loses its offset; an offset past a new range is clamped.
/// </para>
/// </remarks>
internal sealed partial class RenderedView
{
    /// <summary>The columns a Shift+wheel, a horizontal wheel, a click on an edge marker or <c>&lt;</c> and <c>&gt;</c> scroll a table by.</summary>
    internal const int ScrollColumns = 8;

    // The largest offset of each scrolling block, by the block's start; a block that does not scroll has no entry.
    private readonly Dictionary<int, int> _maxShift = [];

    // The offset of each scrolling block, by the block's start, within its range.
    private readonly Dictionary<int, int> _shift = [];

    /// <summary>
    /// Builds the layout at a width with the current fold state, and fixes the scroll ranges of its blocks: the offsets of blocks that no
    /// longer scroll are dropped, and the others are kept within their new range.
    /// </summary>
    private DocumentLayout BuildLayout(int width)
    {
        DocumentLayout layout = DocumentLayout.Build(_document, width, _cache, _folded);
        SetScrollRanges(layout);
        return layout;
    }

    /// <summary>Fills the scroll ranges from a layout and prunes the offsets that no longer apply.</summary>
    private void SetScrollRanges(DocumentLayout layout)
    {
        _maxShift.Clear();
        for (int line = 0; line < layout.LineCount; line++)
        {
            int content = layout.ScrollWidthOf(line);
            if (content > 0)
            {
                int visible = layout.Width - PrefixWidthOf(layout.Line(line));
                int key = layout.BaseOffset(line);
                _maxShift[key] = Math.Max(_maxShift.GetValueOrDefault(key), content - visible + 1);
            }
        }

        foreach (int key in _shift.Keys.ToList())
        {
            if (_maxShift.TryGetValue(key, out int max))
            {
                _shift[key] = Math.Min(_shift[key], max);
            }
            else
            {
                _shift.Remove(key);
            }
        }
    }

    /// <summary>The width of a line's container prefix, in columns.</summary>
    private static int PrefixWidthOf(DisplayLine display)
    {
        int width = 0;
        for (int cell = 0; cell < display.PrefixCells && cell < display.Cells.Length; cell++)
        {
            width += display.Cells[cell].Width;
        }

        return width;
    }

    /// <summary>The width of a line's content: its cells after the container prefix, in columns.</summary>
    private static int ContentWidthOf(DisplayLine display)
    {
        int width = 0;
        for (int cell = display.PrefixCells; cell < display.Cells.Length; cell++)
        {
            width += display.Cells[cell].Width;
        }

        return width;
    }

    /// <summary>The horizontal offset of a line's block: 0 when the block does not scroll, or the line has no content (a blank separator).</summary>
    private int ShiftOf(int line)
    {
        int key = Built.BaseOffset(line);
        return _maxShift.ContainsKey(key) && ContentWidthOf(Built.Line(line)) > 0 ? _shift.GetValueOrDefault(key) : 0;
    }

    /// <summary>The offset that applies to a layout column of a line: 0 in the container prefix, else the block's offset.</summary>
    private int ShiftFor(int line, int column) => column < PrefixWidthOf(Built.Line(line)) ? 0 : ShiftOf(line);

    /// <summary>
    /// The layout column under a screen column of a line: the column itself in the prefix, else the column plus the block's offset. Mouse
    /// positions go through it, so a click or a drag on a scrolled table lands on the cell that is drawn there.
    /// </summary>
    /// <param name="line">The display line.</param>
    /// <param name="x">The screen column within the view, at least 0.</param>
    private int LayoutColumn(int line, int x) => x + ShiftFor(line, x);

    /// <summary>
    /// Whether the left and right edge markers of a line are drawn: the left one when the block is scrolled, the right one when content
    /// lies beyond the view's last column.
    /// </summary>
    private (bool Left, bool Right) MarkersOf(int line)
    {
        DisplayLine display = Built.Line(line);
        int content = ContentWidthOf(display);
        if (content == 0)
        {
            return (false, false);
        }

        int shift = ShiftOf(line);
        return (shift > 0, content > shift + (Built.Width - PrefixWidthOf(display)));
    }

    /// <summary>
    /// The screen column of the caret: its layout column less the offset of its block. Clamped to the view for a caret that is not
    /// scrolled out of it (as an end position on a full line is).
    /// </summary>
    private int CaretScreenColumn() => Math.Min(CaretRawColumn(), Math.Max(0, Viewport.Width - 1));

    /// <summary>The caret's layout column less the offset of its block: negative or past the view when the caret is scrolled out.</summary>
    private int CaretRawColumn() => _column - ShiftFor(_line, _column);

    /// <summary>Whether the caret is scrolled out of the view horizontally (only in a scrolled block; an unscrolled caret is always drawn).</summary>
    private bool CaretScrolledOut()
    {
        int raw = CaretRawColumn();
        return ShiftFor(_line, _column) > 0 && (raw < 0 || raw >= Viewport.Width);
    }

    /// <summary>
    /// Moves the offset of the block of a line by a number of columns, clamped to its range. Returns false, and changes nothing, when the
    /// line is not in a scrolling block.
    /// </summary>
    /// <param name="line">The display line whose block scrolls.</param>
    /// <param name="delta">The columns to move by; negative to the left.</param>
    private bool ScrollBlock(int line, int delta)
    {
        int key = Built.BaseOffset(line);
        if (!_maxShift.TryGetValue(key, out int max) || ContentWidthOf(Built.Line(line)) == 0)
        {
            return false;
        }

        _shift[key] = Math.Clamp(_shift.GetValueOrDefault(key) + delta, 0, max);
        SyncTracking();
        UpdateCursor();
        SetNeedsDraw();
        return true;
    }

    /// <summary>
    /// Moves the offset of the block under the cursor, for <c>&lt;</c> and <c>&gt;</c>. Returns false when the cursor's block does not scroll.
    /// </summary>
    /// <param name="delta">The columns to move by; negative to the left.</param>
    private bool ScrollCursorBlock(int delta) => HasCursor && ScrollBlock(_line, delta);

    /// <summary>
    /// Makes the caret's cell visible in its block by the least scrolling: a cell that starts left of the view moves the block so that it
    /// starts at the first column after the left marker; a cell that ends right of it moves the block so that it ends at the last column
    /// before the right marker. Does nothing for a caret outside a scrolling block.
    /// </summary>
    private void EnsureCaretColumnVisible()
    {
        if (!HasCursor)
        {
            return;
        }

        DisplayLine display = Built.Line(_line);
        int width = _cell < display.Cells.Length ? Math.Max(1, display.Cells[_cell].Width) : 1;
        EnsureColumnVisible(_line, _column, width);
    }

    /// <summary>
    /// Makes a span of a line's content visible in its block, by the least scrolling (see <see cref="EnsureCaretColumnVisible"/>). Does
    /// nothing for a line outside a scrolling block, or a span in the prefix.
    /// </summary>
    /// <param name="line">The display line.</param>
    /// <param name="column">The layout column where the span starts.</param>
    /// <param name="width">The span's width in columns, at least 1.</param>
    private void EnsureColumnVisible(int line, int column, int width)
    {
        DisplayLine display = Built.Line(line);
        int prefix = PrefixWidthOf(display);
        int key = Built.BaseOffset(line);
        if (column < prefix || !_maxShift.TryGetValue(key, out int max) || ContentWidthOf(display) == 0)
        {
            return;
        }

        int content = ContentWidthOf(display);
        int visible = Built.Width - prefix;
        int start = column - prefix;
        int end = start + width;
        int shift = _shift.GetValueOrDefault(key);

        // The left marker takes the first column when the block is scrolled.
        if (start < shift + (shift > 0 ? 1 : 0))
        {
            shift = start == 0 ? 0 : start - 1;
        }

        // The right marker takes the last column when content lies beyond the view, so a cell ending there needs one more column.
        if (end > shift + visible - (content > shift + visible ? 1 : 0))
        {
            shift = end - visible + (content > end ? 1 : 0);
        }

        shift = Math.Clamp(shift, 0, max);
        if (shift != _shift.GetValueOrDefault(key))
        {
            _shift[key] = shift;
        }
    }

    /// <summary>
    /// A press on an edge marker of a scrolled block scrolls it by <see cref="ScrollColumns"/> toward that side. Returns true when the press
    /// was on a marker.
    /// </summary>
    /// <param name="line">The display line under the press.</param>
    /// <param name="x">The screen column of the press.</param>
    private bool PressMarker(int line, int x)
    {
        (bool left, bool right) = MarkersOf(line);
        if (left && x == PrefixWidthOf(Built.Line(line)))
        {
            ScrollBlock(line, -ScrollColumns);
            return true;
        }

        if (right && x == Built.Width - 1)
        {
            ScrollBlock(line, ScrollColumns);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A wheel with a horizontal meaning (the fork's <see cref="MouseFlags.WheeledLeft"/> and <see cref="MouseFlags.WheeledRight"/>, or Shift
    /// with the vertical wheel) over a scrolled block scrolls it by <see cref="ScrollColumns"/>. Returns false when the block does not scroll,
    /// so that Shift+wheel elsewhere still scrolls the document.
    /// </summary>
    /// <param name="mouse">The wheel event.</param>
    /// <param name="delta">The columns to move by; negative to the left.</param>
    private bool WheelTable(Mouse mouse, int delta)
    {
        int y = mouse.Position?.Y ?? 0;
        return InView(y) && ScrollBlock(_top + y, delta);
    }
}
