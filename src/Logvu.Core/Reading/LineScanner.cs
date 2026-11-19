using System.Text;

namespace Logvu.Core.Reading;

/// <summary>
/// The lines of a stream by byte offset, as views of a block cache, without an allocation per line. <see cref="LineReader"/> and
/// <see cref="EventFramer"/> both run on it, so a line means the same thing to both. The scanner takes the stream's length when it is
/// made; an enumeration that must see a growing stream makes a new scanner.
/// </summary>
/// <remarks>
/// A <see cref="LineView"/> and the text decoded from it are valid until the next call on the scanner. The scanner holds one prefix buffer
/// and one character buffer, so a view's prefix is overwritten by the next line.
/// </remarks>
internal sealed class LineScanner
{
    private static readonly UTF8Encoding Utf8 = new(false, false);

    private readonly BlockCache _cache;
    private readonly int _textCap;
    private readonly byte[] _prefix;
    private readonly char[] _chars;

    /// <summary>Creates a scanner over a stream.</summary>
    /// <param name="stream">A seekable stream. It is read, never disposed.</param>
    /// <param name="blockSize">The number of bytes read at a time.</param>
    /// <param name="textCap">The most bytes of a line decoded into text.</param>
    public LineScanner(Stream stream, int blockSize, int textCap)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(textCap, 1);
        _cache = new BlockCache(stream, blockSize);
        _textCap = textCap;
        _prefix = new byte[textCap + 1];
        _chars = new char[textCap + 1];
        ContentStart = _cache.Length >= 3 && _cache.Byte(0) == 0xEF && _cache.Byte(1) == 0xBB && _cache.Byte(2) == 0xBF ? 3 : 0;
    }

    /// <summary>The stream's length, taken when the scanner was made.</summary>
    public long Length => _cache.Length;

    /// <summary>The offset where the content starts: 3 when the stream begins with a UTF-8 byte order mark, otherwise 0.</summary>
    public long ContentStart { get; }

    /// <summary>
    /// The line starting at <paramref name="start"/>, which must end, or be cut off, before <paramref name="end"/>. Only bytes before
    /// <paramref name="end"/> are read.
    /// </summary>
    public LineView Forward(long start, long end)
    {
        // The prefix keeps the first textCap + 1 raw bytes: enough for the text, and the byte after it when truncating.
        int collected = 0;
        int previous = -1;
        long newline = -1;
        long scan = start;
        while (scan < end && newline < 0)
        {
            ReadOnlySpan<byte> window = _cache.Window(scan);
            window = window[..(int)Math.Min(window.Length, end - scan)];
            int index = window.IndexOf((byte)'\n');
            int content = index < 0 ? window.Length : index;
            int take = Math.Min(content, _prefix.Length - collected);
            window[..take].CopyTo(_prefix.AsSpan(collected));
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
            return MakeLine(start, end, false, end - start, collected);
        }

        long contentLength = newline - start - (previous == '\r' ? 1 : 0);
        return MakeLine(start, newline + 1, true, contentLength, collected);
    }

    /// <summary>
    /// The line that ends at <paramref name="end"/>, which must be a line end or the data end, and begins no earlier than
    /// <paramref name="bom"/>.
    /// </summary>
    public LineView Backward(long end, long bom)
    {
        bool terminated = _cache.Byte(end - 1) == '\n';
        long contentEnd = terminated ? end - 1 : end;
        bool carriageReturn = terminated && contentEnd - 1 >= bom && _cache.Byte(contentEnd - 1) == '\r';
        long start = FindLineStart(contentEnd, bom);
        long contentLength = contentEnd - (carriageReturn ? 1 : 0) - start;

        // The start is known now, so the prefix is read forward from it; the line is never buffered whole.
        int collected = (int)Math.Min(contentLength, _prefix.Length);
        CopyOut(start, _prefix.AsSpan(0, collected));
        return MakeLine(start, end, terminated, contentLength, collected);
    }

    /// <summary>The text of a line: its first bytes, decoded as UTF-8 and cut at the text cap. The span is valid until the next call.</summary>
    public ReadOnlySpan<char> Decode(in LineView line)
    {
        int count = Utf8.GetChars(line.Prefix[..line.TextLength], _chars);
        return _chars.AsSpan(0, count);
    }

    /// <summary>Copies raw bytes at an absolute offset. The bytes must lie within the scanner's length.</summary>
    /// <exception cref="EndOfStreamException">The bytes end before the destination is full.</exception>
    public void CopyOut(long offset, Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            ReadOnlySpan<byte> window = _cache.Window(offset);
            if (window.IsEmpty)
            {
                throw new EndOfStreamException("The stream ends before the bytes requested.");
            }

            int count = Math.Min(window.Length, destination.Length);
            window[..count].CopyTo(destination);
            destination = destination[count..];
            offset += count;
        }
    }

    /// <summary>The offset after the last <c>\n</c> before <paramref name="contentEnd"/>, or <paramref name="bom"/> when there is none.</summary>
    private long FindLineStart(long contentEnd, long bom)
    {
        long position = contentEnd;
        while (position > bom)
        {
            long blockStart = position - 1 - (position - 1) % _cache.BlockSize;
            long lower = Math.Max(blockStart, bom);
            ReadOnlySpan<byte> part = _cache.Window(blockStart)[(int)(lower - blockStart)..(int)(position - blockStart)];
            int index = part.LastIndexOf((byte)'\n');
            if (index >= 0)
            {
                return lower + index + 1;
            }

            position = lower;
        }

        return bom;
    }

    /// <summary>Builds a view. <paramref name="collected"/> bytes of the content are in the prefix.</summary>
    private LineView MakeLine(long start, long end, bool terminated, long contentLength, int collected)
    {
        bool truncated = contentLength > _textCap;
        ReadOnlySpan<byte> prefix = _prefix.AsSpan(0, collected);
        int length = truncated ? CutPoint(prefix) : (int)contentLength;
        return new LineView(start, end, terminated, truncated, prefix, length);
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
    /// The stream read in block-aligned windows. Block k covers [k * blockSize, min((k + 1) * blockSize, length)). Two blocks are kept:
    /// the current one and the one before it. Reads are nearly sequential, so a block is read once unless a line reaches back across
    /// two blocks.
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

/// <summary>
/// One line of a stream seen through a <see cref="LineScanner"/>. The prefix is the line's first bytes, up to the text cap plus one, and is
/// valid until the scanner's next call.
/// </summary>
/// <remarks>
/// The text is the first <see cref="TextLength"/> bytes of the prefix, cut at a character boundary when the line is truncated.
/// </remarks>
internal readonly ref struct LineView(long start, long end, bool terminated, bool truncated, ReadOnlySpan<byte> prefix, int textLength)
{
    /// <summary>The offset of the first byte of the line.</summary>
    public long Start { get; } = start;

    /// <summary>The offset after the line, including its <c>\n</c>; the end of data for an unterminated line.</summary>
    public long End { get; } = end;

    /// <summary>Whether the line ends with <c>\n</c>.</summary>
    public bool Terminated { get; } = terminated;

    /// <summary>Whether the content is longer than the text cap, so that the text is a prefix.</summary>
    public bool Truncated { get; } = truncated;

    /// <summary>The first raw bytes of the content, without the line terminator.</summary>
    public ReadOnlySpan<byte> Prefix { get; } = prefix;

    /// <summary>The number of prefix bytes that make the text.</summary>
    public int TextLength { get; } = textLength;
}
