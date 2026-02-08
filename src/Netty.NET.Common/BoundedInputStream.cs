using System;
using System.IO;

namespace Netty.NET.Common;

public sealed class BoundedInputStream : Stream
{
    private readonly Stream _inner;
    private long _remaining;

    public BoundedInputStream(Stream inner, long limit)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        _remaining = limit;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0)
            return 0;

        int toRead = (int)Math.Min(count, _remaining);
        int read = _inner.Read(buffer, offset, toRead);
        _remaining -= read;
        return read;
    }

    public override int ReadByte()
    {
        if (_remaining <= 0)
            return -1; // EOF

        int value = _inner.ReadByte();
        if (value == -1)
            return -1;

        _remaining--;
        return value; 
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
