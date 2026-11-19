using System.Text;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;

namespace Logvu.Core.Tests.Reading;

/// <summary>
/// A file truncated while it is being read: the length captured at the start of an enumeration no longer holds, and the read
/// ends with an <see cref="EndOfStreamException"/>. That is an <see cref="IOException"/>, which the store's readers record as a
/// read error for the member (and the reconciler then sees the truncation), so it must not surface as anything else.
/// </summary>
public sealed class ShrinkingStreamTests
{
    private static byte[] Log(int events)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < events; i++)
        {
            sb.Append($"INFO  2026-11-18 10:20:{i % 60:00},000 [1] SheetKraft.Engine.Runner - event {i}\n");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Fact]
    public void Lines_read_forward_past_a_truncation_end_with_an_io_exception()
    {
        using var stream = new ShrinkingStream(Log(200), shrinkAfterReads: 2, shrinkTo: 100);
        var reader = new LineReader(stream, blockSize: 256);

        IOException error = Assert.ThrowsAny<IOException>(() => reader.ReadForward(0, stream.Length).ToList());
        Assert.IsType<EndOfStreamException>(error);
    }

    [Fact]
    public void Events_framed_backward_past_a_truncation_end_with_an_io_exception()
    {
        using var stream = new ShrinkingStream(Log(200), shrinkAfterReads: 3, shrinkTo: 100);
        var framer = new EventFramer(stream, BuiltInSchemes.Log4NetCore, live: false, blockSize: 256);

        Assert.ThrowsAny<IOException>(() => framer.ReadBackward(long.MaxValue).ToList());
    }

    /// <summary>A stream whose content is cut to <c>shrinkTo</c> bytes after a number of reads, as a writer truncating the file would.</summary>
    private sealed class ShrinkingStream(byte[] data, int shrinkAfterReads, int shrinkTo) : Stream
    {
        private int _reads;
        private long _length = data.Length;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (++_reads > shrinkAfterReads)
            {
                _length = Math.Min(_length, shrinkTo);
            }

            int available = (int)Math.Max(0, Math.Min(count, _length - Position));
            Array.Copy(data, Position, buffer, offset, available);
            Position += available;
            return available;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => _length + offset,
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
