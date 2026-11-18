namespace Logvu.Core.Store;

/// <summary>
/// A read-only view of the first <see cref="Length"/> bytes of a stream. A framer over the view treats those bytes as a closed file,
/// so history reads stop at a member's read cursor. The view does not own the stream and does not dispose it.
/// </summary>
internal sealed class ReadWindow : Stream
{
    private readonly Stream _inner;
    private long _position;

    /// <param name="inner">The stream to read. It must be at least <paramref name="length"/> bytes long.</param>
    /// <param name="length">The length of the view.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    public ReadWindow(Stream inner, long length)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _inner = inner;
        Length = length;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => true;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length { get; }

    /// <inheritdoc/>
    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        if (_position >= Length || buffer.IsEmpty)
        {
            return 0;
        }

        int count = (int)Math.Min(buffer.Length, Length - _position);
        _inner.Position = _position;
        int read = _inner.Read(buffer[..count]);
        _position += read;
        return read;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        return _position;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: the view is read-only.</exception>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: the view is read-only.</exception>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
