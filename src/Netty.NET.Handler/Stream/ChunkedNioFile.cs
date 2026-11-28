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
using System.Globalization;
using System.IO;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Stream;

/**
 * A {@link ChunkedInput} that fetches data from a file chunk by chunk using
 * NIO {@link FileChannel}.
 * <p>
 * If your operating system supports
 * <a href="https://en.wikipedia.org/wiki/Zero-copy">zero-copy file transfer</a>
 * such as {@code sendfile()}, you might want to use {@link FileRegion} instead.
 */
// CLR: the NIO FileChannel is a FileStream (owner decision: JVM-only NIO is replaced by the BCL), the file type
// the Transport DefaultFileRegion takes for a FileChannel, and java.io.File is FileInfo; new RandomAccessFile(in,
// "r").getChannel() opens a read-only FileStream. The positional FileChannel read, which "does not modify this
// channel's position"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/channels/FileChannel.html), is
// ByteBuf.WriteBytes(SafeFileHandle, long, int) on the stream's handle, the Buffer port's FileChannel overload
// (System.IO RandomAccess; the file position never changes, docs/buffer-porting.md, Positioned file transfers).
// in.isOpen() is FileStream.CanRead, "false if the stream is closed or was opened with write-only access"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.canread?view=net-10.0); in.size() is
// FileStream.Length. java.nio.channels.ClosedChannelException is the Transport ClosedChannelException. The
// @Deprecated readChunk(ChannelHandlerContext) is not ported with ChunkedInput's (see IChunkedInput). The public
// members are virtual because the Java class is not final.
public class ChunkedNioFile : IChunkedInput<ByteBuf>
{

    private readonly FileStream @in;
    private readonly long startOffset;
    private readonly long endOffset;
    private readonly int chunkSize;
    private long offset;

    /**
     * Creates a new instance that fetches data from the specified file.
     */
    public ChunkedNioFile(FileInfo @in) : this(new FileStream(@in.FullName, FileMode.Open, FileAccess.Read))
    {
    }

    /**
     * Creates a new instance that fetches data from the specified file.
     *
     * @param chunkSize the number of bytes to fetch on each
     *                  {@link #readChunk(ChannelHandlerContext)} call
     */
    public ChunkedNioFile(FileInfo @in, int chunkSize)
        : this(new FileStream(@in.FullName, FileMode.Open, FileAccess.Read), chunkSize)
    {
    }

    /**
     * Creates a new instance that fetches data from the specified file.
     */
    public ChunkedNioFile(FileStream @in) : this(@in, ChunkedStream.DEFAULT_CHUNK_SIZE)
    {
    }

    /**
     * Creates a new instance that fetches data from the specified file.
     *
     * @param chunkSize the number of bytes to fetch on each
     *                  {@link #readChunk(ChannelHandlerContext)} call
     */
    public ChunkedNioFile(FileStream @in, int chunkSize) : this(@in, 0, Size(@in), chunkSize)
    {
    }

    /**
     * Creates a new instance that fetches data from the specified file.
     *
     * @param offset the offset of the file where the transfer begins
     * @param length the number of bytes to transfer
     * @param chunkSize the number of bytes to fetch on each
     *                  {@link #readChunk(ChannelHandlerContext)} call
     */
    public ChunkedNioFile(FileStream @in, long offset, long length, int chunkSize)
    {
        ObjectUtil.CheckNotNull(@in, "in");
        ObjectUtil.CheckPositiveOrZero(offset, "offset");
        ObjectUtil.CheckPositiveOrZero(length, "length");
        ObjectUtil.CheckPositive(chunkSize, "chunkSize");
        if (!@in.CanRead)
        {
            throw new ClosedChannelException();
        }

        this.@in = @in;
        this.chunkSize = chunkSize;
        this.offset = startOffset = offset;
        endOffset = offset + length;
    }

    // CLR: in.size() throws ClosedChannelException "If this channel is closed" (FileChannel docs above), while
    // FileStream.Length of a closed stream throws ObjectDisposedException; this keeps the channel's exception for the
    // (FileStream, int) constructor, which reads the size before the checks of the main constructor run.
    private static long Size(FileStream @in)
    {
        if (@in != null && !@in.CanRead)
        {
            throw new ClosedChannelException();
        }

        return @in.Length;
    }

    /**
     * Returns the offset in the file where the transfer began.
     */
    public virtual long StartOffset()
    {
        return startOffset;
    }

    /**
     * Returns the offset in the file where the transfer will end.
     */
    public virtual long EndOffset()
    {
        return endOffset;
    }

    /**
     * Returns the offset in the file where the transfer is happening currently.
     */
    public virtual long CurrentOffset()
    {
        return offset;
    }

    public virtual bool IsEndOfInput()
    {
        return !(offset < endOffset && @in.CanRead);
    }

    public virtual void Close()
    {
        @in.Close();
    }

    public virtual ByteBuf ReadChunk(IByteBufAllocator allocator)
    {
        long offset = this.offset;
        if (offset >= endOffset)
        {
            return null;
        }

        int chunkSize = (int) Math.Min(this.chunkSize, endOffset - offset);
        ByteBuf buffer = allocator.Buffer(chunkSize);
        bool release = true;

        try
        {
            int readBytes = 0;

            for (;;)
            {
                int localReadBytes = buffer.WriteBytes(@in.SafeFileHandle, offset + readBytes, chunkSize - readBytes);
                // CLR: FileChannel.read returns -1 "if the given position is greater than or equal to the file's
                // current size"; the Buffer port reports the end of the file as 0, and the requested length is
                // never 0 here, so 0 is the end of the file.
                if (localReadBytes <= 0)
                {
                    // The file is shorter than the region we were asked to transfer (for example it was truncated
                    // after this ChunkedNioFile was created). Fail like ChunkedFile instead of returning empty
                    // chunks forever, as isEndOfInput() would never return true.
                    // CLR: EOFException is EndOfStreamException (an IOException, as in ChunkedFile); the numbers
                    // use the invariant culture, as Java decimal text does not depend on a culture.
                    throw new EndOfStreamException("Underlying file size " +
                            @in.Length.ToString(CultureInfo.InvariantCulture) + " smaller than requested end " +
                            "offset " + endOffset.ToString(CultureInfo.InvariantCulture));
                }

                readBytes += localReadBytes;
                if (readBytes == chunkSize)
                {
                    break;
                }
            }

            this.offset += readBytes;
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
        return endOffset - startOffset;
    }

    public virtual long Progress()
    {
        return offset - startOffset;
    }
}
