using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>One display line of the command list. A detail line belongs to the logical row at <see cref="RowIndex"/>.</summary>
internal readonly record struct DisplayLine(int RowIndex, bool IsDetail);

/// <summary>
/// Maps the logical rows of the command list to display lines. A group header takes one line. A command takes two:
/// its main line, then its detail line. Focus and navigation only ever rest on main lines. Pure; no UI types.
/// </summary>
internal sealed class DisplayLines
{
    private readonly DisplayLine[] _lines;
    private readonly int[] _firstLine;
    private readonly int[] _lastLine;

    private DisplayLines(DisplayLine[] lines, int[] firstLine, int[] lastLine)
    {
        _lines = lines;
        _firstLine = firstLine;
        _lastLine = lastLine;
    }

    /// <summary>Display lines for the rows. Only command rows get a detail line.</summary>
    public static DisplayLines Build(IReadOnlyList<ListRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return FromHasDetail(rows.Select(row => row is CommandRow).ToList());
    }

    /// <summary>Display lines for rows that either have a detail line (<paramref name="hasDetail"/>[i] is true) or only a main line.</summary>
    public static DisplayLines FromHasDetail(IReadOnlyList<bool> hasDetail)
    {
        ArgumentNullException.ThrowIfNull(hasDetail);

        var lines = new List<DisplayLine>(hasDetail.Count * 2);
        var first = new int[hasDetail.Count];
        var last = new int[hasDetail.Count];
        for (int row = 0; row < hasDetail.Count; row++)
        {
            first[row] = lines.Count;
            lines.Add(new DisplayLine(row, IsDetail: false));
            if (hasDetail[row])
            {
                lines.Add(new DisplayLine(row, IsDetail: true));
            }

            last[row] = lines.Count - 1;
        }

        return new DisplayLines(lines.ToArray(), first, last);
    }

    /// <summary>Number of display lines.</summary>
    public int Count => _lines.Length;

    /// <summary>The display lines, in order.</summary>
    public IReadOnlyList<DisplayLine> Lines => _lines;

    /// <summary>The main line of the row, or -1 when the row index is out of range.</summary>
    public int RowToLine(int rowIndex) => rowIndex >= 0 && rowIndex < _firstLine.Length ? _firstLine[rowIndex] : -1;

    /// <summary>The last line of the row (its detail line, or its main line when it has none), or -1 when out of range.</summary>
    public int LastLineOfRow(int rowIndex) => rowIndex >= 0 && rowIndex < _lastLine.Length ? _lastLine[rowIndex] : -1;

    /// <summary>The row a display line belongs to, or -1 when the line is out of range.</summary>
    public int LineToRow(int line) => IsValid(line) ? _lines[line].RowIndex : -1;

    /// <summary>Whether the line is a detail line.</summary>
    public bool IsDetail(int line) => IsValid(line) && _lines[line].IsDetail;

    /// <summary>The main line at or before a line: the line itself, or the command's main line for a detail line.</summary>
    public int MainLineOf(int line) => IsDetail(line) ? line - 1 : line;

    /// <summary>The first focusable line (a main line), or -1 when there are no lines.</summary>
    public int FirstFocusableLine => _lines.Length == 0 ? -1 : 0;

    /// <summary>The last focusable line (a main line), or -1 when there are no lines.</summary>
    public int LastFocusableLine => _lines.Length == 0 ? -1 : MainLineOf(_lines.Length - 1);

    /// <summary>
    /// The next focusable line in the direction of <paramref name="delta"/> (only its sign matters). Detail lines are skipped.
    /// Clamps at the ends: when there is no focusable line further on, <paramref name="currentLine"/> (as a main line) is returned.
    /// With no current line (-1 or out of range) the first focusable line is returned. Returns -1 for an empty list.
    /// </summary>
    public int NextFocusableLine(int currentLine, int delta)
    {
        if (_lines.Length == 0)
        {
            return -1;
        }

        if (currentLine < 0)
        {
            return FirstFocusableLine;
        }

        int step = Math.Sign(delta);
        int line = MainLineOf(Math.Min(currentLine, _lines.Length - 1));
        if (step == 0)
        {
            return line;
        }

        int next = line + step;
        while (IsValid(next) && _lines[next].IsDetail)
        {
            next += step;
        }

        return IsValid(next) ? next : line;
    }

    private bool IsValid(int line) => line >= 0 && line < _lines.Length;
}
