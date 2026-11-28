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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Stream;

/**
 * A {@link ChunkedInput} that fetches data from a {@link ReadableByteChannel}
 * chunk by chunk.  Please note that the {@link ReadableByteChannel} must
 * operate in blocking mode.  Non-blocking mode channels are not supported.
 */
// CLR: the NIO ReadableByteChannel is System.IO.Stream (owner decision: JVM-only NIO is replaced by the BCL; the tests'
// Channels.newChannel(InputStream) adapts a stream, which is the Stream itself here). Stream.Read blocks: "If more than
// zero bytes are requested, the implementation will not complete the operation until at least one byte of data can be
// read" (https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.read?view=net-10.0). in.isOpen() is
// Stream.CanRead, which a closed stream reports as false (FileStream and MemoryStream: "If the stream is closed, this
// property returns false", https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.canread?view=net-10.0,
// https://learn.microsoft.com/en-us/dotnet/api/system.io.memorystream.canread?view=net-10.0). The NIO ByteBuffer is a
// byte array with its fill position (byteBufferPosition): flip() and writeBytes(ByteBuffer) copy the filled prefix and
// clear() resets the position. The @Deprecated readChunk(ChannelHandlerContext) is not ported with ChunkedInput's (see
// IChunkedInput). The public members are virtual because the Java class is not final.
public class ChunkedNioStream : IChunkedInput<ByteBuf>
{

    private readonly System.IO.Stream @in;

    private readonly int chunkSize;
    private long offset;

    /**
     * Associated ByteBuffer
     */
    private readonly byte[] byteBuffer;
    // CLR: the position of the ByteBuffer (the number of bytes read into it).
    private int byteBufferPosition;

    /**
     * Creates a new instance that fetches data from the specified channel.
     */
    public ChunkedNioStream(System.IO.Stream @in) : this(@in, ChunkedStream.DEFAULT_CHUNK_SIZE)
    {
    }

    /**
     * Creates a new instance that fetches data from the specified channel.
     *
     * @param chunkSize the number of bytes to fetch on each
     *                  {@link #readChunk(ChannelHandlerContext)} call
     */
    public ChunkedNioStream(System.IO.Stream @in, int chunkSize)
    {
        this.@in = CheckNotNull(@in, "in");
        this.chunkSize = CheckPositive(chunkSize, "chunkSize");
        byteBuffer = new byte[chunkSize];
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
        if (byteBufferPosition > 0)
        {
            // A previous read was not over, so there is a next chunk in the buffer at least
            return false;
        }

        if (@in.CanRead)
        {
            // Try to read a new part, and keep this part (no rewind)
            int b = Read();
            if (b < 0)
            {
                return true;
            }
            else
            {
                offset += b;
                return false;
            }
        }

        return true;
    }

    public virtual void Close()
    {
        @in.Close();
    }

    public virtual ByteBuf ReadChunk(IByteBufAllocator allocator)
    {
        if (IsEndOfInput())
        {
            return null;
        }

        // buffer cannot be not be empty from there
        int readBytes = byteBufferPosition;

        for (;;)
        {
            int localReadBytes = Read();
            if (localReadBytes < 0)
            {
                break;
            }

            readBytes += localReadBytes;
            offset += localReadBytes;
            if (readBytes == chunkSize)
            {
                break;
            }
        }

        // CLR: byteBuffer.flip(): the filled prefix is byteBuffer[0, byteBufferPosition).
        bool release = true;
        ByteBuf buffer = allocator.Buffer(byteBufferPosition);

        try
        {
            buffer.WriteBytes(byteBuffer.AsSpan(0, byteBufferPosition));
            byteBufferPosition = 0;
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

    // CLR: in.read(byteBuffer) of the ReadableByteChannel: "An attempt is made to read up to r bytes from the channel,
    // where r is the number of bytes remaining in the buffer", and it returns "The number of bytes read, possibly zero,
    // or -1 if the channel has reached end-of-stream"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/channels/ReadableByteChannel.html). A full
    // buffer reads nothing and returns 0; otherwise Stream.Read returns 0 only at the end of the stream (Stream.Read
    // docs above), which is -1 here.
    private int Read()
    {
        int remaining = byteBuffer.Length - byteBufferPosition;
        if (remaining == 0)
        {
            return 0;
        }

        int read = @in.Read(byteBuffer, byteBufferPosition, remaining);
        if (read == 0)
        {
            return -1;
        }

        byteBufferPosition += read;
        return read;
    }
}
