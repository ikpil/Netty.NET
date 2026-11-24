/*
 * Copyright 2023 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Tests.Pcap;

/**
 * A {@link ByteBufOutputStream} which detects if {@link #close()} was called.
 */
// CLR: the Java test extends ByteBufOutputStream, which is sealed in the Buffer port (the Java class is not final), so
// this System.IO.Stream forwards every member to a ByteBufOutputStream over the same buffer instead. close() is
// Stream.Close(), which disposes the stream (Dispose(true)); the override closes the ByteBufOutputStream
// (super.close()) and then records the call.
internal sealed class CloseDetectingByteBufOutputStream : System.IO.Stream
{

    private readonly ByteBufOutputStream @delegate;
    private bool isCloseCalled;

    /**
     * Creates a new stream which writes data to the specified {@code buffer}.
     */
    internal CloseDetectingByteBufOutputStream(ByteBuf buffer)
    {
        @delegate = new ByteBufOutputStream(buffer);
    }

    public bool CloseCalled()
    {
        return isCloseCalled;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            @delegate.Dispose();
            isCloseCalled = true;
        }
        base.Dispose(disposing);
    }

    public override bool CanRead => @delegate.CanRead;

    public override bool CanSeek => @delegate.CanSeek;

    public override bool CanWrite => @delegate.CanWrite;

    public override long Length => @delegate.Length;

    public override long Position
    {
        get => @delegate.Position;
        set => @delegate.Position = value;
    }

    public override void Flush()
    {
        @delegate.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return @delegate.Read(buffer, offset, count);
    }

    public override long Seek(long offset, System.IO.SeekOrigin origin)
    {
        return @delegate.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        @delegate.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        @delegate.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        @delegate.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        @delegate.WriteByte(value);
    }
}
