using System.Text;

namespace Logvu.Core.Reading;

/// <summary>
/// Splits a UTF-8 stream into lines by byte offset, forward from a line start or backward from a line end. Memory is
/// bounded by twice the block size plus the text cap, whatever the line length. The reader does not own the stream; it
/// sets <see cref="Stream.Position"/> before each read.
/// </summary>
public sealed class LineReader
{
    /// <summary>The default text cap: the most bytes of a line that are decoded into <see cref="RawLine.Text"/>.</summary>
    public const int TextCap = 8 * 1024;

    private static readonly UTF8Encoding Utf8 = new(false, false);

    private readonly Stream _stream;
    private readonly int _blockSize;
    private readonly int _textCap;

    /// <param name="stream">A seekable stream. It is read, never disposed.</param>
    /// <param name="blockSize">The number of bytes read at a time.</param>
    /// <param name="textCap">The most bytes of a line decoded into <see cref="RawLine.Text"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockSize"/> or <paramref name="textCap"/> is below 1.</exception>
    public LineReader(Stream stream, int blockSize = 64 * 1024, int textCap = TextCap)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(textCap, 1);
        _stream = stream;
        _blockSize = blockSize;
        _textCap = textCap;
    }

    /// <summary>The offset where the content starts: 3 when the stream begins with a UTF-8 byte order mark, otherwise 0.</summary>
    public static long ContentStart(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ContentStart(new BlockCache(stream, 3));
    }

    /// <summary>
    /// The lines in order, starting at <paramref name="from"/> and continuing while a line starts before
    /// <paramref name="limit"/>. Only bytes before <paramref name="limit"/> are read, so a line that runs past it ends
    /// there and is unterminated. A <paramref name="limit"/> beyond the stream length is clamped to it.
    /// </summary>
    /// <param name="from">A line start. An offset inside the byte order mark is moved to the content start.</param>
    /// <param name="limit">The offset at which reading stops.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> is outside [0, stream length].</exception>
    public IEnumerable<RawLine> ReadForward(long from, long limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, _stream.Length, nameof(from));
        return Forward(from, limit);
    }

    /// <summary>
    /// The lines in reverse order, starting with the line that ends at <paramref name="before"/> and continuing
    /// backward to the content start. An unterminated tail before <paramref name="before"/> comes first.
    /// </summary>
    /// <param name="before">A line end (the offset after a <c>\n</c>) or the stream length.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="before"/> is outside [0, stream length].</exception>
    public IEnumerable<RawLine> ReadBackward(long before)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(before);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(before, _stream.Length, nameof(before));
        return Backward(before);
    }

    private IEnumerable<RawLine> Forward(long from, long limit)
    {
        var cache = new BlockCache(_stream, _blockSize);
        long end = Math.Min(limit, cache.Length);
        long start = Math.Max(from, ContentStart(cache));
        var prefix = new byte[_textCap + 1];
        while (start < end)
        {
            RawLine line = ReadForwardLine(cache, start, end, prefix);
            yield return line;
            start = line.End;
        }
    }

    private IEnumerable<RawLine> Backward(long before)
    {
        var cache = new BlockCache(_stream, _blockSize);
        long bom = ContentStart(cache);
        var prefix = new byte[_textCap + 1];
        long end = before;
        while (end > bom)
        {
            RawLine line = ReadBackwardLine(cache, end, bom, prefix);
            yield return line;
            end = line.Start;
        }
    }

    /// <summary>Reads the line starting at <paramref name="start"/>, which must end (or be cut off) before <paramref name="end"/>.</summary>
    private RawLine ReadForwardLine(BlockCache cache, long start, long end, byte[] prefix)
    {
        // The prefix keeps the first textCap + 1 raw bytes: enough for the text, and the byte after it when truncating.
        int collected = 0;
        int previous = -1;
        long newline = -1;
        long scan = start;
        while (scan < end && newline < 0)
        {
            ReadOnlySpan<byte> window = cache.Window(scan);
            window = window[..(int)Math.Min(window.Length, end - scan)];
            int index = window.IndexOf((byte)'\n');
            int content = index < 0 ? window.Length : index;
            int take = Math.Min(content, prefix.Length - collected);
            window[..take].CopyTo(prefix.AsSpan(collected));
            collected += take;
            if (content > 0)
            {
                previous = window[content - 1];
            }

            if (index >= 0)
            {
                newline = scan + index;
            }

            scan += window.Length;
        }

        if (newline < 0)
        {
            return MakeLine(start, end, false, end - start, prefix.AsSpan(0, collected));
        }

        long contentLength = newline - start - (previous == '\r' ? 1 : 0);
        return MakeLine(start, newline + 1, true, contentLength, prefix.AsSpan(0, collected));
    }

    /// <summary>Reads the line that ends at <paramref name="end"/>.</summary>
    private RawLine ReadBackwardLine(BlockCache cache, long end, long bom, byte[] prefix)
    {
        bool terminated = cache.Byte(end - 1) == '\n';
        long contentEnd = terminated ? end - 1 : end;
        bool carriageReturn = terminated && contentEnd - 1 >= bom && cache.Byte(contentEnd - 1) == '\r';
        long start = FindLineStart(cache, contentEnd, bom);
        long contentLength = contentEnd - (carriageReturn ? 1 : 0) - start;

        // The start is known now, so the prefix is read forward from it; the line is never buffered whole.
        int collected = (int)Math.Min(contentLength, prefix.Length);
        CopyOut(cache, start, prefix.AsSpan(0, collected));
        return MakeLine(start, end, terminated, contentLength, prefix.AsSpan(0, collected));
    }

    /// <summary>The offset after the last <c>\n</c> before <paramref name="contentEnd"/>, or the content start if there is none.</summary>
    private static long FindLineStart(BlockCache cache, long contentEnd, long bom)
    {
        long position = contentEnd;
        while (position > bom)
        {
            long blockStart = position - 1 - (position - 1) % cache.BlockSize;
            long lower = Math.Max(blockStart, bom);
            ReadOnlySpan<byte> part = cache.Window(blockStart)[(int)(lower - blockStart)..(int)(position - blockStart)];
            int index = part.LastIndexOf((byte)'\n');
            if (index >= 0)
            {
                return lower + index + 1;
            }

            position = lower;
        }

        return bom;
    }

    private static void CopyOut(BlockCache cache, long offset, Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            ReadOnlySpan<byte> window = cache.Window(offset);
            int count = Math.Min(window.Length, destination.Length);
            window[..count].CopyTo(destination);
            destination = destination[count..];
            offset += count;
        }
    }

    private static long ContentStart(BlockCache cache)
    {
        return cache.Length >= 3 && cache.Byte(0) == 0xEF && cache.Byte(1) == 0xBB && cache.Byte(2) == 0xBF ? 3 : 0;
    }

    /// <param name="prefix">The first raw bytes of the line: at least min(contentLength, textCap + 1) of them.</param>
    /// <param name="contentLength">The content length, without <c>\r\n</c> or <c>\n</c>.</param>
    private RawLine MakeLine(long start, long end, bool terminated, long contentLength, ReadOnlySpan<byte> prefix)
    {
        bool truncated = contentLength > _textCap;
        int length = truncated ? CutPoint(prefix) : (int)contentLength;
        return new RawLine(start, end, terminated, Utf8.GetString(prefix[..length]), truncated);
    }

    /// <summary>
    /// The byte count to decode for a truncated line. A cut that lands inside a multi-byte sequence moves back to the
    /// sequence's lead byte. A run of continuation bytes with no lead within reach is not a sequence, so the cut stays.
    /// </summary>
    private int CutPoint(ReadOnlySpan<byte> prefix)
    {
        int cut = _textCap;
        for (int steps = 0; steps < 3 && cut > 0 && IsContinuation(prefix[cut]); steps++)
        {
            cut--;
        }

        return IsContinuation(prefix[cut]) ? _textCap : cut;
    }

    private static bool IsContinuation(byte value) => (value & 0xC0) == 0x80;

    /// <summary>
    /// The stream read in block-aligned windows, for one enumeration. Block k covers [k * blockSize, min((k + 1) * blockSize,
    /// length)), where length is the stream length when the cache is made. Two blocks are kept: the current one and the one
    /// before it. Reads are nearly sequential, so a block is read once unless a line reaches back across two blocks.
    /// </summary>
    private sealed class BlockCache
    {
        private readonly Stream _stream;
        private readonly byte[][] _buffers;
        private readonly long[] _blocks = [-1, -1];
        private readonly int[] _counts = new int[2];
        private int _recent;

        public BlockCache(Stream stream, int blockSize)
        {
            _stream = stream;
            BlockSize = blockSize;
            Length = stream.Length;
            _buffers = [new byte[blockSize], new byte[blockSize]];
        }

        public int BlockSize { get; }

        public long Length { get; }

        /// <summary>The bytes from <paramref name="offset"/> to the end of its block. <paramref name="offset"/> must be below <see cref="Length"/>.</summary>
        public ReadOnlySpan<byte> Window(long offset)
        {
            long block = offset / BlockSize;
            int slot = Slot(block);
            int within = (int)(offset - block * BlockSize);
            return _buffers[slot].AsSpan(within, _counts[slot] - within);
        }

        public byte Byte(long offset) => Window(offset)[0];

        private int Slot(long block)
        {
            int slot = _blocks[0] == block ? 0 : _blocks[1] == block ? 1 : -1;
            if (slot < 0)
            {
                slot = 1 - _recent;
                long begin = block * BlockSize;
                int count = (int)Math.Min(BlockSize, Length - begin);
                _stream.Position = begin;
                _stream.ReadExactly(_buffers[slot].AsSpan(0, count));
                _blocks[slot] = block;
                _counts[slot] = count;
            }

            _recent = slot;
            return slot;
        }
    }
}
