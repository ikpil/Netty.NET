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
using System.IO;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Stream;

/**
 * A {@link ChunkedInput} that fetches data from a file chunk by chunk.
 * <p>
 * If your operating system supports
 * <a href="https://en.wikipedia.org/wiki/Zero-copy">zero-copy file transfer</a>
 * such as {@code sendfile()}, you might want to use {@link FileRegion} instead.
 */
// CLR: java.io.File is FileInfo (docs/common-clr-design.md; Transport DefaultFileRegion takes a FileInfo) and
// RandomAccessFile is FileStream (the type Transport DefaultFileRegion takes for a file, docs/transport-porting.md);
// new RandomAccessFile(file, "r") opens a read-only FileStream. file.length() is FileStream.Length, file.seek(offset)
// is Seek(offset, SeekOrigin.Begin) ("Sets the file-pointer offset, measured from the beginning of this file",
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/RandomAccessFile.html), and
// file.getChannel().isOpen() is FileStream.CanRead, which is "false if the stream is closed or was opened with
// write-only access" (https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.canread?view=net-10.0); a
// RandomAccessFile is always readable ("r" or "rw"). The @Deprecated readChunk(ChannelHandlerContext) is not ported
// with ChunkedInput's (see IChunkedInput). The public members are virtual because the Java class is not final.
public class ChunkedFile : IChunkedInput<ByteBuf>
{

    private readonly FileStream file;
    private readonly long startOffset;
    private readonly long endOffset;
    private readonly int chunkSize;
    private long offset;

    /**
     * Creates a new instance that fetches data from the specified file.
     */
    public ChunkedFile(FileInfo file) : this(file, ChunkedStream.DEFAULT_CHUNK_SIZE)
    {
    }

    /**
     * Creates a new instance that fetches data from the specified file.
     *
     * @param chunkSize the number of bytes to fetch on each
     *                  {@link #readChunk(ChannelHandlerContext)} call
     */
    public ChunkedFile(FileInfo file, int chunkSize)
        : this(new FileStream(file.FullName, FileMode.Open, FileAccess.Read), chunkSize)
    {
    }

    /**
     * Creates a new instance that fetches data from the specified file.
     */
    public ChunkedFile(FileStream file) : this(file, ChunkedStream.DEFAULT_CHUNK_SIZE)
    {
    }

    /**
     * Creates a new instance that fetches data from the specified file.
     *
     * @param chunkSize the number of bytes to fetch on each
     *                  {@link #readChunk(ChannelHandlerContext)} call
     */
    public ChunkedFile(FileStream file, int chunkSize) : this(file, 0, file.Length, chunkSize)
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
    public ChunkedFile(FileStream file, long offset, long length, int chunkSize)
    {
        ObjectUtil.CheckNotNull(file, "file");
        ObjectUtil.CheckPositiveOrZero(offset, "offset");
        ObjectUtil.CheckPositiveOrZero(length, "length");
        ObjectUtil.CheckPositive(chunkSize, "chunkSize");

        this.file = file;
        this.offset = startOffset = offset;
        this.endOffset = offset + length;
        this.chunkSize = chunkSize;

        file.Seek(offset, SeekOrigin.Begin);
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
        return !(offset < endOffset && file.CanRead);
    }

    public virtual void Close()
    {
        file.Close();
    }

    public virtual ByteBuf ReadChunk(IByteBufAllocator allocator)
    {
        long offset = this.offset;
        if (offset >= endOffset)
        {
            return null;
        }

        int chunkSize = (int) Math.Min(this.chunkSize, endOffset - offset);
        // Check if the buffer is backed by an byte array. If so we can optimize it a bit an safe a copy

        ByteBuf buf = allocator.HeapBuffer(chunkSize);
        bool release = true;
        try
        {
            // CLR: file.readFully(buf.array(), buf.arrayOffset(), chunkSize) reads into the heap buffer's writable
            // memory (ByteBuf.AsSpan; Memory borrowing replaces Java hasArray/array/arrayOffset,
            // docs/buffer-porting.md). readFully "reads repeatedly from the file until the requested number of bytes
            // are read" and throws EOFException "if this file reaches the end before reading all the bytes"
            // (RandomAccessFile docs above); Stream.ReadExactly throws EndOfStreamException, an IOException, when "The
            // end of the stream is reached before filling the buffer"
            // (https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.readexactly?view=net-10.0,
            // https://learn.microsoft.com/en-us/dotnet/api/system.io.endofstreamexception?view=net-10.0).
            file.ReadExactly(buf.AsSpan(0, chunkSize));
            buf.WriterIndex = chunkSize;
            this.offset = offset + chunkSize;
            release = false;
            return buf;
        }
        finally
        {
            if (release)
            {
                buf.Release();
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
