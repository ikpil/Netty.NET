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
using System.Buffers;
using System.IO;
using Netty.NET.Common;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

/**
 * Default {@link FileRegion} implementation which transfer data from a {@link FileChannel} or {@link File}.
 *
 * Be aware that the {@link FileChannel} will be automatically closed once {@link #refCnt()} returns
 * {@code 0}.
 */
public class DefaultFileRegion : AbstractReferenceCounted, IFileRegion
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(DefaultFileRegion));
    private readonly FileInfo _path;
    private readonly long _position;
    private readonly long _count;
    private long _transferred;
    private FileStream _file;
    /**
     * Create a new instance
     *
     * @param fileChannel      the {@link FileChannel} which should be transferred
     * @param position         the position from which the transfer should start
     * @param count            the number of bytes to transfer
     */
    public DefaultFileRegion(FileStream fileChannel, long position, long count)
    {
        ArgumentNullException.ThrowIfNull(fileChannel);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _file = fileChannel;
        _position = position;
        _count = count;
    }
    /**
     * Create a new instance using the given {@link File}. The {@link File} will be opened lazily or
     * explicitly via {@link #open()}.
     *
     * @param file         the {@link File} which should be transferred
     * @param position     the position from which the transfer should start
     * @param count        the number of bytes to transfer
     */
    public DefaultFileRegion(FileInfo file, long position, long count)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _path = file;
        _position = position;
        _count = count;
    }
    /**
     * Returns {@code true} if the {@link FileRegion} has a open file-descriptor
     */
    public bool IsOpen() => _file != null;
    /**
     * Explicitly open the underlying file-descriptor if not done yet.
     */
    public void Open()
    {
        if (!IsOpen() && ReferenceCount > 0)
        {
            // Only open if this DefaultFileRegion was not released yet.
            _file = new FileStream(_path.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
    }
    public long Position() => _position;
    public long Count() => _count;
    public long Transferred() => _transferred;
    public long TransferTo(Stream target, long position)
    {
        if (position < 0 || position > _count)
            throw new ArgumentOutOfRangeException(nameof(position));
        long count = _count - position;
        if (count == 0)
            return 0;
        if (ReferenceCount == 0)
            throw new IllegalReferenceCountException(0);
        // Call open to make sure fc is initialized. This is a no-oop if we called it before.
        Open();
        ArgumentNullException.ThrowIfNull(target);
        if (position > long.MaxValue - _position)
            throw new IOException("File region offset exceeds Int64 range.");
        long offset = _position + position;
        long written = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            // RandomAccess preserves the supplied FileStream's position, like positional FileChannel.transferTo.
            // Stream writes consume the supplied span in full. Count only writes that returned successfully.
            while (written < count)
            {
                int length = (int)Math.Min(count - written, buffer.Length);
                if (written > long.MaxValue - offset)
                    throw new IOException("File region offset exceeds Int64 range.");
                int read = RandomAccess.Read(_file.SafeFileHandle, buffer.AsSpan(0, length), offset + written);
                if (read == 0)
                    break;
                target.Write(buffer.AsSpan(0, read));
                written += read;
                _transferred = unchecked(_transferred + read);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        if (written == 0)
        {
            // If the amount of written data is 0 we need to check if the requested count is bigger then the
            // actual file itself as it may have been truncated on disk.
            //
            // See https://github.com/netty/netty/issues/8868
            Validate(this, position);
        }
        return written;
    }
    protected override void Deallocate()
    {
        FileStream file = _file;
        if (file == null)
            return;
        _file = null;
        try
        { file.Dispose(); }
        catch (IOException error) { Logger.Warn("Failed to close a file.", error); }
    }
    public new IFileRegion Retain() { base.Retain(); return this; }
    public override IFileRegion Retain(int increment) { base.Retain(increment); return this; }
    public new IFileRegion Touch() => this;
    public override IFileRegion Touch(object hint) => this;
    internal static void Validate(DefaultFileRegion region, long position)
    {
        // If the amount of written data is 0 we need to check if the requested count is bigger then the
        // actual file itself as it may have been truncated on disk.
        //
        // See https://github.com/netty/netty/issues/8868
        long size = RandomAccess.GetLength(region._file.SafeFileHandle);
        // The requested end is independent of the relative transfer position. Compare without overflowing Int64.
        if (region._position > size || region._count > size - region._position)
            throw new IOException($"Underlying file size {size} smaller then requested count {region._count}");
    }
}
