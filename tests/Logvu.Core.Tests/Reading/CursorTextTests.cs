using System.Text;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;

namespace Logvu.Core.Tests.Reading;

/// <summary>
/// The text an event cursor reads from its own blocks, and the text <see cref="EventText.Read"/> reads from a stream, must be the same for
/// every event, in both framing directions, at every block size and text cap. <see cref="Reference"/> is the decoding of
/// <see cref="EventText"/> before the span-based decoder: it is kept here as the model the fast path must agree with.
/// </summary>
public sealed class CursorTextTests
{
    private const int Cases = 300;
    private const int TextCap = 1024 * 1024;

    /// <summary>The pieces of a random document, as raw bytes: ASCII, line ends, multi-byte characters, and invalid sequences.</summary>
    private static readonly byte[][] Pieces =
    [
        "a"u8.ToArray(),
        "b"u8.ToArray(),
        "INFO"u8.ToArray(),
        " "u8.ToArray(),
        "\r"u8.ToArray(),
        "\n"u8.ToArray(),
        "\r\n"u8.ToArray(),
        "\n\n"u8.ToArray(),
        "é"u8.ToArray(),
        "😀"u8.ToArray(),
        [0xFF],
        [0xE2, 0x82],
        [0xC3],
        "   at X.Y()"u8.ToArray(),
        "tail\r"u8.ToArray(),
        [0x00],
        "\t"u8.ToArray(),
    ];

    /// <summary>The text of an event as the original streaming decoder reads it: the same bytes, the same rules.</summary>
    internal static (string Text, bool Truncated) Reference(byte[] data, long start, long end, int maxBytes)
    {
        if (start == 0 && end >= 3 && data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            start = 3;
        }

        long available = end - start;
        bool truncated = available > maxBytes;
        int take = (int)Math.Min(available, maxBytes);
        var output = new byte[take];
        int count = 0;
        bool pendingCarriageReturn = false;
        byte lastByte = 0;
        for (int i = 0; i < take; i++)
        {
            byte value = data[start + i];
            lastByte = value;
            if (pendingCarriageReturn)
            {
                pendingCarriageReturn = false;
                if (value == (byte)'\n')
                {
                    output[count++] = (byte)'\n';
                    continue;
                }

                output[count++] = (byte)'\r';
            }

            if (value == (byte)'\r')
            {
                pendingCarriageReturn = true;
                continue;
            }

            output[count++] = value;
        }

        if (pendingCarriageReturn)
        {
            output[count++] = (byte)'\r';
        }

        if (!truncated && lastByte == (byte)'\n')
        {
            count--;
        }

        if (truncated)
        {
            count = CompleteLength(output.AsSpan(0, count));
        }

        return (Encoding.UTF8.GetString(output, 0, count), truncated);
    }

    private static int CompleteLength(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.Length;
        for (int back = 1; back <= Math.Min(4, length); back++)
        {
            byte value = bytes[length - back];
            if ((value & 0xC0) == 0x80)
            {
                continue;
            }

            int sequence = value < 0xC0 ? 1 : value < 0xE0 ? 2 : value < 0xF0 ? 3 : value < 0xF8 ? 4 : 1;
            return back >= sequence ? length : length - back;
        }

        return length;
    }

    [Fact]
    public void Event_text_from_a_stream_equals_the_reference_for_random_extents_and_caps()
    {
        var rng = new Random(20261201);
        for (int i = 0; i < Cases; i++)
        {
            byte[] data = RandomDocument(rng);
            using var stream = new MemoryStream(data, writable: false);
            long a = rng.Next(0, data.Length + 1);
            long b = rng.Next((int)a, data.Length + 1);
            int cap = rng.Next(3) == 0 ? rng.Next(1, 12) : rng.Next(1, 200);
            var extent = new EventExtent(a, b, b, Provisional: false, Orphan: false);

            (string text, bool truncated) = EventText.Read(stream, extent, cap);
            (string expected, bool expectedTruncated) = Reference(data, a, b, cap);
            Assert.True(expected == text && expectedTruncated == truncated, $"case {i}: [{a}, {b}) cap {cap}: '{text}' vs '{expected}'");
        }
    }

    [Fact]
    public void Cursor_text_equals_stream_text_for_every_event_in_both_directions()
    {
        var rng = new Random(20261202);
        for (int i = 0; i < Cases; i++)
        {
            byte[] data = RandomDocument(rng);
            ILogScheme scheme = i % 2 == 0 ? BuiltInSchemes.Log4NetCore : BuiltInSchemes.SlogJson;
            int blockSize = rng.Next(1, 40);
            int textCap = rng.Next(4, 60);
            int maxBytes = rng.Next(3) == 0 ? rng.Next(1, 30) : TextCap;
            bool live = rng.Next(2) == 0;

            using var stream = new MemoryStream(data, writable: false);
            var framer = new EventFramer(stream, scheme, live, blockSize, textCap);
            EventExtent[] forward = framer.ReadForward(0).ToArray();
            EventExtent[] backward = framer.ReadBackward(stream.Length).ToArray();
            Assert.Equal(forward.Reverse(), backward);

            EventCursor cursor = framer.Forward(0);
            foreach (EventExtent extent in forward)
            {
                Assert.True(cursor.MoveNext(out EventExtent got));
                Assert.Equal(extent, got);
                AssertSameText(i, cursor.ReadText(got, maxBytes), EventText.Read(stream, got, maxBytes), got);
            }

            Assert.False(cursor.MoveNext(out _));

            EventCursor back = framer.Backward(stream.Length);
            foreach (EventExtent extent in backward)
            {
                Assert.True(back.MoveNext(out EventExtent got));
                Assert.Equal(extent, got);
                AssertSameText(i, back.ReadText(got, maxBytes), EventText.Read(stream, got, maxBytes), got);
            }

            Assert.False(back.MoveNext(out _));
        }
    }

    private static void AssertSameText(int caseIndex, string fromCursor, (string Text, bool Truncated) fromStream, EventExtent extent)
    {
        Assert.True(fromCursor == fromStream.Text, $"case {caseIndex}, event [{extent.Start}, {extent.End}): '{fromCursor}' vs '{fromStream.Text}'");
    }

    /// <summary>A random document of log-like lines, pieces of every kind, with a byte order mark sometimes.</summary>
    private static byte[] RandomDocument(Random rng)
    {
        var bytes = new List<byte>();
        if (rng.Next(3) == 0)
        {
            bytes.AddRange([0xEF, 0xBB, 0xBF]);
        }

        int lines = rng.Next(0, 30);
        for (int line = 0; line < lines; line++)
        {
            if (rng.Next(3) == 0)
            {
                bytes.AddRange(Encoding.UTF8.GetBytes("ERROR 2026-11-18 10:20:30,123 [1] A.B - header " + line));
            }
            else if (rng.Next(3) == 0)
            {
                bytes.AddRange(Encoding.UTF8.GetBytes("{\"time\":\"2026-11-18T10:20:30Z\",\"level\":\"INFO\",\"msg\":\"m" + line + "\"}"));
            }

            int pieces = rng.Next(0, 6);
            for (int piece = 0; piece < pieces; piece++)
            {
                bytes.AddRange(Pieces[rng.Next(Pieces.Length)]);
            }

            if (rng.Next(2) == 0)
            {
                bytes.Add((byte)'\n');
            }
        }

        return bytes.ToArray();
    }
}
