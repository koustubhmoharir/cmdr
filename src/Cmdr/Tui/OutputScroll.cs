namespace Cmdr.Tui;

/// <summary>
/// A place in the output: an absolute line number and a 0-based row within that line (a long line wraps to several rows).
/// Positions are ordered by line, then by row.
/// </summary>
internal readonly record struct ScrollPosition(long Line, int Row) : IComparable<ScrollPosition>
{
    public int CompareTo(ScrollPosition other) =>
        Line != other.Line ? Line.CompareTo(other.Line) : Row.CompareTo(other.Row);
}

/// <summary>
/// Scroll math over the retained lines <c>[first, last]</c>, where line <c>n</c> wraps to <c>rowsOf(n)</c> screen rows (at
/// least 1) and the viewport is <c>height</c> rows high. Pure: the caller supplies the row counts, so no UI or buffer is
/// needed. Positions passed in should already be clamped (see <see cref="Clamp"/>); the functions clamp their results.
/// </summary>
internal static class OutputScroll
{
    /// <summary>The position that shows the last <paramref name="height"/> rows, or the first line when there are fewer rows.</summary>
    public static ScrollPosition MaxTop(long first, long last, int height, Func<long, int> rowsOf)
    {
        ArgumentNullException.ThrowIfNull(rowsOf);

        if (last < first)
        {
            return new ScrollPosition(first, 0);
        }

        // Walk back from the last line until the rows from the top to the end fill the viewport. Only the lines needed are wrapped.
        int remaining = Math.Max(1, height);
        for (long line = last; line >= first; line--)
        {
            int rows = rowsOf(line);
            if (rows >= remaining)
            {
                return new ScrollPosition(line, rows - remaining);
            }

            remaining -= rows;
        }

        return new ScrollPosition(first, 0);
    }

    /// <summary>The position clamped to the retained lines and to <see cref="MaxTop"/>.</summary>
    public static ScrollPosition Clamp(ScrollPosition position, long first, long last, int height, Func<long, int> rowsOf)
    {
        ArgumentNullException.ThrowIfNull(rowsOf);

        if (last < first)
        {
            return new ScrollPosition(first, 0);
        }

        long line = Math.Clamp(position.Line, first, last);
        int row = line == position.Line ? Math.Clamp(position.Row, 0, rowsOf(line) - 1) : 0;
        var clamped = new ScrollPosition(line, row);
        ScrollPosition max = MaxTop(first, last, height, rowsOf);
        return clamped.CompareTo(max) > 0 ? max : clamped;
    }

    /// <summary>The position moved by <paramref name="delta"/> screen rows, across line boundaries, then clamped.</summary>
    public static ScrollPosition ScrollBy(ScrollPosition position, int delta, long first, long last, int height, Func<long, int> rowsOf)
    {
        ScrollPosition start = Clamp(position, first, last, height, rowsOf);
        long line = start.Line;
        int row = start.Row;
        for (int step = 0; step < Math.Abs(delta); step++)
        {
            if (delta > 0)
            {
                if (row + 1 < rowsOf(line))
                {
                    row++;
                }
                else if (line < last)
                {
                    line++;
                    row = 0;
                }
                else
                {
                    break;
                }
            }
            else if (row > 0)
            {
                row--;
            }
            else if (line > first)
            {
                line--;
                row = rowsOf(line) - 1;
            }
            else
            {
                break;
            }
        }

        return Clamp(new ScrollPosition(line, row), first, last, height, rowsOf);
    }

    /// <summary>
    /// The top position that shows <paramref name="cursor"/> fully, moving the top as little as possible. A cursor line
    /// above the top, or a top that starts inside the cursor line, puts the cursor line's first row at the top. A cursor line
    /// taller than the viewport also puts its first row at the top.
    /// </summary>
    public static ScrollPosition EnsureVisible(ScrollPosition top, long cursor, long first, long last, int height, Func<long, int> rowsOf)
    {
        ArgumentNullException.ThrowIfNull(rowsOf);

        if (cursor < top.Line || (cursor == top.Line && top.Row > 0))
        {
            return new ScrollPosition(cursor, 0);
        }

        int viewport = Math.Max(1, height);

        // Rows from the top through the end of the cursor line. Stops early once past the viewport.
        long rowsThrough = 0;
        for (long line = top.Line; line <= cursor; line++)
        {
            int rows = rowsOf(line);
            rowsThrough += line == top.Line ? rows - top.Row : rows;
            if (rowsThrough > viewport)
            {
                break;
            }
        }

        if (rowsThrough <= viewport)
        {
            return top;
        }

        int cursorRows = rowsOf(cursor);
        if (cursorRows >= viewport)
        {
            return new ScrollPosition(cursor, 0);
        }

        // The cursor line goes at the bottom; fill the rest of the viewport with the lines above it.
        int remaining = viewport - cursorRows;
        for (long line = cursor - 1; line >= first; line--)
        {
            int rows = rowsOf(line);
            if (rows >= remaining)
            {
                return new ScrollPosition(line, rows - remaining);
            }

            remaining -= rows;
        }

        return new ScrollPosition(first, 0);
    }

    /// <summary>
    /// The line the cursor moves to for a page step: the line that holds the row <paramref name="height"/> rows below (or above)
    /// the cursor's first row, and at least one line away from the cursor. Clamped to the retained lines.
    /// </summary>
    public static long PageTarget(long cursor, int direction, long first, long last, int height, Func<long, int> rowsOf)
    {
        ArgumentNullException.ThrowIfNull(rowsOf);

        int viewport = Math.Max(1, height);
        if (direction > 0)
        {
            if (cursor >= last)
            {
                return last;
            }

            long target = last;
            int covered = 0;
            for (long line = cursor; line <= last; line++)
            {
                int rows = rowsOf(line);
                if (covered + rows > viewport)
                {
                    target = line;
                    break;
                }

                covered += rows;
            }

            return Math.Clamp(Math.Max(target, cursor + 1), first, last);
        }

        if (cursor <= first)
        {
            return first;
        }

        long above = first;
        int passed = 0;
        for (long line = cursor - 1; line >= first; line--)
        {
            int rows = rowsOf(line);
            if (passed + rows >= viewport)
            {
                above = line;
                break;
            }

            passed += rows;
        }

        return Math.Clamp(Math.Min(above, cursor - 1), first, last);
    }
}
