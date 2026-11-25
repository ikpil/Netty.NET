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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Abstract Memory HttpData implementation
 */
// CLR: java.io.File is FileInfo and RandomAccessFile with its FileChannel is one FileStream (docs/handler-porting.md,
// java.io and NIO), opened with AbstractDiskHttpData.OpenFile (the JDK sharing mode, no FileStream buffer); File.length()
// and createNewFile() are AbstractDiskHttpData.FileLength/CreateNewFile, which keep the
// Java results (0 for a missing file, false for an existing one). InputStream is Stream: InputStream.read(byte[]) is
// Read(bytes, 0, bytes.Length), whose 0 at the end of the stream ends the loop where Java's -1 does
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.read?view=net-10.0). The NIO ByteBuffer of
// setContent(File) is the byte array it wraps, and wrappedBuffer(Integer.MAX_VALUE, byteBuffer) wraps that array.
// EMPTY_BUFFER is Unpooled.EmptyBuffer, whose array() is EmptyArrays.EMPTY_BYTES (the Buffer port has no array(),
// docs/buffer-porting.md, Array extraction). CompositeByteBuf.addComponent(true, buffer) and addComponents(true, ...) are
// AddComponent(buffer, true) and AddComponents(buffers, true) (Buffer port parameter order). IOException is
// System.IO.IOException and IllegalArgumentException is ArgumentException.
public abstract class AbstractMemoryHttpData : AbstractHttpData
{

    private ByteBuf byteBuf;
    private int chunkPosition;

    protected AbstractMemoryHttpData(string name, Encoding charset, long size) : base(name, charset, size)
    {
        byteBuf = Unpooled.EmptyBuffer;
    }

    public override void SetContent(ByteBuf buffer)
    {
        ObjectUtil.CheckNotNull(buffer, "buffer");
        long localsize = buffer.ReadableBytes;
        try
        {
            CheckSize(localsize);
        }
        catch (IOException)
        {
            buffer.Release();
            throw;
        }
        if (definedSize > 0 && definedSize < localsize)
        {
            buffer.Release();
            throw new IOException("Out of size: " + localsize + " > " +
                    definedSize);
        }
        if (byteBuf != null)
        {
            byteBuf.Release();
        }
        byteBuf = buffer;
        size = localsize;
        SetCompleted();
    }

    public override void SetContent(System.IO.Stream inputStream)
    {
        ObjectUtil.CheckNotNull(inputStream, "inputStream");

        byte[] bytes = new byte[4096 * 4];
        ByteBuf buffer = Unpooled.Buffer();
        int written = 0;
        try
        {
            int read = inputStream.Read(bytes, 0, bytes.Length);
            while (read > 0)
            {
                buffer.WriteBytes(new ReadOnlySpan<byte>(bytes, 0, read));
                written += read;
                CheckSize(written);
                read = inputStream.Read(bytes, 0, bytes.Length);
            }
        }
        catch (IOException)
        {
            buffer.Release();
            throw;
        }
        size = written;
        if (definedSize > 0 && definedSize < size)
        {
            buffer.Release();
            throw new IOException("Out of size: " + size + " > " + definedSize);
        }
        if (byteBuf != null)
        {
            byteBuf.Release();
        }
        byteBuf = buffer;
        SetCompleted();
    }

    public override void AddContent(ByteBuf buffer, bool last)
    {
        if (buffer != null)
        {
            long localsize = buffer.ReadableBytes;
            try
            {
                CheckSize(size + localsize);
            }
            catch (IOException)
            {
                buffer.Release();
                throw;
            }
            if (definedSize > 0 && definedSize < size + localsize)
            {
                buffer.Release();
                throw new IOException("Out of size: " + (size + localsize) +
                        " > " + definedSize);
            }
            size += localsize;
            if (byteBuf == null)
            {
                byteBuf = buffer;
            }
            else if (localsize == 0)
            {
                // Nothing to add and byteBuf already exists
                buffer.Release();
            }
            else if (byteBuf.ReadableBytes == 0)
            {
                // Previous buffer is empty, so just replace it
                byteBuf.Release();
                byteBuf = buffer;
            }
            else if (byteBuf is CompositeByteBuf)
            {
                CompositeByteBuf cbb = (CompositeByteBuf) byteBuf;
                cbb.AddComponent(buffer, true);
            }
            else
            {
                CompositeByteBuf cbb = Unpooled.CompositeBuffer(int.MaxValue);
                cbb.AddComponents(new ByteBuf[] { byteBuf, buffer }, true);
                byteBuf = cbb;
            }
        }
        if (last)
        {
            SetCompleted();
        }
        else
        {
            ObjectUtil.CheckNotNull(buffer, "buffer");
        }
    }

    public override void SetContent(FileInfo file)
    {
        ObjectUtil.CheckNotNull(file, "file");

        long newsize = AbstractDiskHttpData.FileLength(file);
        if (newsize > int.MaxValue)
        {
            throw new ArgumentException("File too big to be loaded in memory");
        }
        CheckSize(newsize);
        FileStream accessFile = AbstractDiskHttpData.OpenFile(file, FileMode.Open, FileAccess.Read);
        byte[] byteBuffer;
        try
        {
            byte[] array = new byte[(int) newsize];
            byteBuffer = array;
            int read = 0;
            while (read < newsize)
            {
                read += accessFile.Read(array, read, (int) newsize - read);
            }
        }
        finally
        {
            accessFile.Dispose();
        }
        if (byteBuf != null)
        {
            byteBuf.Release();
        }
        byteBuf = Unpooled.WrappedBuffer(int.MaxValue, byteBuffer);
        size = newsize;
        SetCompleted();
    }

    public override void Delete()
    {
        if (byteBuf != null)
        {
            byteBuf.Release();
            byteBuf = null;
        }
    }

    public override byte[] Get()
    {
        if (byteBuf == null)
        {
            return EmptyArrays.EMPTY_BYTES;
        }
        byte[] array = new byte[byteBuf.ReadableBytes];
        byteBuf.GetBytes(byteBuf.ReaderIndex, array);
        return array;
    }

    public override string GetString()
    {
        return GetString(HttpConstants.DEFAULT_CHARSET);
    }

    public override string GetString(Encoding encoding)
    {
        if (byteBuf == null)
        {
            return "";
        }
        if (encoding == null)
        {
            encoding = HttpConstants.DEFAULT_CHARSET;
        }
        return byteBuf.ToString(encoding);
    }

    /**
     * Utility to go from a In Memory FileUpload
     * to a Disk (or another implementation) FileUpload
     * @return the attached ByteBuf containing the actual bytes
     */
    public override ByteBuf GetByteBuf()
    {
        return byteBuf;
    }

    public override ByteBuf GetChunk(int length)
    {
        if (byteBuf == null || length == 0 || byteBuf.ReadableBytes == 0)
        {
            chunkPosition = 0;
            return Unpooled.EmptyBuffer;
        }
        int sizeLeft = byteBuf.ReadableBytes - chunkPosition;
        if (sizeLeft == 0)
        {
            chunkPosition = 0;
            return Unpooled.EmptyBuffer;
        }
        int sliceLength = length;
        if (sizeLeft < length)
        {
            sliceLength = sizeLeft;
        }
        ByteBuf chunk = byteBuf.RetainedSlice(chunkPosition, sliceLength);
        chunkPosition += sliceLength;
        return chunk;
    }

    public override bool IsInMemory()
    {
        return true;
    }

    // CLR: new RandomAccessFile(dest, "rw") opens the file for reading and writing and creates it if it does not exist
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/RandomAccessFile.html), which is
    // FileMode.OpenOrCreate with FileAccess.ReadWrite (https://learn.microsoft.com/en-us/dotnet/api/system.io.filemode?view=net-10.0);
    // neither truncates. The nioBuffer()/nioBuffers() writes are one ByteBuf.GetBytes(int, Stream, int), which writes the
    // whole readable range (the Buffer port has no NIO buffers; the stream loop of Java only repeats partial channel
    // writes), and fileChannel.force(false), which writes the content to the storage device
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/channels/FileChannel.html), is
    // Flush(true), which "clears all intermediate file buffers"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0).
    public override bool RenameTo(FileInfo dest)
    {
        ObjectUtil.CheckNotNull(dest, "dest");
        if (byteBuf == null)
        {
            // empty file
            if (!AbstractDiskHttpData.CreateNewFile(dest))
            {
                throw new IOException("file exists already: " + dest);
            }
            return true;
        }
        int length = byteBuf.ReadableBytes;
        long written = 0;
        FileStream accessFile = AbstractDiskHttpData.OpenFile(dest, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        try
        {
            while (written < length)
            {
                byteBuf.GetBytes(byteBuf.ReaderIndex, accessFile, length);
                written += length;
            }
            accessFile.Flush(true);
        }
        finally
        {
            accessFile.Dispose();
        }
        return written == length;
    }

    public override FileInfo GetFile()
    {
        throw new IOException("Not represented by a file");
    }

    public override IHttpData Touch()
    {
        return Touch(null);
    }

    public override IHttpData Touch(object hint)
    {
        if (byteBuf != null)
        {
            byteBuf.Touch(hint);
        }
        return this;
    }
}
