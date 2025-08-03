/*
 * Copyright 2024 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.IO;

namespace Netty.NET.Common.Internal;

public class BoundedStream : Stream
{
    private readonly Stream _innerStream;
    private readonly int _maxBytesRead;
    private int _numRead;

    public BoundedStream(Stream innerStream, int maxBytesRead)
    {
        if (innerStream == null)
            throw new ArgumentNullException(nameof(innerStream));
        if (maxBytesRead <= 0)
            throw new ArgumentException("maxBytesRead must be positive.", nameof(maxBytesRead));

        _innerStream = innerStream;
        _maxBytesRead = maxBytesRead;
        _numRead = 0;
    }

    public BoundedStream(Stream innerStream)
        : this(innerStream, 8 * 1024)
    {
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        CheckMaxBytesRead();
        
        int num = Math.Min(count, _maxBytesRead - _numRead + 1);

        int bytesRead = _innerStream.Read(buffer, offset, num);
        if (bytesRead != -1)
        {
            _numRead += bytesRead;
        }

        return bytesRead;
    }

    public override int ReadByte()
    {
        CheckMaxBytesRead();

        int b = _innerStream.ReadByte();
        if (b != -1)
        {
            _numRead++;
        }

        return b;
    }

    private void CheckMaxBytesRead()
    {
        if (_numRead > _maxBytesRead)
        {
            throw new IOException($"Maximum number of bytes read: {_numRead}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerStream.Dispose();
        }

        base.Dispose(disposing);
    }
}