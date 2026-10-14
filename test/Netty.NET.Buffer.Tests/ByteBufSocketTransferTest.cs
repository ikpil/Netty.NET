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
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufSocketTransferTest
{
    public static IEnumerable<object[]> Modes()
    {
        for (int kind = 0; kind < 6; kind++)
            foreach (bool async in new[] { false, true }) yield return [kind, async];
    }

    private static ValueTask<int> Get(ByteBuf b, bool async, int index, Socket s, int length, CancellationToken t)
        => async ? b.GetBytesAsync(index, s, length, t) : ValueTask.FromResult(b.GetBytes(index, s, length));
    private static ValueTask<int> Set(ByteBuf b, bool async, int index, Socket s, int length, CancellationToken t)
        => async ? b.SetBytesAsync(index, s, length, t) : ValueTask.FromResult(b.SetBytes(index, s, length));
    private static ValueTask<int> Read(ByteBuf b, bool async, Socket s, int length, CancellationToken t)
        => async ? b.ReadBytesAsync(s, length, t) : ValueTask.FromResult(b.ReadBytes(s, length));
    private static ValueTask<int> Write(ByteBuf b, bool async, Socket s, int length, CancellationToken t)
        => async ? b.WriteBytesAsync(s, length, t) : ValueTask.FromResult(b.WriteBytes(s, length));

    [Theory, MemberData(nameof(Modes))]
    public async Task AbsoluteAndRelativeOutputPreserveBytesMarksAndBorrowedOwnership(int kind, bool async)
    {
        using var scope = new Owner(kind);
        using var pair = new Pair();
        ByteBuf b = scope.Buffer;
        for (int i = 0; i < 24; i++) b.SetByte(i, 20 + i);
        b.SetIndex(2, 18).MarkReaderIndex().MarkWriterIndex();
        int sent = await Get(b, async, 7, pair.Local, 9, pair.Token);
        Assert.InRange(sent, 1, 9);
        Assert.Equal(ByteBufUtil.GetBytes(b, 7, sent), pair.Receive(sent));
        Assert.Equal(2, b.ReaderIndex); Assert.Equal(18, b.WriterIndex);
        sent = await Read(b, async, pair.Local, 12, pair.Token);
        Assert.InRange(sent, 1, 12);
        Assert.Equal(ByteBufUtil.GetBytes(b, 2, sent), pair.Receive(sent));
        Assert.Equal(2 + sent, b.ReaderIndex); Assert.Equal(18, b.WriterIndex);
        b.ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(2, b.ReaderIndex); Assert.Equal(18, b.WriterIndex);
        Assert.Equal(1, b.ReferenceCount);
        Assert.False(pair.Local.SafeHandle.IsClosed);
    }

    [Theory, MemberData(nameof(Modes))]
    public async Task ShortInputAndEofPublishOnlyActualBytes(int kind, bool async)
    {
        using var scope = new Owner(kind);
        using var pair = new Pair();
        ByteBuf b = scope.Buffer.SetZero(0, 24).SetIndex(2, 5).MarkReaderIndex().MarkWriterIndex();
        pair.SendAndFinish([91, 92, 93]);
        int total = 0;
        while (total < 3)
        {
            int count = await Set(b, async, 7 + total, pair.Local, 12 - total, pair.Token);
            Assert.InRange(count, 1, 3 - total);
            total += count;
            Assert.Equal(2, b.ReaderIndex); Assert.Equal(5, b.WriterIndex);
        }
        Assert.Equal(new byte[] { 91, 92, 93 }, ByteBufUtil.GetBytes(b, 7, 3));
        Assert.Equal(0, b.GetByte(10));
        Assert.Equal(0, await Set(b, async, 7, pair.Local, 12, pair.Token));
        Assert.Equal(0, await Write(b, async, pair.Local, 12, pair.Token));
        Assert.Equal(5, b.WriterIndex);

        using var second = new Pair();
        second.SendAndFinish([81, 82, 83]);
        total = 0;
        while (total < 3)
        {
            int count = await Write(b, async, second.Local, 12, second.Token);
            Assert.InRange(count, 1, 3 - total);
            total += count;
            Assert.Equal(5 + total, b.WriterIndex);
        }
        Assert.Equal(new byte[] { 81, 82, 83 }, ByteBufUtil.GetBytes(b, 5, 3));
        Assert.Equal(0, await Write(b, async, second.Local, 12, second.Token));
        b.ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(2, b.ReaderIndex); Assert.Equal(5, b.WriterIndex);
        Assert.Equal(1, b.ReferenceCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void NonblockingReceiveReturnsZeroWithoutChangingBytesOrIndices(int kind)
    {
        using var scope = new Owner(kind);
        using var pair = new Pair();
        ByteBuf b = scope.Buffer.SetZero(0, 24).SetIndex(2, 5);
        pair.Local.Blocking = false;
        Assert.Equal(0, b.SetBytes(7, pair.Local, 12));
        Assert.Equal(0, b.WriteBytes(pair.Local, 12));
        Assert.Equal(new byte[24], ByteBufUtil.GetBytes(b, 0, 24));
        Assert.Equal(2, b.ReaderIndex); Assert.Equal(5, b.WriterIndex);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NonblockingSendUsesActualCountAndStopsAtWouldBlock(bool direct)
    {
        const int length = 4 * 1024 * 1024;
        ByteBuf b = direct ? Unpooled.DirectBuffer(length, length) : Unpooled.Buffer(length, length);
        using var pair = new Pair();
        try
        {
            b.WriteZero(length);
            pair.Local.SendBufferSize = 4096; pair.Peer.ReceiveBufferSize = 4096;
            pair.Local.Blocking = false;
            bool sent = false, blocked = false;
            for (int i = 0; i < 64; i++)
            {
                int before = b.ReaderIndex;
                int count = b.ReadBytes(pair.Local, b.ReadableBytes);
                Assert.InRange(count, 0, length - before);
                Assert.Equal(before + count, b.ReaderIndex);
                if (count == 0) { blocked = true; break; }
                sent = true;
                // Socket buffer sizes are advisory. Windows may accept the whole
                // request; resubmit without draining the peer to reach WouldBlock.
                b.ReaderIndex = 0;
            }
            Assert.True(sent); Assert.True(blocked);
            Assert.Equal(length, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory, MemberData(nameof(Modes))]
    public async Task ValidationReadOnlyEmptyAndClosedSocketFailuresDoNotPublishIndices(int kind, bool async)
    {
        using var scope = new Owner(kind);
        using var pair = new Pair();
        ByteBuf b = scope.Buffer.SetZero(0, 24).SetIndex(2, 5);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await Get(b, async, 23, pair.Local, 2, pair.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await Read(b, async, pair.Local, 4, pair.Token));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await Set(b.AsReadOnly(), async, 0, pair.Local, 0, pair.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await Set(b, async, 0, null, 1, pair.Token));
        pair.Local.Dispose();
        Assert.Equal(0, await Get(b, async, 24, pair.Local, 0, pair.Token));
        Assert.Equal(0, await Set(b, async, 24, pair.Local, 0, pair.Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await Get(b, async, 0, pair.Local, 1, pair.Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await Write(b, async, pair.Local, 1, pair.Token));
        Assert.Equal(2, b.ReaderIndex); Assert.Equal(5, b.WriterIndex);
        Assert.Equal(new byte[24], ByteBufUtil.GetBytes(b, 0, 24));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(5)]
    public async Task GrowthIsRetainedAtEofButPreCancellationPreventsIt(int kind)
    {
        using var scope = new Owner(kind);
        using var pair = new Pair();
        ByteBuf b = scope.Buffer.SetIndex(0, 24);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await b.WriteBytesAsync(pair.Local, 8, canceled.Token));
        Assert.Equal(24, b.Capacity);
        pair.SendAndFinish([]);
        Assert.Equal(0, b.WriteBytes(pair.Local, 8));
        Assert.True(b.Capacity >= 32); Assert.Equal(24, b.WriterIndex);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task PendingReceiveCancellationDoesNotCommitStagingAndSocketRemainsUsable(int kind)
    {
        using var scope = new Owner(kind);
        using var pair = new Pair();
        ByteBuf b = scope.Buffer.SetZero(0, 24).SetIndex(2, 5);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(pair.Token);
        ValueTask<int> pending = b.WriteBytesAsync(pair.Local, 12, canceled.Token);
        Assert.False(pending.IsCompleted);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(2, b.ReaderIndex); Assert.Equal(5, b.WriterIndex);
        Assert.Equal(new byte[24], ByteBufUtil.GetBytes(b, 0, 24));
        pair.SendAndFinish([76]);
        Assert.Equal(1, await b.WriteBytesAsync(pair.Local, 12, pair.Token));
        Assert.Equal(76, b.GetByte(5)); Assert.Equal(6, b.WriterIndex);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task AdvancedWrappersRecordExactlyOnceForAllSocketAccessesIncludingFailures(bool composite, bool async)
    {
        using var scope = new Owner(composite ? 2 : 1);
        using var pair = new Pair();
        ByteBuf origin = scope.Buffer.SetZero(0, 24).SetIndex(2, 5);
        var tracker = new Tracker(origin);
        ByteBuf b = composite ? new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)origin, tracker)
            : new AdvancedLeakAwareByteBuf(origin, tracker);
        pair.SendAndFinish([]);
        Assert.Equal(0, await Get(b, async, 0, pair.Local, 0, pair.Token)); Assert.Equal(1, tracker.Records);
        Assert.Equal(0, await Set(b, async, 0, pair.Local, 1, pair.Token)); Assert.Equal(2, tracker.Records);
        Assert.Equal(0, await Read(b, async, pair.Local, 0, pair.Token)); Assert.Equal(3, tracker.Records);
        Assert.Equal(0, await Write(b, async, pair.Local, 1, pair.Token)); Assert.Equal(4, tracker.Records);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await Get(b, async, -1, pair.Local, 1, pair.Token));
        Assert.Equal(5, tracker.Records);
        int count = await Get(b, async, 7, pair.Local, 3, pair.Token);
        Assert.Equal(new byte[count], pair.Receive(count)); Assert.Equal(6, tracker.Records);
        count = await Read(b, async, pair.Local, 3, pair.Token);
        Assert.Equal(new byte[count], pair.Receive(count)); Assert.Equal(7, tracker.Records);
        Assert.Equal(2 + count, origin.ReaderIndex);
        using var input = new Pair(); input.SendAndFinish([68]);
        Assert.Equal(1, await Set(b, async, 7, input.Local, 3, input.Token)); Assert.Equal(8, tracker.Records);
        Assert.Equal(68, origin.GetByte(7));
        using var relativeInput = new Pair(); relativeInput.SendAndFinish([69]);
        Assert.Equal(1, await Write(b, async, relativeInput.Local, 3, relativeInput.Token)); Assert.Equal(9, tracker.Records);
        Assert.Equal(69, origin.GetByte(5)); Assert.Equal(6, origin.WriterIndex);
        Assert.True(b.Release()); Assert.Equal(1, tracker.Closes);
    }

    [Theory, MemberData(nameof(Modes))]
    public async Task ReleasedBuffersRejectSocketAccessBeforeIo(int kind, bool async)
    {
        using var scope = new Owner(kind);
        using var pair = new Pair();
        ByteBuf b = scope.Buffer; b.Release();
        await Assert.ThrowsAsync<IllegalReferenceCountException>(async () => await Get(b, async, 0, pair.Local, 1, pair.Token));
        await Assert.ThrowsAsync<IllegalReferenceCountException>(async () => await Set(b, async, 0, pair.Local, 1, pair.Token));
        await Assert.ThrowsAsync<IllegalReferenceCountException>(async () => await Read(b, async, pair.Local, 1, pair.Token));
        await Assert.ThrowsAsync<IllegalReferenceCountException>(async () => await Write(b, async, pair.Local, 1, pair.Token));
    }

    private sealed class Owner : IDisposable
    {
        private readonly NativeMemoryAllocator _native = new();
        private readonly IDisposable _allocator;
        internal ByteBuf Buffer { get; }
        internal Owner(int kind)
        {
            if (kind == 4)
            {
                var allocator = new PooledByteBufAllocator(nativeAllocator: _native, maxOrder: 1);
                _allocator = allocator; Buffer = allocator.DirectBuffer(24, 256);
            }
            else if (kind == 5)
            {
                var allocator = new AdaptiveByteBufAllocator(nativeAllocator: _native, lowMemory: false);
                _allocator = allocator; Buffer = allocator.DirectBuffer(24, 256);
            }
            else Buffer = kind switch
            {
                0 => Unpooled.Buffer(24, 256),
                1 => new UnpooledDirectByteBuf(24, 256, _native),
                2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.Buffer(9, 9).WriteZero(9))
                    .AddComponent(Unpooled.EmptyBuffer).AddComponent(new UnpooledDirectByteBuf(15, 15, _native).WriteZero(15)),
                _ => new UnpooledDirectByteBuf(28, 28, _native).Slice(2, 24)
            };
        }
        public void Dispose()
        {
            if (Buffer.ReferenceCount != 0) Buffer.Release(Buffer.ReferenceCount);
            _allocator?.Dispose();
            Assert.Equal(0, _native.ReservedBytes);
        }
    }

    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Records, Closes;
        public void Record() => Records++;
        public void Record(object hint) => Records++;
        public bool Close(ByteBuf b) { Assert.Same(expected, b); return ++Closes == 1; }
    }

    private sealed class Pair : IDisposable
    {
        private readonly CancellationTokenSource _timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        internal Socket Local { get; }
        internal Socket Peer { get; }
        internal CancellationToken Token => _timeout.Token;
        internal Pair()
        {
            _timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0)); listener.Listen(1);
            Local = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Local.Connect(listener.LocalEndPoint);
            Peer = listener.Accept();
            Local.ReceiveTimeout = Local.SendTimeout = Peer.ReceiveTimeout = Peer.SendTimeout = 5000;
        }
        internal byte[] Receive(int length)
        {
            var bytes = new byte[length];
            for (int offset = 0; offset < length;)
            {
                int count = Peer.Receive(bytes.AsSpan(offset));
                Assert.True(count > 0); offset += count;
            }
            return bytes;
        }
        internal void SendAndFinish(byte[] bytes)
        {
            for (int offset = 0; offset < bytes.Length;) offset += Peer.Send(bytes.AsSpan(offset));
            Peer.Shutdown(SocketShutdown.Send);
        }
        public void Dispose() { Local.Dispose(); Peer.Dispose(); _timeout.Dispose(); }
    }
}
