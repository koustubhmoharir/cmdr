using System.Text;
using Logvu.Core.Reading;

namespace Logvu.Core.Tests.Reading;

public sealed class LineReaderTests
{
    private static readonly UTF8Encoding Utf8 = new(false, false);

    private static RawLine Line(long start, long end, bool terminated, string text, bool truncated = false) =>
        new(start, end, terminated, text, truncated);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static RawLine[] Forward(byte[] data, int blockSize = 64 * 1024, int textCap = LineReader.TextCap)
    {
        using var stream = new MemoryStream(data, writable: false);
        var reader = new LineReader(stream, blockSize, textCap);
        return reader.ReadForward(LineReader.ContentStart(stream), stream.Length).ToArray();
    }

    private static RawLine[] Backward(byte[] data, int blockSize = 64 * 1024, int textCap = LineReader.TextCap)
    {
        using var stream = new MemoryStream(data, writable: false);
        var reader = new LineReader(stream, blockSize, textCap);
        return reader.ReadBackward(stream.Length).ToArray();
    }

    /// <summary>Asserts the same lines in both directions.</summary>
    private static void AssertBothDirections(string text, params RawLine[] expected)
    {
        byte[] data = Bytes(text);
        Assert.Equal(expected, Forward(data));
        Assert.Equal(expected.Reverse(), Backward(data));
    }

    [Fact]
    public void Empty_stream_has_no_lines_in_either_direction()
    {
        AssertBothDirections("");
    }

    [Fact]
    public void Bom_only_stream_has_no_lines_in_either_direction()
    {
        AssertBothDirections("﻿");
    }

    [Fact]
    public void Content_start_is_3_only_for_a_complete_bom()
    {
        using (var empty = new MemoryStream([]))
        using (var partial = new MemoryStream([0xEF, 0xBB]))
        using (var bom = new MemoryStream([0xEF, 0xBB, 0xBF, (byte)'x']))
        using (var text = new MemoryStream(Bytes("abc")))
        {
            Assert.Equal(0, LineReader.ContentStart(empty));
            Assert.Equal(0, LineReader.ContentStart(partial));
            Assert.Equal(3, LineReader.ContentStart(bom));
            Assert.Equal(0, LineReader.ContentStart(text));
        }
    }

    [Fact]
    public void Newline_terminated_lines()
    {
        AssertBothDirections("a\nb\n",
            Line(0, 2, true, "a"),
            Line(2, 4, true, "b"));
    }

    [Fact]
    public void Last_line_without_newline_is_unterminated()
    {
        AssertBothDirections("a\nb",
            Line(0, 2, true, "a"),
            Line(2, 3, false, "b"));
    }

    [Fact]
    public void Crlf_is_stripped_as_one_unit()
    {
        AssertBothDirections("a\r\nb\r\n",
            Line(0, 3, true, "a"),
            Line(3, 6, true, "b"));
    }

    [Fact]
    public void Blank_lines()
    {
        AssertBothDirections("\n\n",
            Line(0, 1, true, ""),
            Line(1, 2, true, ""));
    }

    [Fact]
    public void Lone_carriage_return_without_newline_stays_in_text()
    {
        AssertBothDirections("a\r",
            Line(0, 2, false, "a\r"));
    }

    [Fact]
    public void Bom_is_excluded_from_the_first_line()
    {
        AssertBothDirections("﻿x\ny\n",
            Line(3, 5, true, "x"),
            Line(5, 7, true, "y"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void Multibyte_characters_straddling_blocks_decode_correctly(int blockSize)
    {
        // "é" is 2 bytes, "€" 3, "😀" 4; "ü" is 2. Line 1 is 9 content bytes, line 2 is 2.
        byte[] data = Bytes("é€😀\nü\n");
        RawLine[] expected =
        [
            Line(0, 10, true, "é€😀"),
            Line(10, 13, true, "ü"),
        ];

        Assert.Equal(expected, Forward(data, blockSize));
        Assert.Equal(expected.Reverse(), Backward(data, blockSize));
    }

    [Fact]
    public void Long_line_is_cut_at_a_character_boundary_with_bounded_text()
    {
        // The 3-byte "€" occupies bytes 9..11, so a cut at byte 10 falls inside it.
        string head = "abcdefghi€";
        byte[] longLine = [.. Bytes(head), .. Enumerable.Repeat((byte)'x', 100_000 - 12)];
        Assert.Equal(100_000, longLine.Length);

        byte[] data = [.. Bytes("p\n"), .. longLine, .. Bytes("\n"), .. Bytes("q\n")];
        RawLine[] expected =
        [
            Line(0, 2, true, "p"),
            Line(2, 100_003, true, "abcdefghi", truncated: true),
            Line(100_003, 100_005, true, "q"),
        ];

        Assert.Equal(expected, Forward(data, blockSize: 16, textCap: 10));
        Assert.Equal(expected.Reverse(), Backward(data, blockSize: 16, textCap: 10));
    }

    [Fact]
    public void Invalid_utf8_decodes_to_replacement_characters()
    {
        byte[] data = [(byte)'a', 0xFF, (byte)'b', (byte)'\n'];
        RawLine[] expected = [Line(0, 4, true, "a�b")];

        Assert.Equal(expected, Forward(data));
        Assert.Equal(expected, Backward(data));
    }

    [Fact]
    public void Forward_from_second_line_stops_at_limit_inside_third_line()
    {
        using var stream = new MemoryStream(Bytes("a\nbb\nccc\n"), writable: false);
        var reader = new LineReader(stream, blockSize: 2);

        RawLine[] forward = reader.ReadForward(from: 2, limit: 7).ToArray();

        Assert.Equal([Line(2, 5, true, "bb"), Line(5, 7, false, "cc")], forward);
    }

    [Fact]
    public void Backward_from_end_of_second_line_yields_second_then_first()
    {
        using var stream = new MemoryStream(Bytes("a\nbb\nccc\n"), writable: false);
        var reader = new LineReader(stream, blockSize: 2);

        RawLine[] backward = reader.ReadBackward(before: 5).ToArray();

        Assert.Equal([Line(2, 5, true, "bb"), Line(0, 2, true, "a")], backward);
    }

    [Fact]
    public void Limit_beyond_the_data_is_clamped_to_it()
    {
        using var stream = new MemoryStream(Bytes("a\nb"), writable: false);
        var reader = new LineReader(stream);

        Assert.Equal([Line(0, 2, true, "a"), Line(2, 3, false, "b")], reader.ReadForward(0, 100).ToArray());
    }

    [Fact]
    public void Offsets_outside_the_stream_are_rejected_when_called()
    {
        using var stream = new MemoryStream(Bytes("abc"), writable: false);
        var reader = new LineReader(stream);

        Assert.Throws<ArgumentOutOfRangeException>(() => { reader.ReadForward(4, 4); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { reader.ReadForward(-1, 4); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { reader.ReadBackward(4); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { reader.ReadBackward(-1); });
    }

    [Fact]
    public void Ten_megabyte_line_is_read_with_small_blocks()
    {
        byte[] data = new byte[10 * 1024 * 1024];
        Array.Fill(data, (byte)'x');

        RawLine[] forward = Forward(data, blockSize: 4096);
        RawLine[] backward = Backward(data, blockSize: 4096);

        RawLine expected = Line(0, data.Length, false, new string('x', LineReader.TextCap), truncated: true);
        Assert.Equal([expected], forward);
        Assert.Equal([expected], backward);
    }

    [Fact]
    public void Whole_file_scans_read_each_block_about_once()
    {
        // Lines are shorter than a block, so no line reaches back across more than one block boundary.
        var random = new Random(7);
        var text = new StringBuilder();
        for (int i = 0; i < 2000; i++)
        {
            text.Append((char)('a' + random.Next(26)), random.Next(0, 200)).Append('\n');
        }

        byte[] data = Bytes(text.ToString());
        const int blockSize = 4096;
        using var stream = new MemoryStream(data, writable: false);
        using var counting = new CountingStream(stream);
        var reader = new LineReader(counting, blockSize);

        counting.BytesRead = 0;
        RawLine[] forward = reader.ReadForward(0, counting.Length).ToArray();
        long forwardBytes = counting.BytesRead;

        counting.BytesRead = 0;
        RawLine[] backward = reader.ReadBackward(counting.Length).ToArray();
        long backwardBytes = counting.BytesRead;

        Assert.Equal(2000, forward.Length);
        Assert.Equal(forward.Reverse(), backward);
        // Forward reads each block once. Backward also reads block 0 twice: the content-start check loads it, and the
        // scan reaches it again after it has left the cache.
        Assert.True(forwardBytes <= data.Length, $"forward read {forwardBytes} bytes of {data.Length}");
        Assert.True(backwardBytes <= data.Length + blockSize, $"backward read {backwardBytes} bytes of {data.Length}");
    }

    [Fact]
    public void Random_inputs_frame_the_same_in_both_directions()
    {
        var random = new Random(20261118);
        for (int run = 0; run < 300; run++)
        {
            byte[] data = RandomInput(random);
            int blockSize = random.Next(1, 18);
            int textCap = random.Next(1, 41);
            string description = $"input {Convert.ToHexString(data)} block {blockSize} cap {textCap}";

            using var stream = new MemoryStream(data, writable: false);
            var reader = new LineReader(stream, blockSize, textCap);
            long contentStart = LineReader.ContentStart(stream);
            RawLine[] forward = reader.ReadForward(contentStart, stream.Length).ToArray();
            RawLine[] backward = reader.ReadBackward(stream.Length).ToArray();

            Assert.True(backward.SequenceEqual(forward.Reverse()), $"backward differs: {description}");

            long expectedStart = contentStart;
            foreach (RawLine line in forward)
            {
                Assert.True(line.Start == expectedStart, $"gap or overlap at {line.Start}: {description}");
                expectedStart = line.End;
            }

            Assert.True(expectedStart == data.Length, $"extents do not cover the data: {description}");
            for (int i = 0; i < forward.Length; i++)
            {
                bool isLast = i == forward.Length - 1;
                Assert.True(isLast || forward[i].Terminated, $"only the last line may be unterminated: {description}");
            }

            if (forward.Length > 0)
            {
                Assert.Equal(data[^1] == '\n', forward[^1].Terminated);
            }

            foreach (RawLine line in forward)
            {
                int contentLength = (int)(line.End - line.Start) - (line.Terminated ? 1 : 0);
                if (line.Terminated && contentLength > 0 && data[line.Start + contentLength - 1] == '\r')
                {
                    contentLength--;
                }

                Assert.True(line.Truncated == contentLength > textCap, $"truncation flag wrong at {line.Start}: {description}");
                string reference = Utf8.GetString(data, (int)line.Start, contentLength);
                bool textMatches = line.Truncated
                    ? reference.StartsWith(line.Text, StringComparison.Ordinal)
                    : line.Text == reference;
                Assert.True(textMatches, $"text at {line.Start} is {line.Text:S}, reference {reference:S}: {description}");
            }
        }
    }

    /// <summary>Forwards to an inner stream and counts the bytes its reads return.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; set; }

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Random content: ASCII, multi-byte characters, LF, CRLF, lone CR, an optional BOM and an optional unterminated tail. At most 200 bytes.</summary>
    private static byte[] RandomInput(Random random)
    {
        string[] pieces = ["a", "bc", "é", "€", "😀", "\n", "\r\n", "\r", "xyz"];
        var bytes = new List<byte>();
        if (random.Next(2) == 0)
        {
            bytes.AddRange([0xEF, 0xBB, 0xBF]);
        }

        int target = random.Next(0, 201);
        while (bytes.Count < target)
        {
            byte[] piece = Bytes(pieces[random.Next(pieces.Length)]);
            if (bytes.Count + piece.Length > 200)
            {
                break;
            }

            bytes.AddRange(piece);
        }

        if (random.Next(2) == 0 && bytes.Count + 4 <= 200)
        {
            bytes.AddRange(Bytes("tail"));
        }

        return bytes.ToArray();
    }
}
