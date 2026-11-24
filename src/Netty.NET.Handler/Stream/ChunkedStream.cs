/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Stream;

/**
 * A {@link ChunkedInput} that fetches data from an {@link InputStream} chunk by
 * chunk.
 * <p>
 * Please note that the {@link InputStream} instance that feeds data into
 * {@link ChunkedStream} must implement {@link InputStream#available()} as
 * accurately as possible, rather than using the default implementation.
 * Otherwise, {@link ChunkedStream} will generate many too small chunks or
 * block unnecessarily often.
 */
// CLR: java.io.InputStream is System.IO.Stream (owner decision: JVM-only I/O families are replaced by the BCL). The
// PushbackInputStream wrapper, which keeps "pushed-back bytes in an internal buffer" so that "the next read operation
// on the input stream will reread the byte that was pushed back"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/PushbackInputStream.html), has no BCL
// counterpart: the byte that isEndOfInput() unreads is kept in the pushback field, and Available()/WriteBytes() read it
// first, as PushbackInputStream.available()/read(byte[], int, int) do. Stream has no available(); see Available(). The
// @Deprecated readChunk(ChannelHandlerContext) is not ported with ChunkedInput's (see IChunkedInput). The public
// members are virtual because the Java class is not final.
public class ChunkedStream : IChunkedInput<ByteBuf>
{

    internal const int DEFAULT_CHUNK_SIZE = 8192;

    private readonly System.IO.Stream @in;
    // CLR: the one-byte pushback buffer of the PushbackInputStream (-1 when empty).
    private int pushback = -1;
    private readonly int chunkSize;
    private long offset;
    private bool closed;

    /**
     * Creates a new instance that fetches data from the specified stream.
     */
    public ChunkedStream(System.IO.Stream @in) : this(@in, DEFAULT_CHUNK_SIZE)
    {
    }

    /**
     * Creates a new instance that fetches data from the specified stream.
     *
     * @param chunkSize the number of bytes to fetch on each
     *                  {@link #readChunk(ChannelHandlerContext)} call
     */
    public ChunkedStream(System.IO.Stream @in, int chunkSize)
    {
        ObjectUtil.CheckNotNull(@in, "in");
        ObjectUtil.CheckPositive(chunkSize, "chunkSize");

        // CLR: the source wraps any stream that is not a PushbackInputStream in one; the pushback field is that buffer.
        this.@in = @in;
        this.chunkSize = chunkSize;
    }

    /**
     * Returns the number of transferred bytes.
     */
    public virtual long TransferredBytes()
    {
        return offset;
    }

    public virtual bool IsEndOfInput()
    {
        if (closed)
        {
            return true;
        }
        if (Available() > 0)
        {
            return false;
        }

        // CLR: in.read() is Stream.ReadByte(), which returns -1 at the end of the stream as InputStream.read() does;
        // in.unread(b) stores the byte in the pushback field.
        int b = @in.ReadByte();
        if (b < 0)
        {
            return true;
        }
        else
        {
            pushback = b;
            return false;
        }
    }

    public virtual void Close()
    {
        closed = true;
        @in.Close();
    }

    public virtual ByteBuf ReadChunk(IByteBufAllocator allocator)
    {
        if (IsEndOfInput())
        {
            return null;
        }

        int availableBytes = Available();
        int chunkSize;
        if (availableBytes <= 0)
        {
            chunkSize = this.chunkSize;
        }
        else
        {
            chunkSize = Math.Min(this.chunkSize, Available());
        }

        bool release = true;
        ByteBuf buffer = allocator.Buffer(chunkSize);
        try
        {
            // transfer to buffer
            int written = WriteBytes(buffer, chunkSize);
            if (written < 0)
            {
                return null;
            }
            offset += written;
            release = false;
            return buffer;
        }
        finally
        {
            if (release)
            {
                buffer.Release();
            }
        }
    }

    public virtual long Length()
    {
        return -1;
    }

    public virtual long Progress()
    {
        return offset;
    }

    // CLR: in.available() of the PushbackInputStream, which "returns the sum of the number of bytes that have been
    // pushed back and the value returned by available" (PushbackInputStream docs above). System.IO.Stream has no
    // available(): a seekable stream reports Length - Position, the bytes left before its end (Stream.Length is "the
    // length of the stream in bytes" and is unsupported only when the stream "does not support seeking",
    // https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.length?view=net-10.0), as ByteArrayInputStream and
    // FileInputStream report; any other stream reports 0, as "The available method of InputStream always returns 0"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/InputStream.html).
    private int Available()
    {
        long available = pushback >= 0 ? 1 : 0;
        if (@in.CanSeek)
        {
            available += Math.Max(0L, @in.Length - @in.Position);
        }
        return (int) Math.Min(available, int.MaxValue);
    }

    // CLR: buffer.writeBytes(in, length) on the PushbackInputStream: its read(byte[], int, int) copies the pushed-back
    // byte first and then reads the rest from the stream once, and returns -1 only when nothing was read at the end of
    // the stream. ByteBuf.WriteBytes(Stream, int) performs one Stream.Read and returns its count, and Stream.Read
    // "returns 0 only if zero bytes were requested or when there is no more data in the stream"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.read?view=net-10.0), so a zero count is Java's -1.
    private int WriteBytes(ByteBuf buffer, int length)
    {
        int avail = 0;
        if (pushback >= 0)
        {
            buffer.WriteByte(pushback);
            pushback = -1;
            avail = 1;
            length--;
        }
        if (length > 0)
        {
            int read = buffer.WriteBytes(@in, length);
            if (read == 0)
            {
                return avail == 0 ? -1 : avail;
            }
            return avail + read;
        }
        return avail;
    }
}
