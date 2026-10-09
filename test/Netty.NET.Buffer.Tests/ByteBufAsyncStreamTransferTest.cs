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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// CLR asynchronous counterparts of the pinned ByteBuf stream-transfer contracts.
public class ByteBufAsyncStreamTransferTest
{
    private static ByteBuf Owner(int kind)
    {
        if (kind == 0) return Unpooled.Buffer(8, 128).SetZero(0, 8);
        if (kind == 1) return Unpooled.DirectBuffer(8, 128).SetZero(0, 8);
        return Unpooled.CompositeBuffer(2).AddComponent(Unpooled.WrappedBuffer(new byte[3]))
            .AddComponent(Unpooled.DirectBuffer(5, 5).WriteZero(5));
    }
    private static byte[] Bytes(ByteBuf b, int index, int length) => b.AsReadOnlySequence(index, length).ToArray();
    private static async Task Finish(Task task) => await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task DelayedRelativeReadCommitsActualCountOnlyAfterCompletion(int kind)
    {
        ByteBuf b = Owner(kind); b.SetIndex(1, 2);
        using var input = new DelayedStream();
        try
        {
            Task<int> pending = b.WriteBytesAsync(input, 10, TestContext.Current.CancellationToken).AsTask();
            Assert.False(pending.IsCompleted); Assert.True(b.Capacity >= 12);
            Assert.Equal(2, b.WriterIndex); Assert.Equal(1, b.ReaderIndex);
            Assert.Equal(new byte[6], Bytes(b, 2, 6)); Assert.Equal(1, b.ReferenceCount);
            input.Complete(); await Finish(pending);
            Assert.Equal(4, (await pending)); Assert.Equal(6, b.WriterIndex); Assert.Equal(1, b.ReaderIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, Bytes(b, 2, 4));
            Assert.Equal(1, input.Calls); Assert.Equal(10, input.RequestedLength); Assert.False(input.Disposed);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task DelayedRelativeOutputAdvancesReaderOnlyAfterCompletion(int kind)
    {
        ByteBuf b = Owner(kind).WriteBytes(new byte[] { 9, 1, 2, 3, 4 }); b.ReaderIndex = 1;
        using var output = new DelayedStream();
        try
        {
            Task pending = b.ReadBytesAsync(output, 4, TestContext.Current.CancellationToken).AsTask();
            Assert.False(pending.IsCompleted); Assert.Equal(1, b.ReaderIndex);
            Assert.Equal(5, b.WriterIndex); Assert.Null(output.Accepted);
            output.Complete(); await Finish(pending);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, output.Accepted);
            Assert.Equal(5, b.ReaderIndex); Assert.Equal(1, output.Calls); Assert.False(output.Disposed);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task AbsoluteTransfersPreserveIndicesAndShortReadsReturnImmediately(int kind)
    {
        ByteBuf b = Owner(kind); b.SetIndex(1, 2);
        using var input = new DelayedStream(); using var output = new DelayedStream();
        try
        {
            Task<int> read = b.SetBytesAsync(2, input, 6, TestContext.Current.CancellationToken).AsTask(); input.Complete(); await Finish(read);
            Assert.Equal(4, (await read)); Assert.Equal(1, input.Calls);
            Assert.Equal(1, b.ReaderIndex); Assert.Equal(2, b.WriterIndex);
            Task write = b.GetBytesAsync(2, output, 4, TestContext.Current.CancellationToken).AsTask(); output.Complete(); await Finish(write);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, output.Accepted);
            Assert.Equal(1, b.ReaderIndex); Assert.Equal(2, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task PreCancellationPerformsNoIoGrowthOrIndexChange(int kind)
    {
        ByteBuf b = Owner(kind).WriteBytes(new byte[] { 1, 2 });
        using var stream = new DelayedStream(); using var cts = new CancellationTokenSource(); cts.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.WriteBytesAsync(stream, 30, cts.Token).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.SetBytesAsync(0, stream, 2, cts.Token).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.ReadBytesAsync(stream, 2, cts.Token).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.GetBytesAsync(0, stream, 2, cts.Token).AsTask());
            Assert.Equal(0, stream.Calls); Assert.Equal(8, b.Capacity);
            Assert.Equal(0, b.ReaderIndex); Assert.Equal(2, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task PendingInputCancellationDoesNotPublishAndKeepsReservedCapacity(int kind)
    {
        ByteBuf b = Owner(kind); b.WriterIndex = 2;
        using var stream = new DelayedStream(); using var cts = new CancellationTokenSource();
        try
        {
            Task<int> pending = b.WriteBytesAsync(stream, 30, cts.Token).AsTask();
            Assert.Equal(cts.Token, stream.Token); Assert.False(pending.IsCompleted);
            int capacity = b.Capacity; Assert.True(capacity >= 32); cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.True(pending.IsCanceled); Assert.Equal(capacity, b.Capacity);
            Assert.Equal(2, b.WriterIndex); Assert.Equal(new byte[6], Bytes(b, 2, 6));
            Assert.False(stream.Disposed);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public async Task FailedOrCanceledOutputCanAcceptPrefixWithoutAdvancingReader(int kind, bool cancel)
    {
        ByteBuf b = Owner(kind).WriteBytes(new byte[] { 9, 1, 2, 3, 4 }); b.ReaderIndex = 1;
        using var stream = new DelayedStream { Fail = !cancel, CancelAfterCopy = cancel };
        using var cts = new CancellationTokenSource(); stream.Cancellation = cts;
        try
        {
            Task pending = b.ReadBytesAsync(stream, 4, cts.Token).AsTask(); stream.Complete();
            if (cancel)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            else
                await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 1 }, stream.Accepted);
            Assert.Equal(1, b.ReaderIndex); Assert.Equal(5, b.WriterIndex); Assert.False(stream.Disposed);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public async Task FailedOrCanceledReadAfterTouchingStagingPublishesNothing(int kind, bool cancel)
    {
        ByteBuf b = Owner(kind); b.WriterIndex = 1;
        using var stream = new DelayedStream { Fail = !cancel, CancelAfterCopy = cancel };
        using var cts = new CancellationTokenSource(); stream.Cancellation = cts;
        try
        {
            Task<int> pending = b.WriteBytesAsync(stream, 4, cts.Token).AsTask(); stream.Complete();
            if (cancel)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            else
                await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(new byte[8], Bytes(b, 0, 8)); Assert.Equal(1, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SuccessfulIoIsPublishedWhenStreamIgnoresLateCancellation(bool input)
    {
        ByteBuf b = Owner(1).WriteBytes(new byte[] { 1, 2, 3, 4 });
        using var stream = new DelayedStream { IgnoreCancellation = true };
        using var cts = new CancellationTokenSource();
        try
        {
            Task pending = input ? b.WriteBytesAsync(stream, 4, cts.Token).AsTask()
                : b.ReadBytesAsync(stream, 4, cts.Token).AsTask();
            cts.Cancel(); stream.Complete(); await Finish(pending);
            Assert.Equal(input ? 8 : 4, b.WriterIndex); Assert.Equal(input ? 0 : 4, b.ReaderIndex);
            if (input) Assert.Equal(new byte[] { 1, 2, 3, 4 }, Bytes(b, 4, 4));
            else Assert.Equal(new byte[] { 1, 2, 3, 4 }, stream.Accepted);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task EofReturnsZeroAfterOneReadAndDoesNotAdvanceWriter(int kind)
    {
        ByteBuf b = Owner(kind); using var stream = new DelayedStream { Count = 0 };
        try
        {
            Task<int> pending = b.WriteBytesAsync(stream, 30, TestContext.Current.CancellationToken).AsTask(); stream.Complete(); await Finish(pending);
            Assert.Equal(0, (await pending)); Assert.Equal(0, b.WriterIndex);
            Assert.True(b.Capacity >= 30); Assert.Equal(1, stream.Calls);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(-1)] [InlineData(9)]
    public async Task InvalidReadCountIsRejectedBeforeCommit(int count)
    {
        ByteBuf b = Owner(1); using var stream = new DelayedStream { Count = count };
        try
        {
            Task<int> pending = b.WriteBytesAsync(stream, 8, TestContext.Current.CancellationToken).AsTask(); stream.Complete();
            await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(0, b.WriterIndex); Assert.Equal(new byte[8], Bytes(b, 0, 8));
        }
        finally { b.Release(); }
    }

    [Fact]
    public async Task ValidationAndZeroRequestsNeverInvokeStream()
    {
        ByteBuf b = Owner(0); using var stream = new DelayedStream();
        try
        {
            await b.GetBytesAsync(8, stream, 0, TestContext.Current.CancellationToken); Assert.Equal(0, await b.SetBytesAsync(8, stream, 0, TestContext.Current.CancellationToken));
            await b.ReadBytesAsync(stream, 0, TestContext.Current.CancellationToken); Assert.Equal(0, await b.WriteBytesAsync(stream, 0, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.GetBytesAsync(9, stream, 0, TestContext.Current.CancellationToken).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.SetBytesAsync(1, stream, int.MaxValue, TestContext.Current.CancellationToken).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.ReadBytesAsync(stream, 1, TestContext.Current.CancellationToken).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.WriteBytesAsync(stream, -1, TestContext.Current.CancellationToken).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.WriteBytesAsync(stream, 129, TestContext.Current.CancellationToken).AsTask());
            await Assert.ThrowsAsync<ArgumentNullException>(() => b.GetBytesAsync(0, null, 0, TestContext.Current.CancellationToken).AsTask());
            await Assert.ThrowsAsync<ArgumentNullException>(() => b.SetBytesAsync(0, null, 0, TestContext.Current.CancellationToken).AsTask());
            await Assert.ThrowsAsync<ArgumentNullException>(() => b.ReadBytesAsync(null, 0, TestContext.Current.CancellationToken).AsTask());
            await Assert.ThrowsAsync<ArgumentNullException>(() => b.WriteBytesAsync(null, 0, TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(0, stream.Calls); Assert.Equal(8, b.Capacity);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReadonlyIncludingFixedCompositeSupportsOutputAndRejectsEmptyInput(bool composite)
    {
        ByteBuf b = composite ? Unpooled.WrappedUnmodifiableBuffer(
            Unpooled.WrappedBuffer(new byte[] { 1, 2 }),
            Unpooled.DirectBuffer(2, 2).WriteBytes(new byte[] { 3, 4 }))
            : Owner(1).WriteBytes(new byte[] { 1, 2, 3, 4 }).AsReadOnly();
        using var stream = new DelayedStream();
        try
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => b.SetBytesAsync(0, stream, 0, TestContext.Current.CancellationToken).AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(() => b.WriteBytesAsync(stream, 0, TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(0, stream.Calls);
            Task pending = b.ReadBytesAsync(stream, 4, TestContext.Current.CancellationToken).AsTask(); stream.Complete(); await Finish(pending);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, stream.Accepted); Assert.Equal(4, b.ReaderIndex);
        }
        finally { b.Release(); }
    }

    [Fact]
    public async Task ReleasedBuffersRejectAllAsyncOperationsEvenWhenEmpty()
    {
        ByteBuf b = Owner(1); b.Release(); using var stream = new DelayedStream();
        await Assert.ThrowsAsync<IllegalReferenceCountException>(() => b.GetBytesAsync(0, stream, 0, TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<IllegalReferenceCountException>(() => b.SetBytesAsync(0, stream, 0, TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<IllegalReferenceCountException>(() => b.ReadBytesAsync(stream, 0, TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<IllegalReferenceCountException>(() => b.WriteBytesAsync(stream, 0, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, stream.Calls);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task StreamAdaptersCanAppendToSameBufferAcrossGrowthAndConsolidation(int kind)
    {
        ByteBuf b = Owner(kind).WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        using var output = new ByteBufOutputStream(b);
        try
        {
            await b.ReadBytesAsync(output, 8, TestContext.Current.CancellationToken);
            Assert.Equal(8, b.ReaderIndex); Assert.Equal(16, b.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Bytes(b, 8, 8));
            using var input = new ByteBufInputStream(b);
            Assert.Equal(8, await b.WriteBytesAsync(input, 8, TestContext.Current.CancellationToken));
            Assert.Equal(16, b.ReaderIndex); Assert.Equal(24, b.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Bytes(b, 16, 8));
        }
        finally { b.Release(); }
    }

    [Fact]
    public async Task SliceOffsetsAndUnreleasableSharedWriterSurviveSuspension()
    {
        ByteBuf b = Owner(1); ByteBuf slice = b.Slice(2, 6).Clear();
        ByteBuf wrapper = Unpooled.UnreleasableBuffer(b);
        using var first = new DelayedStream(); using var second = new DelayedStream();
        try
        {
            Task<int> pending = slice.WriteBytesAsync(first, 4, TestContext.Current.CancellationToken).AsTask(); first.Complete(); await Finish(pending);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, Bytes(b, 2, 4));
            Assert.Equal(0, b.WriterIndex); Assert.Equal(4, slice.WriterIndex);
            pending = wrapper.WriteBytesAsync(second, 4, TestContext.Current.CancellationToken).AsTask();
            Assert.Equal(0, b.WriterIndex); second.Complete(); await Finish(pending);
            Assert.Equal(4, b.WriterIndex); Assert.Equal(4, wrapper.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Fact]
    public async Task CompositeCommitFailureCanChangePrefixWithoutAdvancingWriter()
    {
        ByteBuf b = Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[2]))
            .AddComponent(Unpooled.WrappedBuffer(new byte[2]).AsReadOnly());
        using var stream = new DelayedStream();
        try
        {
            Task<int> pending = b.WriteBytesAsync(stream, 4, TestContext.Current.CancellationToken).AsTask(); stream.Complete();
            await Assert.ThrowsAsync<NotSupportedException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(0, b.WriterIndex); Assert.Equal(new byte[] { 1, 2, 0, 0 }, Bytes(b, 0, 4));
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SuspendedOutputSnapshotSurvivesCallbackResizeOrConsolidation(bool composite)
    {
        ByteBuf b = Owner(composite ? 2 : 1).WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        using var stream = new DelayedStream
        {
            Callback = () =>
            {
                if (b is CompositeByteBuf c) c.Consolidate();
                else b.Capacity = 64;
            }
        };
        try
        {
            Task pending = b.ReadBytesAsync(stream, 8, TestContext.Current.CancellationToken).AsTask();
            Assert.False(pending.IsCompleted); stream.Complete(); await Finish(pending);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, stream.Accepted);
            Assert.Equal(8, b.ReaderIndex); Assert.Equal(8, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SuspendedInputRechecksCallbackResizedNativeStorage(bool shrink)
    {
        ByteBuf b = Owner(1);
        using var stream = new DelayedStream { Callback = () => b.Capacity = shrink ? 4 : 64 };
        try
        {
            Task<int> pending = b.SetBytesAsync(2, stream, 6, TestContext.Current.CancellationToken).AsTask(); stream.Complete();
            if (shrink)
            {
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
                Assert.Equal(new byte[4], Bytes(b, 0, 4));
            }
            else
            {
                await Finish(pending); Assert.Equal(4, await pending);
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, Bytes(b, 2, 4));
            }
            Assert.Equal(0, b.WriterIndex);
        }
        finally { b.Release(); }
    }

    [Fact]
    public async Task EmptySingletonValidatesCancellationWithoutCallingStreams()
    {
        ByteBuf b = Unpooled.EmptyBuffer; using var stream = new DelayedStream();
        await b.GetBytesAsync(0, stream, 0, TestContext.Current.CancellationToken); await b.ReadBytesAsync(stream, 0, TestContext.Current.CancellationToken);
        Assert.Equal(0, await b.SetBytesAsync(0, stream, 0, TestContext.Current.CancellationToken)); Assert.Equal(0, await b.WriteBytesAsync(stream, 0, TestContext.Current.CancellationToken));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.GetBytesAsync(0, stream, 0, cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.ReadBytesAsync(stream, 0, cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.SetBytesAsync(0, stream, 0, cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.WriteBytesAsync(stream, 0, cts.Token).AsTask());
        Assert.Equal(0, stream.Calls);
    }

    [Fact]
    public async Task InvalidArgumentsAndPermissionsTakePrecedenceOverPreCancellation()
    {
        ByteBuf b = Owner(0); using var stream = new DelayedStream();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        try
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.WriteBytesAsync(stream, 129, cts.Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.WriteBytesAsync(stream, -1, cts.Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.SetBytesAsync(8, stream, 1, cts.Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.GetBytesAsync(8, stream, 1, cts.Token).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.ReadBytesAsync(stream, 1, cts.Token).AsTask());
            await Assert.ThrowsAsync<ArgumentNullException>(() => b.WriteBytesAsync(null, 0, cts.Token).AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(() => b.AsReadOnly().WriteBytesAsync(stream, 0, cts.Token).AsTask());
            Assert.Equal(8, b.Capacity); Assert.Equal(0, stream.Calls);
        }
        finally { b.Release(); }
    }

    private sealed class DelayedStream : Stream
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public int Count = 4;
        public int RequestedLength;
        public CancellationToken Token;
        public CancellationTokenSource Cancellation;
        public bool Fail, CancelAfterCopy, IgnoreCancellation, Disposed;
        public byte[] Accepted;
        public Action Callback;
        public void Complete() => _completion.TrySetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            Calls++; RequestedLength = destination.Length; Token = cancellationToken;
            await _completion.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
            Callback?.Invoke();
            for (int i = 0; i < Math.Min(destination.Length, Math.Max(0, Count)); i++) destination.Span[i] = (byte)(i + 1);
            FailAfterCopy(cancellationToken); return Count;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        {
            Calls++; RequestedLength = source.Length; Token = cancellationToken;
            await _completion.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
            Callback?.Invoke();
            Accepted = (Fail || CancelAfterCopy ? source[..1] : source).ToArray(); FailAfterCopy(cancellationToken);
        }
        private void FailAfterCopy(CancellationToken token)
        {
            if (Fail) throw new IOException("Failure after staging or accepting bytes.");
            if (CancelAfterCopy) { Cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int offset, int count) => throw new InvalidOperationException("Synchronous I/O is forbidden.");
        public override void Write(byte[] b, int offset, int count) => throw new InvalidOperationException("Synchronous I/O is forbidden.");
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
