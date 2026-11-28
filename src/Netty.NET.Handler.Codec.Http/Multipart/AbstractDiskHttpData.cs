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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Abstract Disk HttpData implementation
 */
// CLR: java.io.File is FileInfo, and RandomAccessFile with its FileChannel is one FileStream (docs/handler-porting.md,
// java.io and NIO). File.getPath() is FileInfo.FullName (the temporary files have absolute paths), and the
// java.io.File operations whose results the code tests are the CLR-only helpers below: FileLength (File.length(), 0 for
// a missing file), FileExists, CreateNewFile, DeleteFile and RenameFile (the boolean results of exists(),
// createNewFile(), delete() and renameTo(File)), and OpenFile (the RandomAccessFile modes). PlatformDependent.createTempFile
// is the Common PlatformDependent.CreateTempFile(string, string, DirectoryInfo) (docs/common-clr-design.md, Native
// temporary-file creation). fileChannel.force(false) is FileStream.Flush(true), which "clears all intermediate file
// buffers" (https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0), as the content of
// the file is "written to the storage device" in Java
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/channels/FileChannel.html). A
// FileChannel.read(ByteBuffer) of -1 at the end of the file is a Stream.Read of 0
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.read?view=net-10.0), and the NIO ByteBuffers are byte
// arrays. ByteBuf.getBytes(index, FileChannel, position, length) is ByteBuf.GetBytes(int, Stream, int), which writes
// the whole range at the stream position, the channel position of the Java loop (the SafeFileHandle overload of the
// Buffer port is not used, because reading FileStream.SafeFileHandle "automatically flushes the stream and sets the
// current stream position to 0",
// https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.safefilehandle?view=net-10.0). Integer.toString of
// String.hashCode() keeps Java's s[0]*31^(n-1) + ... + s[n-1] value
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html), because string.GetHashCode()
// "is not guaranteed to be stable" (https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0).
// getBaseDirectory() and deleteOnExit() are protected internal, because DefaultHttpDataFactoryTest calls them from the
// package (Java protected admits the package; docs/codec-base-porting.md, Package access). IOException is
// System.IO.IOException and IllegalArgumentException is ArgumentException.
public abstract class AbstractDiskHttpData : AbstractHttpData
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(AbstractDiskHttpData));

    private FileInfo file;
    private bool isRenamed;
    private FileStream fileChannel;

    protected AbstractDiskHttpData(string name, Encoding charset, long size) : base(name, charset, size)
    {
    }

    /**
     *
     * @return the real DiskFilename (basename)
     */
    protected abstract string GetDiskFilename();

    /**
     *
     * @return the default prefix
     */
    protected abstract string GetPrefix();

    /**
     *
     * @return the default base Directory
     */
    protected internal abstract string GetBaseDirectory();

    /**
     *
     * @return the default postfix
     */
    protected abstract string GetPostfix();

    /**
     *
     * @return True if the file should be deleted on Exit by default
     */
    protected internal abstract bool DeleteOnExit();

    /**
     * @return a new Temp File from getDiskFilename(), default prefix, postfix and baseDirectory
     */
    private FileInfo TempFile()
    {
        string newpostfix;
        string diskFilename = GetDiskFilename();
        if (diskFilename != null)
        {
            newpostfix = '_' + JavaHashCode(diskFilename).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            newpostfix = GetPostfix();
        }

        FileInfo tmpFile;

        if (GetBaseDirectory() == null)
        {
            // create a temporary file
            tmpFile = PlatformDependent.CreateTempFile(GetPrefix(), newpostfix, null);
        }
        else
        {
            tmpFile = PlatformDependent.CreateTempFile(GetPrefix(), newpostfix, new DirectoryInfo(
                    GetBaseDirectory()));
        }

        if (DeleteOnExit())
        {
            // See https://github.com/netty/netty/issues/10351
            DeleteFileOnExitHook.Add(tmpFile.FullName);
        }

        return tmpFile;
    }

    public override void SetContent(ByteBuf buffer)
    {
        ObjectUtil.CheckNotNull(buffer, "buffer");

        try
        {
            size = buffer.ReadableBytes;
            CheckSize(size);
            if (definedSize > 0 && definedSize < size)
            {
                throw new IOException("Out of size: " + size + " > " + definedSize);
            }

            if (file == null)
            {
                file = TempFile();
            }

            if (buffer.ReadableBytes == 0)
            {
                // empty file
                if (!CreateNewFile(file))
                {
                    if (FileLength(file) == 0)
                    {
                        return;
                    }
                    else
                    {
                        if (!DeleteFile(file) || !CreateNewFile(file))
                        {
                            throw new IOException("file exists already: " + file);
                        }
                    }
                }

                return;
            }

            FileStream accessFile = OpenFile(file, FileMode.OpenOrCreate, FileAccess.ReadWrite);

            try
            {
                accessFile.SetLength(0);
                FileStream localfileChannel = accessFile;
                int written = 0;

                while (written < size)
                {
                    buffer.GetBytes(buffer.ReaderIndex, localfileChannel, (int) size);
                    written += (int) size;
                }

                buffer.ReaderIndex = buffer.ReaderIndex + written;
                localfileChannel.Flush(true);
            }
            finally
            {
                accessFile.Dispose();
            }

            SetCompleted();
        }
        finally
        {
            // Release the buffer as it was retained before and we not need a reference to it at all
            // See https://github.com/netty/netty/issues/1516
            buffer.Release();
        }
    }

    // CLR: the Java loop repeats a partial channel write and stops on a negative count; ByteBuf.GetBytes(int, Stream,
    // int) writes the whole remaining range, so the loop runs once (see the class comment).
    public override void AddContent(ByteBuf buffer, bool last)
    {
        if (buffer != null)
        {
            try
            {
                int localsize = buffer.ReadableBytes;
                CheckSize(size + localsize);
                if (definedSize > 0 && definedSize < size + localsize)
                {
                    throw new IOException("Out of size: " + (size + localsize) +
                            " > " + definedSize);
                }

                if (file == null)
                {
                    file = TempFile();
                }

                if (fileChannel == null)
                {
                    fileChannel = OpenFile(file, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                }

                int remaining = localsize;
                long position = fileChannel.Position;
                int index = buffer.ReaderIndex;

                while (remaining > 0)
                {
                    buffer.GetBytes(index, fileChannel, remaining);
                    int written = remaining;
                    remaining -= written;
                    position += written;
                    index += written;
                }

                fileChannel.Position = position;
                buffer.ReaderIndex = index;
                size += localsize - remaining;
            }
            finally
            {
                // Release the buffer as it was retained before and we not need a reference to it at all
                // See https://github.com/netty/netty/issues/1516
                buffer.Release();
            }
        }

        if (last)
        {
            if (file == null)
            {
                file = TempFile();
            }

            if (fileChannel == null)
            {
                fileChannel = OpenFile(file, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            }

            try
            {
                fileChannel.Flush(true);
            }
            finally
            {
                fileChannel.Dispose();
            }

            fileChannel = null;
            SetCompleted();
        }
        else
        {
            ObjectUtil.CheckNotNull(buffer, "buffer");
        }
    }

    public override void SetContent(FileInfo file)
    {
        long size = FileLength(file);
        CheckSize(size);
        this.size = size;

        if (this.file != null)
        {
            Delete();
        }

        this.file = file;
        isRenamed = true;
        SetCompleted();
    }

    public override void SetContent(System.IO.Stream inputStream)
    {
        ObjectUtil.CheckNotNull(inputStream, "inputStream");

        if (file != null)
        {
            Delete();
        }

        file = TempFile();
        FileStream accessFile = OpenFile(file, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        long written = 0;

        try
        {
            accessFile.SetLength(0);
            FileStream localfileChannel = accessFile;
            byte[] bytes = new byte[4096 * 4];
            int read = inputStream.Read(bytes, 0, bytes.Length);

            while (read > 0)
            {
                localfileChannel.Write(bytes, 0, read);
                written += read;
                CheckSize(written);
                read = inputStream.Read(bytes, 0, bytes.Length);
            }

            localfileChannel.Flush(true);
        }
        finally
        {
            accessFile.Dispose();
        }

        size = written;
        if (definedSize > 0 && definedSize < size)
        {
            if (!DeleteFile(file))
            {
                logger.Warn("Failed to delete: {}", file);
            }

            file = null;
            throw new IOException("Out of size: " + size + " > " + definedSize);
        }

        isRenamed = true;
        SetCompleted();
    }

    public override void Delete()
    {
        if (fileChannel != null)
        {
            try
            {
                fileChannel.Flush(true);
            }
            catch (IOException e)
            {
                logger.Warn("Failed to force.", e);
            }
            finally
            {
                try
                {
                    fileChannel.Dispose();
                }
                catch (IOException e)
                {
                    logger.Warn("Failed to close a file.", e);
                }
            }

            fileChannel = null;
        }

        if (!isRenamed)
        {
            string filePath = null;

            if (file != null && FileExists(file))
            {
                filePath = file.FullName;

                if (!DeleteFile(file))
                {
                    filePath = null;
                    logger.Warn("Failed to delete: {}", file);
                }
            }

            // If you turn on deleteOnExit make sure it is executed.
            if (DeleteOnExit() && filePath != null)
            {
                DeleteFileOnExitHook.Remove(filePath);
            }

            file = null;
        }
    }

    public override byte[] Get()
    {
        if (file == null)
        {
            return EmptyArrays.EMPTY_BYTES;
        }

        return ReadFrom(file);
    }

    public override ByteBuf GetByteBuf()
    {
        if (file == null)
        {
            return Unpooled.EmptyBuffer;
        }

        byte[] array = ReadFrom(file);
        return Unpooled.WrappedBuffer(array);
    }

    // CLR: wrappedBuffer(byteBuffer) of the flipped ByteBuffer wraps its read bytes, which is WrappedBuffer(array, 0,
    // read).
    public override ByteBuf GetChunk(int length)
    {
        if (file == null || length == 0)
        {
            return Unpooled.EmptyBuffer;
        }

        if (fileChannel == null)
        {
            fileChannel = OpenFile(file, FileMode.Open, FileAccess.Read);
        }

        int read = 0;
        byte[] byteBuffer = new byte[length];

        try
        {
            while (read < length)
            {
                int readnow = fileChannel.Read(byteBuffer, read, length - read);
                if (readnow == 0)
                {
                    fileChannel.Dispose();
                    fileChannel = null;
                    break;
                }

                read += readnow;
            }
        }
        catch (IOException)
        {
            fileChannel.Dispose();
            fileChannel = null;
            throw;
        }

        if (read == 0)
        {
            return Unpooled.EmptyBuffer;
        }

        ByteBuf buffer = Unpooled.WrappedBuffer(byteBuffer, 0, read);
        buffer.ReaderIndex = 0;
        buffer.WriterIndex = read;
        return buffer;
    }

    public override string GetString()
    {
        return GetString(HttpConstants.DEFAULT_CHARSET);
    }

    public override string GetString(Encoding encoding)
    {
        if (file == null)
        {
            return "";
        }

        if (encoding == null)
        {
            byte[] array = ReadFrom(file);
            return HttpConstants.DEFAULT_CHARSET.GetString(array);
        }

        byte[] array2 = ReadFrom(file);
        return encoding.GetString(array2);
    }

    public override bool IsInMemory()
    {
        return false;
    }

    // CLR: File.renameTo(dest) is RenameFile, which returns false where File.Move throws (for example when dest exists,
    // as FileInfo.MoveTo "does not overwrite the destination file if it already exists",
    // https://learn.microsoft.com/en-us/dotnet/api/system.io.fileinfo.moveto?view=net-10.0); the copy then runs as in
    // Java. FileChannel.transferTo(position, count, target), which "does not modify this channel's position" and returns
    // "The number of bytes, possibly zero, that were actually transferred"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/channels/FileChannel.html), is the
    // private TransferTo helper. A close failure of RandomAccessFile.close() is a Dispose() IOException.
    public override bool RenameTo(FileInfo dest)
    {
        ObjectUtil.CheckNotNull(dest, "dest");
        if (file == null)
        {
            throw new IOException("No file defined so cannot be renamed");
        }

        if (!RenameFile(file, dest))
        {
            // must copy
            IOException exception = null;
            FileStream inputAccessFile = null;
            FileStream outputAccessFile = null;
            long chunkSize = 8196;
            long position = 0;

            try
            {
                inputAccessFile = OpenFile(file, FileMode.Open, FileAccess.Read);
                outputAccessFile = OpenFile(dest, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                FileStream @in = inputAccessFile;
                FileStream @out = outputAccessFile;

                while (position < size)
                {
                    if (chunkSize < size - position)
                    {
                        chunkSize = size - position;
                    }

                    position += TransferTo(@in, position, chunkSize, @out);
                }
            }
            catch (IOException e)
            {
                exception = e;
            }
            finally
            {
                if (inputAccessFile != null)
                {
                    try
                    {
                        inputAccessFile.Dispose();
                    }
                    catch (IOException e)
                    {
                        if (exception == null)
                        { // Choose to report the first exception
                            exception = e;
                        }
                        else
                        {
                            logger.Warn("Multiple exceptions detected, the following will be suppressed {}", e);
                        }
                    }
                }

                if (outputAccessFile != null)
                {
                    try
                    {
                        outputAccessFile.Dispose();
                    }
                    catch (IOException e)
                    {
                        if (exception == null)
                        { // Choose to report the first exception
                            exception = e;
                        }
                        else
                        {
                            logger.Warn("Multiple exceptions detected, the following will be suppressed {}", e);
                        }
                    }
                }
            }

            if (exception != null)
            {
                throw exception;
            }

            if (position == size)
            {
                if (!DeleteFile(file))
                {
                    logger.Warn("Failed to delete: {}", file);
                }

                file = dest;
                isRenamed = true;
                return true;
            }
            else
            {
                if (!DeleteFile(dest))
                {
                    logger.Warn("Failed to delete: {}", dest);
                }

                return false;
            }
        }

        file = dest;
        isRenamed = true;
        return true;
    }

    /**
     * Utility function
     *
     * @return the array of bytes
     */
    private static byte[] ReadFrom(FileInfo src)
    {
        long srcsize = FileLength(src);
        if (srcsize > int.MaxValue)
        {
            throw new ArgumentException(
                    "File too big to be loaded in memory");
        }

        FileStream accessFile = OpenFile(src, FileMode.Open, FileAccess.Read);
        byte[] array = new byte[(int) srcsize];

        try
        {
            FileStream fileChannel = accessFile;
            int read = 0;

            while (read < srcsize)
            {
                read += fileChannel.Read(array, read, (int) srcsize - read);
            }
        }
        finally
        {
            accessFile.Dispose();
        }

        return array;
    }

    public override FileInfo GetFile()
    {
        return file;
    }

    public override IHttpData Touch()
    {
        return this;
    }

    public override IHttpData Touch(object hint)
    {
        return this;
    }

    // CLR: FileChannel.transferTo(position, count, target): reads up to count bytes from position without moving this
    // stream's position and writes them at the target's position; 0 at the end of the file.
    private static long TransferTo(FileStream @in, long position, long count, FileStream target)
    {
        byte[] buffer = new byte[(int) Math.Min(count, 8192)];
        long saved = @in.Position;
        @in.Position = position;
        long transferred = 0;

        try
        {
            while (transferred < count)
            {
                int read = @in.Read(buffer, 0, (int) Math.Min(buffer.Length, count - transferred));
                if (read == 0)
                {
                    break;
                }

                target.Write(buffer, 0, read);
                transferred += read;
            }
        }
        finally
        {
            @in.Position = saved;
        }

        return transferred;
    }

    // CLR: String.hashCode() (see the class comment).
    private static int JavaHashCode(string s)
    {
        int h = 0;

        foreach (char c in s)
        {
            h = 31 * h + c;
        }

        return h;
    }

    // CLR: RandomAccessFile(file, "r") is FileMode.Open with FileAccess.Read and RandomAccessFile(file, "rw") opens for
    // reading and writing and "If the file does not already exist then an attempt will be made to create it"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/RandomAccessFile.html), which is
    // FileMode.OpenOrCreate with FileAccess.ReadWrite, without truncation
    // (https://learn.microsoft.com/en-us/dotnet/api/system.io.filemode?view=net-10.0). The JDK opens files on Windows
    // with FILE_SHARE_READ | FILE_SHARE_WRITE (OpenJDK src/java.base/windows/native/libjava/io_util_md.c,
    // https://github.com/openjdk/jdk17u/blob/master/src/java.base/windows/native/libjava/io_util_md.c), so a file that a
    // channel keeps open for writing can be read through a second channel; FileShare.ReadWrite "Allows subsequent opening
    // of the file for reading or writing" (https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0).
    // A FileChannel writes to the file without a user-space buffer, so another channel reads what was written; the
    // FileStream buffer is disabled ("0 or 1 means that buffering should be disabled",
    // https://learn.microsoft.com/en-us/dotnet/api/system.io.filestreamoptions.buffersize?view=net-10.0), so that
    // get()/getByteBuf() read the content that addContent wrote through the still open channel.
    internal static FileStream OpenFile(FileInfo file, FileMode mode, FileAccess access)
    {
        return new FileStream(file.FullName, new FileStreamOptions
        {
            Mode = mode, Access = access, Share = FileShare.ReadWrite, BufferSize = 0
        });
    }

    // CLR: File.length() is "The length, in bytes, of the file denoted by this abstract pathname, or 0L if the file does
    // not exist" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/File.html); FileInfo.Length throws
    // FileNotFoundException when "The file does not exist"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.io.fileinfo.length?view=net-10.0), and FileInfo caches the
    // state: "Call Refresh before attempting to get the attribute information, or the information will be outdated"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.io.filesysteminfo.refresh?view=net-10.0).
    internal static long FileLength(FileInfo file)
    {
        file.Refresh();
        return file.Exists ? file.Length : 0L;
    }

    // CLR: File.exists() (see FileLength for the Refresh).
    internal static bool FileExists(FileInfo file)
    {
        file.Refresh();
        return file.Exists;
    }

    // CLR: File.createNewFile() returns "true if the named file does not exist and was successfully created; false if
    // the named file already exists" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/File.html);
    // FileMode.CreateNew throws IOException "If the file already exists"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.io.filemode?view=net-10.0).
    internal static bool CreateNewFile(FileInfo file)
    {
        try
        {
            new FileStream(file.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite).Dispose();
            return true;
        }
        catch (IOException) when (FileExists(file))
        {
            return false;
        }
    }

    // CLR: File.delete() returns "true if and only if the file or directory is successfully deleted; false otherwise"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/File.html); FileInfo.Delete does nothing
    // for a missing file and throws IOException/UnauthorizedAccessException when it cannot delete it
    // (https://learn.microsoft.com/en-us/dotnet/api/system.io.fileinfo.delete?view=net-10.0).
    internal static bool DeleteFile(FileInfo file)
    {
        if (!FileExists(file))
        {
            return false;
        }

        try
        {
            file.Delete();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return !FileExists(file);
    }

    // CLR: File.renameTo(dest) returns "true if and only if the renaming succeeded; false otherwise"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/File.html). File.Move leaves the FileInfo of
    // the source unchanged, as a java.io.File is immutable (FileInfo.MoveTo would retarget the instance).
    internal static bool RenameFile(FileInfo file, FileInfo dest)
    {
        try
        {
            File.Move(file.FullName, dest.FullName);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
