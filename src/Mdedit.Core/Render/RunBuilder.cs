using System.Collections.Immutable;
using System.Globalization;
using Shared.Core;

namespace Mdedit.Core.Render;

/// <summary>
/// Builds the runs of one block. Cells are added in source order with absolute source offsets, which the builder makes
/// relative to the block's start. A hard break ends the current run, and the next run starts at the break.
/// </summary>
internal sealed class RunBuilder
{
    private readonly int _blockStart;
    private readonly List<Run> _runs = [];
    private List<Cell> _cells = [];
    private int _runStart;

    /// <summary>Starts a builder for a block that starts at <paramref name="blockStart"/>.</summary>
    /// <param name="blockStart">The absolute source offset of the block.</param>
    public RunBuilder(int blockStart)
    {
        _blockStart = blockStart;
    }

    /// <summary>
    /// Adds the grapheme clusters of <paramref name="text"/>. <paramref name="offsets"/> gives the absolute source offset of
    /// each character of the text, or is null for synthetic text. Each cluster takes the offset of its first character.
    /// </summary>
    public void AddText(string text, int[]? offsets, CellStyle style, int linkId)
    {
        for (int index = 0; index < text.Length;)
        {
            int length = StringInfo.GetNextTextElementLength(text.AsSpan(index));
            int offset = offsets is null ? Cell.Synthetic : offsets[index];
            Append(text.Substring(index, length), offset == Cell.Synthetic ? Cell.Synthetic : offset - _blockStart, style, linkId);
            index += length;
        }
    }

    /// <summary>Adds synthetic text: cells with no source offset.</summary>
    public void AddSynthetic(string text, CellStyle style) => AddText(text, null, style, -1);

    /// <summary>
    /// Ends the current run at a hard break. The next run starts at the break's absolute source offset, which is the first
    /// character of the line break, so that the layout can anchor the next line there.
    /// </summary>
    public void HardBreak(int absoluteOffset)
    {
        _runs.Add(new Run(_cells.ToImmutableArray(), _runStart));
        _cells = [];
        _runStart = absoluteOffset - _blockStart;
    }

    /// <summary>Ends the last run and returns all of them. Always at least one run, even when it is empty.</summary>
    public ImmutableArray<Run> Finish()
    {
        _runs.Add(new Run(_cells.ToImmutableArray(), _runStart));
        _cells = [];
        return _runs.ToImmutableArray();
    }

    /// <summary>
    /// A run of one source line: each grapheme cluster of <c>text[start..end)</c> is one cell at its own offset. Used for code,
    /// HTML and table lines, where the text is the source and there is nothing to align.
    /// </summary>
    /// <param name="text">The whole source text.</param>
    /// <param name="start">The absolute offset of the line's content.</param>
    /// <param name="end">The absolute offset after the line's content (exclusive).</param>
    /// <param name="style">The style of every cell.</param>
    /// <param name="blockStart">The absolute offset of the block the line belongs to.</param>
    public static Run Line(string text, int start, int end, CellStyle style, int blockStart)
    {
        var cells = ImmutableArray.CreateBuilder<Cell>();
        ReadOnlySpan<char> line = text.AsSpan(start, end - start);
        for (int index = 0; index < line.Length;)
        {
            int length = StringInfo.GetNextTextElementLength(line[index..]);
            ReadOnlySpan<char> grapheme = line.Slice(index, length);
            cells.Add(new Cell(grapheme.ToString(), DisplayWidth.OfGrapheme(grapheme), style, start + index - blockStart, -1));
            index += length;
        }

        return new Run(cells.ToImmutable(), start - blockStart);
    }

    private void Append(string grapheme, int offset, CellStyle style, int linkId)
    {
        if (_cells.Count > 0 && offset != Cell.Synthetic && MayJoinPrevious(grapheme, _cells[^1]))
        {
            Cell last = _cells[^1];
            string joined = last.Text + grapheme;
            if (StringInfo.GetNextTextElementLength(joined.AsSpan()) == joined.Length)
            {
                // A mark or a joiner that starts this piece belongs to the cluster before it (the piece boundary is an
                // inline boundary, such as an emphasis delimiter): one cell, at the base's offset and style.
                _cells[^1] = last with { Text = joined, Width = DisplayWidth.OfGrapheme(joined.AsSpan()) };
                return;
            }
        }

        _cells.Add(new Cell(grapheme, DisplayWidth.OfGrapheme(grapheme.AsSpan()), style, offset, linkId));
    }

    private static bool MayJoinPrevious(string grapheme, Cell last) =>
        last.IsMapped && (!char.IsAscii(grapheme[0]) || last.Text.EndsWith('‍'));
}
