using System.Collections.Immutable;
using Mdedit.Core.Render;
using Terminal.Gui.Input;
using Cell = Mdedit.Core.Render.Cell;
using Point = System.Drawing.Point;

namespace Mdedit.Tui;

/// <summary>
/// The fold state of the view and the keys, clicks and menu entries that change it. A fold unit is folded when its key (its head block's
/// absolute start) is in the set. Every change rebuilds the layout with the caret's screen row kept (see <see cref="Reflow"/>), so a caret
/// inside what a fold hides moves to the head.
/// </summary>
/// <remarks>
/// <para>
/// <b>Target.</b> Alt+F, a click on a head's glyph and the menu act on the <i>target</i> of a line: the unit whose head is on the
/// line, else the innermost unit around the line's block (a section, or a code block the line is in). The menu's "Fold section" takes
/// the innermost section around the caret.
/// </para>
/// <para>
/// <b>Heads.</b> A folded code block's header line maps the block's start through its label, so a caret that the fold hides moves to that
/// label. A code header's glyph is in the gutter, not mapped, and a click on it folds (see <see cref="IsFoldClick"/>).
/// </para>
/// </remarks>
internal sealed partial class RenderedView
{
    // The keys of the folded units, by their starts.
    private readonly HashSet<int> _folded = [];

    /// <summary>Whether a fold unit is folded in the view.</summary>
    /// <param name="unit">A unit of the document.</param>
    public bool IsFolded(FoldUnit unit) => _folded.Contains(unit.Start);

    /// <summary>
    /// Whether the cursor is on the head of a folded unit: the head's line, which the fold's summary follows. The view does not edit it while the
    /// unit is folded.
    /// </summary>
    public bool CursorOnFoldedHead => HasCursor && Built.FoldUnitAt(_line) is { } unit && IsFolded(unit);

    /// <summary>Folds the unit when it is unfolded, and unfolds it when it is folded. The layout is rebuilt and the caret keeps its row.</summary>
    /// <param name="unit">A unit of the document.</param>
    public void ToggleFold(FoldUnit unit) => ChangeFolds(() =>
    {
        if (!_folded.Remove(unit.Start))
        {
            _folded.Add(unit.Start);
        }
    });

    /// <summary>
    /// The fold unit that a screen position targets: the unit whose head is on its row, else the innermost unit around the row's block (see
    /// the remarks of the class). Null when the position is outside the view or there is none.
    /// </summary>
    /// <param name="screen">A position on the screen.</param>
    public FoldUnit? FoldUnitAtScreen(Point screen)
    {
        if (!SyncLayout())
        {
            return null;
        }

        Point viewport = ScreenToViewport(screen);
        return InView(viewport.Y) ? TargetOfLine(LineAtRow(viewport.Y)) : null;
    }

    /// <summary>The fold units of the document in order of their heads, for the tests.</summary>
    internal IReadOnlyList<FoldUnit> Units => _document.FoldUnits;

    /// <summary>The target of a display line: its head's unit, else the innermost unit around its block.</summary>
    private FoldUnit? TargetOfLine(int line) => Built.FoldUnitAt(line) ?? UnitAround(BlockOfLine(line), sectionsOnly: false);

    /// <summary>The target of the caret, as <see cref="TargetOfLine"/> gives it for the caret's line.</summary>
    private FoldUnit? CaretTarget() => HasCursor ? TargetOfLine(_line) : null;

    /// <summary>
    /// The innermost fold unit whose blocks contain a block (its head at or before the block, and the block before its end). Units are in order
    /// of their heads, so the last one that contains the block is the innermost.
    /// </summary>
    /// <param name="block">The index of a block of the document.</param>
    /// <param name="sectionsOnly">Whether only sections count.</param>
    private FoldUnit? UnitAround(int block, bool sectionsOnly)
    {
        FoldUnit? found = null;
        foreach (FoldUnit unit in _document.FoldUnits)
        {
            if (unit.HeadBlock > block)
            {
                break;
            }

            if (block < unit.EndBlock && (!sectionsOnly || unit.Kind == FoldUnitKind.Section))
            {
                found = unit;
            }
        }

        return found;
    }

    /// <summary>The index of the block whose line a display line is: the block that owns the line.</summary>
    private int BlockOfLine(int line) => BlockAtOffset(Built.BaseOffset(line));

    /// <summary>The index of the last block that starts at or before an offset, or -1 when none does.</summary>
    private int BlockAtOffset(int offset)
    {
        ImmutableArray<RenderBlock> blocks = _document.Blocks;
        int low = 0;
        int high = blocks.Length - 1;
        int found = -1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (blocks[middle].Start <= offset)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    /// <summary>The key that toggles the caret's target: Alt+F, as the editor's fold key.</summary>
    internal static readonly Key ToggleFoldKey = Key.F.WithAlt;

    // The keys that show the headings down to a level: Alt+1 is level 1, and so on to Alt+6.
    private static readonly Key[] LevelKeys = [Key.D1.WithAlt, Key.D2.WithAlt, Key.D3.WithAlt, Key.D4.WithAlt, Key.D5.WithAlt, Key.D6.WithAlt];

    /// <summary>
    /// The keys of the fold state: Alt+F toggles the target, Alt+1 to Alt+6 show the headings down to that level, and Alt+0 unfolds everything.
    /// Returns false for any other key, and for a key with no target.
    /// </summary>
    /// <param name="key">The key.</param>
    private bool FoldKey(Key key)
    {
        if (key == ToggleFoldKey)
        {
            return ToggleTarget();
        }

        if (key == Key.D0.WithAlt)
        {
            if (_folded.Count == 0)
            {
                return true;
            }

            ChangeFolds(() => _folded.Clear());
            return true;
        }

        int level = Array.IndexOf(LevelKeys, key) + 1;
        if (level == 0)
        {
            return false;
        }

        ShowLevel(level);
        return true;
    }

    /// <summary>Toggles the caret's target. Returns false when there is none.</summary>
    private bool ToggleTarget()
    {
        if (CaretTarget() is not { } unit)
        {
            return false;
        }

        ToggleFold(unit);
        return true;
    }

    /// <summary>
    /// Unfolds a unit and every unit inside it: the menu's "Unfold all inside". The layout is rebuilt and the caret keeps its row.
    /// </summary>
    /// <param name="target">A unit of the document.</param>
    public void UnfoldInside(FoldUnit target) => ChangeFolds(() =>
    {
        foreach (FoldUnit unit in _document.FoldUnits)
        {
            if (unit.HeadBlock >= target.HeadBlock && unit.HeadBlock < target.EndBlock)
            {
                _folded.Remove(unit.Start);
            }
        }
    });

    /// <summary>
    /// The innermost section around the caret, which the menu's "Fold section" folds; null when the caret is in no section or there is no
    /// cursor.
    /// </summary>
    public FoldUnit? SectionAroundCaret() => HasCursor ? UnitAround(BlockOfLine(_line), sectionsOnly: true) : null;

    /// <summary>Folds a section. The layout is rebuilt and the caret keeps its row.</summary>
    /// <param name="section">A section of the document, from <see cref="SectionAroundCaret"/>.</param>
    public void FoldSection(FoldUnit section) => ChangeFolds(() => _folded.Add(section.Start));

    /// <summary>Folds every section whose heading is deeper than a level, and unfolds the others. Code units keep their state.</summary>
    /// <param name="level">The deepest heading level still shown, from 1 to 6.</param>
    private void ShowLevel(int level) => ChangeFolds(() =>
    {
        foreach (FoldUnit unit in _document.FoldUnits)
        {
            if (unit.Kind != FoldUnitKind.Section)
            {
                continue;
            }

            if (_document.Blocks[unit.HeadBlock].HeadingLevel > level)
            {
                _folded.Add(unit.Start);
            }
            else
            {
                _folded.Remove(unit.Start);
            }
        }
    });

    /// <summary>
    /// Applies a change of the fold state and rebuilds the layout with the caret's screen row kept. A caret that was on screen stays on
    /// screen (the view scrolls the least it must); one that was off screen stays where it was, and the view does not scroll to it.
    /// </summary>
    /// <param name="change">The change to the fold state.</param>
    private void ChangeFolds(Action change)
    {
        if (!SyncLayout())
        {
            return;
        }

        SetPending(null);
        bool wasVisible = IsCursorVisible();
        change();
        Reflow(Built.Width);
        if (wasVisible)
        {
            EnsureVisible();
        }

        SyncTracking();
        UpdateCursor();
        SetNeedsDraw();
        PositionChanged?.Invoke();
    }

    /// <summary>
    /// Unfolds the units that hide a source offset, so that a match in folded content can be selected. Returns true when a unit was
    /// unfolded.
    /// </summary>
    /// <param name="offset">The absolute source offset.</param>
    private bool UnfoldHiding(int offset)
    {
        int block = BlockAtOffset(offset);
        if (block < 0 || _folded.Count == 0)
        {
            return false;
        }

        bool changed = false;
        foreach (FoldUnit unit in _document.FoldUnits)
        {
            if (!_folded.Contains(unit.Start))
            {
                continue;
            }

            bool hidden = unit.Kind == FoldUnitKind.Section
                ? block > unit.HeadBlock && block < unit.EndBlock
                : block == unit.HeadBlock;
            if (hidden)
            {
                _folded.Remove(unit.Start);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// A click on a fold glyph of a head, or on a fold summary, toggles the unit of its line. Returns false when the click is on neither.
    /// </summary>
    /// <param name="line">The display line of the click.</param>
    /// <param name="x">The screen column of the click.</param>
    private bool ClickFold(int line, int x)
    {
        ImmutableArray<Cell> cells = Built.Line(line).Cells;
        int cell = CellAt(line, LayoutColumn(line, x));
        if (cell < 0 || !IsFoldClick(cells[cell]) || Built.FoldUnitAt(line) is not { } unit)
        {
            return false;
        }

        ToggleFold(unit);
        return true;
    }

    /// <summary>Whether a click on a cell folds or unfolds: the fold glyph in the gutter of a heading or a code header, or a fold summary.</summary>
    private static bool IsFoldClick(Cell cell) => cell.Style.Role is StyleRole.FoldGlyph or StyleRole.FoldSummary;
}
