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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufFileTransferTest
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static ByteBuf Owner(int kind)
    {
        if (kind == 0) return Unpooled.Buffer(8, 128).SetZero(0, 8);
        if (kind == 1) return Unpooled.DirectBuffer(8, 128).SetZero(0, 8);
        return Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[3]))
            .AddComponent(Unpooled.EmptyBuffer).AddComponent(Unpooled.DirectBuffer(5, 5).WriteZero(5));
    }
    private static byte[] Bytes(ByteBuf b, int index, int length) => b.AsReadOnlySequence(index, length).ToArray();
    private static ValueTask<int> Get(ByteBuf b, bool async, int index, SafeFileHandle h, long position, int length,
        CancellationToken token) => async ? b.GetBytesAsync(index, h, position, length, token)
        : ValueTask.FromResult(b.GetBytes(index, h, position, length));
    private static ValueTask<int> Set(ByteBuf b, bool async, int index, SafeFileHandle h, long position, int length,
        CancellationToken token) => async ? b.SetBytesAsync(index, h, position, length, token)
        : ValueTask.FromResult(b.SetBytes(index, h, position, length));
    private static ValueTask<int> Read(ByteBuf b, bool async, SafeFileHandle h, long position, int length,
        CancellationToken token) => async ? b.ReadBytesAsync(h, position, length, token)
        : ValueTask.FromResult(b.ReadBytes(h, position, length));
    private static ValueTask<int> Write(ByteBuf b, bool async, SafeFileHandle h, long position, int length,
        CancellationToken token) => async ? b.WriteBytesAsync(h, position, length, token)
        : ValueTask.FromResult(b.WriteBytes(h, position, length));

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public async Task OriginalReadAndWriteWithFileChannelKeepFilePosition(int kind, bool async)
    {
        using var file = new TemporaryFile(); file.Stream.Position = 7;
        // channelPosition should never be changed
        long channelPosition = file.Stream.Position;
        ByteBuf source = Owner(kind).WriteBytes(new byte[] { 97, 98, 99, 100 });
        ByteBuf target = Owner(kind);
        try
        {
            Assert.Equal(4, await Read(source, async, file.Handle, 10, 4, Token));
            Assert.Equal(4, source.ReaderIndex); Assert.Equal(4, source.WriterIndex);
            Assert.Equal(channelPosition, file.Stream.Position);
            Assert.Equal(4, await Write(target, async, file.Handle, 10, 4, Token));
            Assert.Equal(4, target.WriterIndex); Assert.Equal(0, target.ReaderIndex);
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, Bytes(target, 0, 4));
            Assert.Equal(channelPosition, file.Stream.Position);
            Assert.Equal(0, file.Stream.ReadByte()); // The next sequential read still starts at offset 7.
            Assert.False(file.Handle.IsClosed); Assert.Equal(1, target.ReferenceCount);
        }
        finally { source.Release(); target.Release(); }
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public async Task OriginalGetAndSetWithFileChannelKeepAllIndices(int kind, bool async)
    {
        using var file = new TemporaryFile(); file.Stream.Position = 7;
        // channelPosition should never be changed
        long channelPosition = file.Stream.Position;
        ByteBuf source = Owner(kind).WriteBytes(new byte[] { 97, 98, 99, 100 }).SetIndex(1, 4);
        ByteBuf target = Owner(kind).SetIndex(1, 2);
        try
        {
            Assert.Equal(4, await Get(source, async, 0, file.Handle, 10, 4, Token));
            Assert.Equal(4, await Set(target, async, 2, file.Handle, 10, 4, Token));
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, Bytes(target, 2, 4));
            Assert.Equal(1, source.ReaderIndex); Assert.Equal(4, source.WriterIndex);
            Assert.Equal(1, target.ReaderIndex); Assert.Equal(2, target.WriterIndex);
            Assert.Equal(channelPosition, file.Stream.Position);
            Assert.Equal(new byte[] { 97, 98, 99, 100 }, file.Bytes()[10..]);
        }
        finally { source.Release(); target.Release(); }
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public async Task ShortFileReadAndEofPreserveUnwrittenTailAndReserveRequestedCapacity(int kind, bool async)
    {
        using var file = new TemporaryFile(); RandomAccess.Write(file.Handle, new byte[] { 1, 2, 3 }, 0);
        ByteBuf b = Owner(kind); b.WriterIndex = 2;
        try
        {
            Assert.Equal(2, await Write(b, async, file.Handle, 1, 30, Token));
            Assert.True(b.Capacity >= 32); Assert.Equal(4, b.WriterIndex);
            Assert.Equal(new byte[] { 0, 0, 2, 3, 0, 0, 0, 0 }, Bytes(b, 0, 8));
            Assert.Equal(0, await Write(b, async, file.Handle, 3, 4, Token));
            Assert.Equal(0, await Set(b, async, 0, file.Handle, (long)int.MaxValue + 123, 4, Token));
            Assert.Equal(4, b.WriterIndex); Assert.Equal(0, b.ReaderIndex); Assert.Equal(0, file.Stream.Position);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task InvalidRangesAndEmptyOperationsDoNotModifyFileOrCapacity(bool async)
    {
        using var file = new TemporaryFile(); RandomAccess.Write(file.Handle, new byte[] { 9 }, 0);
        ByteBuf b = Owner(0);
        try
        {
            Assert.Equal(0, await Get(b, async, 8, file.Handle, long.MaxValue, 0, Token));
            Assert.Equal(0, await Set(b, async, 8, file.Handle, long.MaxValue, 0, Token));
            Assert.Equal(0, await Read(b, async, file.Handle, 5, 0, Token));
            Assert.Equal(0, await Write(b, async, file.Handle, 5, 0, Token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Get(b, async, 9, file.Handle, 0, 0, Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Set(b, async, 1, file.Handle, 0, int.MaxValue, Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Get(b, async, 0, file.Handle, -1, 0, Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Write(b, async, file.Handle, long.MaxValue, 1, Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Write(b, async, file.Handle, 0, -1, Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Write(b, async, file.Handle, 0, 129, Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Read(b, async, file.Handle, 0, 1, Token).AsTask());
            await Assert.ThrowsAsync<ArgumentNullException>(() => Write(b, async, null, 0, 0, Token).AsTask());
            Assert.Equal(8, b.Capacity); Assert.Equal(new byte[] { 9 }, file.Bytes());
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ClosedAndInvalidHandlesThrowIncludingAtZeroWithoutPublishingIndices(bool async)
    {
        using var file = new TemporaryFile(); SafeFileHandle closed = file.Handle; file.Stream.Dispose();
        using var invalid = new SafeFileHandle(IntPtr.Zero, false);
        ByteBuf b = Owner(1).WriteByte(9);
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => Get(b, async, 0, closed, 0, 0, Token).AsTask());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => Set(b, async, 0, closed, 0, 0, Token).AsTask());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => Read(b, async, closed, 0, 1, Token).AsTask());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => Write(b, async, closed, 0, 30, Token).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() => Get(b, async, 0, invalid, 0, 0, Token).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() => Set(b, async, 0, invalid, 0, 0, Token).AsTask());
            Assert.Equal(0, b.ReaderIndex); Assert.Equal(1, b.WriterIndex); Assert.Equal(8, b.Capacity);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task ReadonlyAndFixedCompositeSupportOutputAndRejectInput(bool fixedComposite, bool async)
    {
        ByteBuf b = fixedComposite ? Unpooled.WrappedUnmodifiableBuffer(
            Unpooled.WrappedBuffer(new byte[] { 1, 2 }),
            Unpooled.DirectBuffer(2, 2).WriteBytes(new byte[] { 3, 4 }))
            : Owner(1).WriteBytes(new byte[] { 1, 2, 3, 4 }).AsReadOnly();
        using var file = new TemporaryFile();
        try
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => Set(b, async, 0, file.Handle, 0, 0, Token).AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(() => Write(b, async, file.Handle, 0, 0, Token).AsTask());
            Assert.Equal(4, await Read(b, async, file.Handle, 2, 4, Token));
            Assert.Equal(new byte[] { 0, 0, 1, 2, 3, 4 }, file.Bytes()); Assert.Equal(4, b.ReaderIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReleasedAccessAndEmptySingletonFollowBufferLifetimeRules(bool async)
    {
        using var file = new TemporaryFile(); ByteBuf b = Owner(1); b.Release();
        await Assert.ThrowsAsync<IllegalReferenceCountException>(() => Get(b, async, 0, file.Handle, 0, 0, Token).AsTask());
        await Assert.ThrowsAsync<IllegalReferenceCountException>(() => Set(b, async, 0, file.Handle, 0, 0, Token).AsTask());
        await Assert.ThrowsAsync<IllegalReferenceCountException>(() => Read(b, async, file.Handle, 0, 0, Token).AsTask());
        await Assert.ThrowsAsync<IllegalReferenceCountException>(() => Write(b, async, file.Handle, 0, 0, Token).AsTask());
        b = Unpooled.EmptyBuffer;
        Assert.Equal(0, await Get(b, async, 0, file.Handle, 0, 0, Token));
        Assert.Equal(0, await Set(b, async, 0, file.Handle, 0, 0, Token));
        Assert.Equal(0, await Read(b, async, file.Handle, 0, 0, Token));
        Assert.Equal(0, await Write(b, async, file.Handle, 0, 0, Token));
        Assert.Equal(0, RandomAccess.GetLength(file.Handle));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task PreCanceledFileOperationsPreventGrowthAndIoAfterValidation(int kind)
    {
        using var file = new TemporaryFile(); ByteBuf b = Owner(kind).WriteByte(9);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.GetBytesAsync(0, file.Handle, 0, 1, cts.Token).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.SetBytesAsync(0, file.Handle, 0, 1, cts.Token).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.ReadBytesAsync(file.Handle, 0, 1, cts.Token).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.WriteBytesAsync(file.Handle, 0, 30, cts.Token).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.WriteBytesAsync(file.Handle, 0, 0, cts.Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.WriteBytesAsync(file.Handle, -1, 1, cts.Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.WriteBytesAsync(file.Handle, 0, b.MaxWritableBytes + 1, cts.Token).AsTask());
            Assert.Equal(8, b.Capacity); Assert.Equal(0, b.ReaderIndex); Assert.Equal(1, b.WriterIndex);
            Assert.Equal(new byte[] { 9 }, Bytes(b, 0, 1)); Assert.Equal(0, RandomAccess.GetLength(file.Handle));
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ViewsUseCapturedOffsetsAndUnreleasableIndicesAreShared(bool async)
    {
        using var file = new TemporaryFile(); RandomAccess.Write(file.Handle, new byte[] { 1, 2, 3, 4 }, 0);
        ByteBuf b = Owner(1); ByteBuf slice = b.Slice(2, 6).Clear();
        try
        {
            Assert.Equal(4, await Write(slice, async, file.Handle, 0, 4, Token));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, Bytes(b, 2, 4)); Assert.Equal(0, b.WriterIndex);
            ByteBuf wrapper = Unpooled.UnreleasableBuffer(b);
            Assert.Equal(4, await Write(wrapper, async, file.Handle, 0, 4, Token));
            Assert.Equal(4, b.WriterIndex); Assert.Equal(4, wrapper.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CompositeCommitFailureCanChangePrefixWithoutPublishingWriter(bool async)
    {
        using var file = new TemporaryFile(); RandomAccess.Write(file.Handle, new byte[] { 1, 2, 3, 4 }, 0);
        ByteBuf b = Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[2]))
            .AddComponent(Unpooled.WrappedBuffer(new byte[2]).AsReadOnly());
        try
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => Write(b, async, file.Handle, 0, 4, Token).AsTask());
            Assert.Equal(new byte[] { 1, 2, 0, 0 }, Bytes(b, 0, 4)); Assert.Equal(0, b.WriterIndex);
            Assert.Equal(0, file.Stream.Position);
        }
        finally { b.Release(); }
    }

    [Fact]
    public async Task IndependentViewsCanWriteDisjointFileOffsetsConcurrently()
    {
        using var file = new TemporaryFile(); file.Stream.Position = 7;
        ByteBuf b = Owner(1).WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        try
        {
            Task[] writes = Enumerable.Range(0, 128).Select(async i =>
            {
                ByteBuf view = b.Duplicate();
                Assert.Equal(8, await view.ReadBytesAsync(file.Handle, i * 8L, 8, Token));
                Assert.Equal(8, view.ReaderIndex);
            }).ToArray();
            await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.Equal(7, file.Stream.Position); Assert.Equal(0, b.ReaderIndex);
            Assert.Equal(Enumerable.Range(0, 1024).Select(i => (byte)(i % 8 + 1)).ToArray(), file.Bytes());
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FileAccessErrorsLeaveIndicesAndStagedInputUnchanged(bool async)
    {
        using var file = new TemporaryFile(); RandomAccess.Write(file.Handle, new byte[] { 9 }, 0);
        using var readOnly = File.OpenHandle(file.Stream.Name, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
        using var writeOnly = File.OpenHandle(file.Stream.Name, FileMode.Open, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
        ByteBuf b = Owner(1).WriteBytes(new byte[] { 1, 2 });
        try
        {
            Exception outputFailure = await Record.ExceptionAsync(() => Read(b, async, readOnly, 0, 2, Token).AsTask());
            Assert.True(outputFailure is IOException or UnauthorizedAccessException);
            Assert.Equal(0, b.ReaderIndex); Assert.Equal(new byte[] { 9 }, file.Bytes());
            Exception inputFailure = await Record.ExceptionAsync(() => Write(b, async, writeOnly, 0, 30, Token).AsTask());
            Assert.True(inputFailure is IOException or UnauthorizedAccessException);
            Assert.Equal(2, b.WriterIndex); Assert.True(b.Capacity >= 32);
            Assert.Equal(new byte[] { 1, 2, 0, 0, 0, 0, 0, 0 }, Bytes(b, 0, 8));
            Assert.False(readOnly.IsClosed); Assert.False(writeOnly.IsClosed); Assert.Equal(0, file.Stream.Position);
        }
        finally { b.Release(); }
    }

    private sealed class TemporaryFile : IDisposable
    {
        public readonly FileStream Stream = new(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".netty-test"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite, 1,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        public SafeFileHandle Handle => Stream.SafeFileHandle;
        public byte[] Bytes()
        {
            byte[] bytes = new byte[checked((int)RandomAccess.GetLength(Handle))];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = RandomAccess.Read(Handle, bytes.AsSpan(offset), offset);
                Assert.True(count > 0); offset += count;
            }
            return bytes;
        }
        public void Dispose() => Stream.Dispose();
    }
}
