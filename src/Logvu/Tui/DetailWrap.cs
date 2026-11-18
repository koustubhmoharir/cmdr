using System.Globalization;
using Logvu.Core.Detail;
using Shared.Tui;

namespace Logvu.Tui;

/// <summary>One row of a wrapped detail line: its UTF-16 range in the line's text, and the column where its text starts.</summary>
/// <param name="Start">The first UTF-16 unit of the row in the line's text.</param>
/// <param name="Length">The number of UTF-16 units of the row. Zero only for the row of an empty line.</param>
/// <param name="Indent">
/// The column the row's text starts at: 0 for the first row, the continuation indent for the others.
/// </param>
internal readonly record struct WrapRow(int Start, int Length, int Indent);

/// <summary>
/// The rows of one <see cref="DetailLine"/> at a width, computed lazily: a request for rows measures only the graphemes those rows need,
/// and the rows already computed are kept, so a later request continues where the last one stopped. Cells are grapheme clusters measured
/// with <see cref="TextWidth"/>; a grapheme is never split, and a zero-width grapheme takes no cell. A row takes cells until the next one
/// would not fit; a row that is empty takes whatever comes next, even a cell wider than the row. Pure: no UI types.
/// </summary>
/// <remarks>
/// A continuation row starts at the line's <see cref="DetailLine.Indent"/> column and has the width less the indent. An indent of
/// width minus 10 or more is not used: the continuation rows then start at column 0, so a narrow view never keeps only a sliver of text.
/// One instance caches the rows of one line at one width; the view makes a new one when either changes.
/// </remarks>
internal sealed class DetailWrap
{
    /// <summary>A continuation indent of this many columns or more short of the width is dropped, and the rows start at column 0.</summary>
    private const int MinContinuationColumns = 10;

    private readonly string _text;
    private readonly int _width;
    private readonly int _indent;
    private readonly List<WrapRow> _rows = [];
    private int _next;
    private bool _complete;
    private int _measured;

    /// <summary>Creates the wrapping of a line at a width. Nothing is measured until rows are requested.</summary>
    /// <param name="line">The line to wrap.</param>
    /// <param name="width">The width in columns. A width below 1 is treated as 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <c>null</c>.</exception>
    public DetailWrap(DetailLine line, int width)
    {
        ArgumentNullException.ThrowIfNull(line);

        _text = line.Text;
        _width = Math.Max(1, width);
        _indent = ContinuationIndent(line, _width);
    }

    /// <summary>The number of grapheme clusters measured so far, including the one that started each row after the first.</summary>
    /// <remarks>The view does not use it. It shows that requesting rows measures only what those rows need.</remarks>
    public int Measured => _measured;

    /// <summary>
    /// The column the continuation rows of a line start at: its <see cref="DetailLine.Indent"/>, or 0 when the indent is
    /// <paramref name="width"/> less 10 or more.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="width">The width in columns.</param>
    public static int ContinuationIndent(DetailLine line, int width) =>
        line.Indent >= width - MinContinuationColumns ? 0 : line.Indent;

    /// <summary>The number of rows of the line. Measures the whole line the first time it is asked for.</summary>
    public int RowCount
    {
        get
        {
            while (!_complete)
            {
                Advance();
            }

            return _rows.Count;
        }
    }

    /// <summary>
    /// The rows from <paramref name="first"/> on, at most <paramref name="count"/> of them. Fewer are returned when the line ends first, and
    /// none when <paramref name="first"/> is past its last row. Only the rows up to <paramref name="first"/> + <paramref name="count"/> are
    /// measured.
    /// </summary>
    /// <param name="first">The index of the first row, from 0.</param>
    /// <param name="count">The most rows to return.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="first"/> or <paramref name="count"/> is negative.</exception>
    public IReadOnlyList<WrapRow> Rows(int first, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        while (!_complete && _rows.Count < first + count)
        {
            Advance();
        }

        int end = Math.Min(_rows.Count, first + count);
        return first >= end ? [] : _rows.GetRange(first, end - first);
    }

    /// <summary>Computes the next row, from the end of the last one.</summary>
    private void Advance()
    {
        bool first = _rows.Count == 0;
        int budget = first ? _width : _width - _indent;
        int start = _next;
        int used = 0;
        int offset = start;
        bool broke = false;
        while (offset < _text.Length)
        {
            ReadOnlySpan<char> rest = _text.AsSpan(offset);
            int length = StringInfo.GetNextTextElementLength(rest);
            int width = TextWidth.WidthOf(rest[..length]);
            _measured++;
            if (width > 0)
            {
                if (used > 0 && used + width > budget)
                {
                    broke = true;
                    break;
                }

                used += width;
            }

            offset += length;
        }

        _rows.Add(new WrapRow(start, offset - start, first ? 0 : _indent));
        _next = offset;
        _complete = !broke;
    }
}
