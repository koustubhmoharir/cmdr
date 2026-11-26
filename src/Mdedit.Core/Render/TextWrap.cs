using System.Collections.Immutable;
using Shared.Core;

namespace Mdedit.Core.Render;

/// <summary>
/// Breaks the cells of a run into display lines: word wrap for prose, cell-boundary wrap for code and other source lines.
/// Cells are never split. A cell wider than the whole line still goes on a line of its own, so that wrapping always makes
/// progress.
/// </summary>
internal static class TextWrap
{
    /// <summary>
    /// Word wrap: a line takes whole words while they fit. Spaces after the last word stay on the line when they fit and are
    /// not shown otherwise. A word wider than the line breaks between its cells.
    /// </summary>
    /// <param name="cells">The cells of one run.</param>
    /// <param name="baseIndex">The flat index of the first cell, so that each line can record the index of its last cell.</param>
    /// <param name="width">The content width in columns.</param>
    public static List<WrappedLine> Words(ImmutableArray<Cell> cells, int baseIndex, int width)
    {
        var lines = new List<WrappedLine>();
        var line = new LineBuilder(continuation: false, reserved: 0);
        int index = 0;
        while (index < cells.Length)
        {
            if (cells[index].Text == " ")
            {
                if (line.Width + cells[index].Width <= width)
                {
                    line.Place(cells[index], baseIndex + index);
                }

                index++;
                continue;
            }

            int end = index;
            int wordWidth = 0;
            while (end < cells.Length && cells[end].Text != " ")
            {
                wordWidth += cells[end].Width;
                end++;
            }

            if (line.Width + wordWidth > width && line.HasContent)
            {
                lines.Add(line.Finish());
                line = new LineBuilder(continuation: true, reserved: 0);
            }

            for (int cell = index; cell < end; cell++)
            {
                if (line.HasContent && line.Width + cells[cell].Width > width)
                {
                    lines.Add(line.Finish());
                    line = new LineBuilder(continuation: true, reserved: 0);
                }

                line.Place(cells[cell], baseIndex + cell);
            }

            index = end;
        }

        lines.Add(line.Finish());
        return lines;
    }

    /// <summary>
    /// Cell-boundary wrap, for code and other source lines. With <paramref name="marked"/>, each continuation line starts with
    /// the wrap marker, a synthetic arrow of one column, and holds one column less of the run.
    /// </summary>
    /// <param name="cells">The cells of one source line.</param>
    /// <param name="baseIndex">The flat index of the first cell.</param>
    /// <param name="width">The content width in columns.</param>
    /// <param name="marked">Whether continuation lines start with the wrap marker.</param>
    public static List<WrappedLine> Cells(ImmutableArray<Cell> cells, int baseIndex, int width, bool marked)
    {
        var lines = new List<WrappedLine>();
        LineBuilder line = Start(marked, continuation: false);
        for (int index = 0; index < cells.Length; index++)
        {
            if (line.HasContent && line.Width + cells[index].Width > width)
            {
                lines.Add(line.Finish());
                line = Start(marked, continuation: true);
            }

            line.Place(cells[index], baseIndex + index);
        }

        lines.Add(line.Finish());
        return lines;
    }

    private static LineBuilder Start(bool marked, bool continuation)
    {
        if (marked && continuation)
        {
            var line = new LineBuilder(continuation: true, reserved: 1);
            line.Place(SyntheticCell.Make("↪", StyleRole.WrapMarker), -1);
            return line;
        }

        return new LineBuilder(continuation, reserved: 0);
    }

    /// <summary>Collects the cells of one line while it is wrapped.</summary>
    private sealed class LineBuilder
    {
        private readonly List<Cell> _cells = [];
        private readonly bool _continuation;
        private readonly int _reserved;
        private int _lastPlaced = -1;
        private bool _hasMapped;

        /// <summary>Starts an empty line.</summary>
        /// <param name="continuation">Whether the line continues the line before it.</param>
        /// <param name="reserved">The number of leading cells that are not content (the wrap marker).</param>
        public LineBuilder(bool continuation, int reserved)
        {
            _continuation = continuation;
            _reserved = reserved;
        }

        /// <summary>The width of the cells placed so far.</summary>
        public int Width { get; private set; }

        /// <summary>Whether the line holds any content cell, beyond its reserved leading cells.</summary>
        public bool HasContent => _cells.Count > _reserved;

        /// <summary>Adds a cell at the end of the line.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="index">The flat index of the cell in its block, or -1 for a cell that is not in the block's runs (a wrap marker).</param>
        public void Place(Cell cell, int index)
        {
            _cells.Add(cell);
            Width += cell.Width;
            if (index >= 0)
            {
                _lastPlaced = index;
            }

            _hasMapped |= cell.IsMapped;
        }

        /// <summary>The line as placed so far.</summary>
        public WrappedLine Finish() => new(_cells.ToImmutableArray(), _lastPlaced, _hasMapped, _continuation);
    }
}
