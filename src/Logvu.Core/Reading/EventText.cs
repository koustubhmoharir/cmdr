using System.Text;

namespace Logvu.Core.Reading;

/// <summary>Reads the text of an event from its byte extent, with bounded memory.</summary>
public static class EventText
{
    /// <summary>The default cap on the bytes of an event read as text: 4 MiB.</summary>
    public const int DefaultCap = 4 * 1024 * 1024;

    /// <summary>The size of the chunks the event bytes are read in.</summary>
    private const int ChunkSize = 64 * 1024;

    private const byte CarriageReturn = (byte)'\r';
    private const byte LineFeed = (byte)'\n';

    /// <summary>Reads the text of an event.</summary>
    /// <remarks>
    /// <para>
    /// The text is the event's bytes decoded as UTF-8, with each <c>\r\n</c> and <c>\n</c> written as <c>\n</c>. The
    /// final line terminator of the event is dropped. A UTF-8 byte order mark at offset 0 is skipped. Invalid UTF-8
    /// decodes to replacement characters.
    /// </para>
    /// <para>
    /// When the event has more than <paramref name="maxBytes"/> bytes, only the first <paramref name="maxBytes"/> are
    /// read, cut back to a character boundary, and the result is marked truncated. The cut never leaves a replacement
    /// character behind. Only bytes of invalid UTF-8 can make the text's UTF-8 length exceed <paramref name="maxBytes"/>.
    /// </para>
    /// <para>
    /// Memory is bounded by <paramref name="maxBytes"/> plus one chunk, whatever the length of the event.
    /// </para>
    /// </remarks>
    /// <param name="stream">A seekable stream. It is read, never disposed.</param>
    /// <param name="extent">The event to read.</param>
    /// <param name="maxBytes">The most bytes of the event that are read.</param>
    /// <returns>The text, and whether the event was cut to <paramref name="maxBytes"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxBytes"/> is negative, or the extent is negative or ends before it starts.
    /// </exception>
    public static (string Text, bool Truncated) Read(Stream stream, EventExtent extent, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(extent.Start, nameof(extent));
        ArgumentOutOfRangeException.ThrowIfLessThan(extent.End, extent.Start, nameof(extent));

        long start = extent.Start;
        if (start == 0 && extent.End >= 3 && HasByteOrderMark(stream))
        {
            start = 3;
        }

        long available = extent.End - start;
        bool truncated = available > maxBytes;
        int take = (int)Math.Min(available, maxBytes);

        // Each raw byte yields at most one output byte (a CR before a LF is dropped), so take bytes always suffice.
        byte[] output = new byte[take];
        int count = 0;
        bool pendingCarriageReturn = false;
        byte lastByte = 0;
        byte[] chunk = new byte[Math.Min(take, ChunkSize)];

        stream.Position = start;
        for (int remaining = take; remaining > 0;)
        {
            int n = Math.Min(chunk.Length, remaining);
            stream.ReadExactly(chunk, 0, n);
            remaining -= n;
            foreach (byte value in chunk.AsSpan(0, n))
            {
                lastByte = value;
                if (pendingCarriageReturn)
                {
                    pendingCarriageReturn = false;
                    if (value == LineFeed)
                    {
                        output[count++] = LineFeed;
                        continue;
                    }

                    output[count++] = CarriageReturn;
                }

                if (value == CarriageReturn)
                {
                    pendingCarriageReturn = true;
                    continue;
                }

                output[count++] = value;
            }
        }

        // A carriage return at the very end of the bytes read is not followed by a LF within the event.
        if (pendingCarriageReturn)
        {
            output[count++] = CarriageReturn;
        }

        if (!truncated && lastByte == LineFeed)
        {
            // The final terminator of the event. The LF was written as an LF whether it came from "\n" or "\r\n".
            count--;
        }

        if (truncated)
        {
            count = CompleteLength(output.AsSpan(0, count));
        }

        return (Encoding.UTF8.GetString(output, 0, count), truncated);
    }

    /// <summary>Tells whether the stream starts with a UTF-8 byte order mark. The stream must be at least three bytes long.</summary>
    private static bool HasByteOrderMark(Stream stream)
    {
        Span<byte> head = stackalloc byte[3];
        stream.Position = 0;
        stream.ReadExactly(head);
        return head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
    }

    /// <summary>
    /// The length of the prefix of <paramref name="bytes"/> that ends on a character boundary. A trailing sequence whose
    /// lead byte promises more bytes than are present is excluded. Only the last four bytes can belong to such a sequence.
    /// </summary>
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

            return back >= SequenceLength(value) ? length : length - back;
        }

        return length;
    }

    /// <summary>The length of the UTF-8 sequence that starts with <paramref name="lead"/>. An invalid lead counts as one byte.</summary>
    private static int SequenceLength(byte lead) => lead switch
    {
        < 0xC0 => 1,
        < 0xE0 => 2,
        < 0xF0 => 3,
        < 0xF8 => 4,
        _ => 1,
    };
}
