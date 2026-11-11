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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Source write order and independently owned cleanup.
public class SocketFileRegionSourceTest
{
    protected virtual bool Domain => false;

    private ShutdownFixture NewFixture(bool adaptive) => new(adaptive, Domain);

    private EndPoint NewAddress() => Domain ? DomainSocketSourceTest.NewAddress() : new IPEndPoint(IPAddress.Loopback, 0);

    private void RequireBackend() { if (Domain) DomainSocketSourceTest.RequireDomainSockets(); }
    private static readonly byte[] Data = RandomData(1048576 * 10);
    private static byte[] RandomData(int length) { var bytes = new byte[length]; Random.Shared.NextBytes(bytes); return bytes; }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestFileRegion(bool adaptive) => Transfer(adaptive, false, true, true, TestContext.Current.CancellationToken);
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestCustomFileRegion(bool adaptive) => Transfer(adaptive, false, true, false, TestContext.Current.CancellationToken);
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestFileRegionNotAutoRead(bool adaptive) => Transfer(adaptive, false, false, true, TestContext.Current.CancellationToken);
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestFileRegionVoidPromise(bool adaptive) => Transfer(adaptive, true, true, true, TestContext.Current.CancellationToken);
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestFileRegionVoidPromiseNotAutoRead(bool adaptive) => Transfer(adaptive, true, false, true, TestContext.Current.CancellationToken);

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestFileRegionCountLargerThenFile(bool adaptive)
    {
        RequireBackend();
        await using var fixture = NewFixture(adaptive);
        string path = Path.GetTempFileName();
        IChannel server = null, client = null;
        try
        {
            await File.WriteAllBytesAsync(path, Data, TestContext.Current.CancellationToken);
            server = await fixture.Server.ChildHandler(new Discard()).BindAsync(NewAddress());
            client = await fixture.Client.Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(server.LocalAddress);
            // Request file region which is bigger then the underlying file.
            var region = new DefaultFileRegion(Open(path), 0, Data.Length + 1024);
            await Assert.ThrowsAnyAsync<IOException>(() => client.WriteAndFlushAsync(region, TestContext.Current.CancellationToken));
            Assert.Equal(0, region.ReferenceCount);
        }
        finally { if (client != null) await client.CloseAsync(); if (server != null) await server.CloseAsync(); File.Delete(path); }
    }

    /**
     * Reproducer for a {@code FileRegion} drain-loop overshoot. Transports must short-circuit
     * as soon as the region reports {@code transferred() >= count()}; any extra
     * {@code transferTo} call violates the contract and would corrupt
     * implementations that lazily emit per-chunk framing.
     *
     * <p>A custom {@link FileRegion} whose {@code transferTo} advances {@code transferred}
     * past the bytes it writes to the target on a single call (legal: an encryption- or
     * framing-on-write FileRegion that flushes a complete inner chunk in one call behaves
     * this way) records every {@code transferTo} invocation made past
     * {@code transferred == count}. The expected behaviour for every transport is that
     * {@code transferToCallsPastCompletion} stays zero.
     */
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestFileRegionDrainStopsAtCompletion(bool adaptive)
    {
        RequireBackend();
        await using var fixture = NewFixture(adaptive);
        // Region size > 1 so any chunked transport (e.g. io_uring's generic FileRegion fallback)
        // that sizes its chunk buffer from count() retains spare capacity after the first
        // (and only) source byte is written -- without that spare capacity an inner drain loop
        // would exit on writableBytes() == 0 and the overshoot path would not be exercised.
        const int regionSize = 16;
        IChannel server = await fixture.Server.ChildHandler(new Discard()).BindAsync(NewAddress());
        IChannel client = null;
        try
        {
            client = await fixture.Client.Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(server.LocalAddress);
            // OioByteStreamChannel#doWriteFileRegion drains using a locally-tracked
            // bytes-written counter as the transferTo position (ignoring transferred()), so an
            // ill-behaved FileRegion that advances transferred() past actual bytes written --
            // the exact pattern this fixture uses to exercise the overshoot path -- cannot
            // satisfy OIO's loop without violating the position invariant. The overshoot
            // detection is meaningless on OIO for the same reason; skip the permutation.
            // CLR: its single asynchronous socket backend has no OIO permutation.
            var region = new OvershootDetectingFileRegion(regionSize);
            // sync() blocks until the write future completes, by which point every
            // transferTo() call the transport is going to make has already been made --
            // the fixture's call counter is observable here without any timing wait. Ref
            // ownership is transferred to the pipeline on writeAndFlush(), which releases
            // it as the write completes (success or failure).
            await client.WriteAndFlushAsync(region, TestContext.Current.CancellationToken);
            Assert.Equal(0, region.TransferToCallsPastCompletion);
            Assert.Equal(0, region.ReferenceCount);
        }
        finally { if (client != null) await client.CloseAsync(); await server.CloseAsync(); }
    }

    private async Task Transfer(bool adaptive, bool unobservedWrites, bool autoRead, bool defaultFileRegion, CancellationToken cancellation)
    {
        RequireBackend();
        await using var fixture = NewFixture(adaptive);
        const int bufferSize = 1024;
        string path = Path.GetTempFileName();
        IChannel server = null, client = null;
        try
        {
            // Prepend random data which will not be transferred, so that we can test non-zero start offset
            int startOffset = Random.Shared.Next(8192);
            await using (var output = File.Create(path))
            {
                await output.WriteAsync(RandomData(startOffset), cancellation);
                // .. and here comes the real data to transfer.
                await output.WriteAsync(Data.AsMemory(bufferSize), cancellation);
                // .. and then some extra data which is not supposed to be transferred.
                await output.WriteAsync(RandomData(Random.Shared.Next(8192)), cancellation);
            }
            var received = new TestHandler(autoRead);
            server = await fixture.Server.ChildOption(ChannelOption.AUTO_READ, autoRead).ChildHandler(received).BindAsync(NewAddress(), cancellation);
            client = await fixture.Client.Option(ChannelOption.AUTO_READ, autoRead).Handler(new Discard(autoRead)).ConnectAsync(server.LocalAddress, cancellationToken: cancellation);
            IFileRegion region = new DefaultFileRegion(Open(path), startOffset, Data.Length - bufferSize);
            IFileRegion emptyRegion = new DefaultFileRegion(Open(path), 0, 0);
            if (!defaultFileRegion) { region = new FileRegionWrapper(region); emptyRegion = new FileRegionWrapper(emptyRegion); }
            // Do write ByteBuf and then FileRegion to ensure that mixed writes work
            // Also, write an empty FileRegion to test if writing an empty FileRegion does not cause any issues.
            //
            // See https://github.com/netty/netty/issues/2769
            //     https://github.com/netty/netty/issues/2964
            ByteBuf prefix = Random.Shared.Next(2) == 0 ? client.Allocator.DirectBuffer().WriteBytes(Data.AsSpan(0, bufferSize))
                : Unpooled.WrappedBuffer(Data, 0, bufferSize);
            // CLR uses Task observation rather than a caller-supplied VoidPromise identity.
            // Both unobserved cases retain the original write/write/writeAndFlush order and full wire assertions.
            Task first = client.WriteAsync(prefix, cancellation);
            Task empty = client.WriteAsync(emptyRegion, cancellation);
            Task last = client.WriteAndFlushAsync(region, cancellation);
            if (!unobservedWrites) await Task.WhenAll(first, empty, last).WaitAsync(cancellation);
            await received.Done.Task.WaitAsync(cancellation);
            await received.Channel.CloseAsync();
            Assert.Null(received.Error);
            // Make sure we did not receive more than we expected.
            Assert.Equal(Data.Length, received.Counter);
            Assert.Equal(0, prefix.ReferenceCount);
            Assert.Equal(0, emptyRegion.ReferenceCount);
            if (!unobservedWrites) Assert.Equal(0, region.ReferenceCount);
        }
        finally { if (client != null) await client.CloseAsync(); if (server != null) await server.CloseAsync(); File.Delete(path); }
    }
    // CLR file-handle sends require an asynchronous FileStream. This preserves the original range and file identity.
    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 1, options: FileOptions.Asynchronous);
    private sealed class Discard(bool autoRead = true) : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, object message)
        {
            // Just drop the message.
            // drain
        }
        public override void ChannelReadComplete(ChannelHandlerContext context) { if (!autoRead) context.Read(); }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => _ = context.CloseAsync();
    }
    private sealed class TestHandler(bool autoRead) : SimpleChannelInboundHandler<ByteBuf>
    {
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IChannel Channel;
        internal int Counter;
        internal Exception Error;
        public override void ChannelActive(ChannelHandlerContext context) { Channel = context.Channel; if (!autoRead) context.Read(); }
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf input)
        {
            try
            {
                var actual = new byte[input.ReadableBytes];
                input.ReadBytes(actual);
                Assert.True(Data.AsSpan(Counter, actual.Length).SequenceEqual(actual));
                Counter += actual.Length;
                if (Counter >= Data.Length) Done.TrySetResult();
            }
            catch (Exception error) { Fail(context, error); }
        }
        public override void ChannelReadComplete(ChannelHandlerContext context) { if (!autoRead) context.Read(); }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => Fail(context, error);
        private void Fail(ChannelHandlerContext context, Exception error)
        { if (Interlocked.CompareExchange(ref Error, error, null) == null) { Done.TrySetException(error); _ = context.CloseAsync(); } }
    }

    /**
     * Custom {@link FileRegion} that advances {@code transferred()} past the bytes it writes
     * to the target on a single {@code transferTo} call -- the same pattern an
     * encryption-on-write or framing FileRegion follows when it reports an inner chunk as
     * "delivered" once that chunk fully drains. Records every {@code transferTo} invocation
     * made after the region has been fully transferred so a drain-loop overshoot is visible
     * synchronously on the writer side.
     *
     * <ul>
     *   <li>{@link #count()} returns the constant configured size.</li>
     *   <li>The first {@code transferTo} call writes one byte to the target; once that
     *       byte is accepted it advances {@code transferred} to {@code count} and returns
     *       the bytes written.</li>
     *   <li>Every subsequent {@code transferTo} call increments
     *       {@link #transferToCallsPastCompletion} and writes one phantom byte to mimic
     *       the protocol-corrupting side effect a real overshoot would produce.</li>
     * </ul>
     * The {@code transferred} field is written and read only on the EventLoop (in
     * {@code transferTo} and the surrounding drain loop), so no synchronization is needed.
     * The cross-thread observation channel is the {@link AtomicInteger} counter, which the
     * test reads after {@code writeAndFlush(...).sync()} establishes the happens-before.
     */
    // CLR: TransferTo and its logical counter are serialized on the fallback worker. The write Task
    // publishes that producer's result before the test observes the counter; socket progress returns to the owner.
    internal sealed class OvershootDetectingFileRegion : AbstractReferenceCounted, IFileRegion
    {
        private readonly long _count;
        private long _transferred;
        internal int TransferToCallsPastCompletion;
        internal OvershootDetectingFileRegion(long count)
        {
            // count must be > 1 so the chunk buffer retains writable capacity after the first
            // source byte is written -- otherwise the drain-loop overshoot path is not exercised.
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 2);
            _count = count;
        }
        public long Position() => 0;
        public long Count() => _count;
        public long Transferred() => _transferred;
        public long TransferTo(Stream target, long position)
        {
            // Per FileRegion's contract, the caller passes transferred() as position. Surface
            // a violation via IOException so the transport's catch (Exception) at the write
            // site routes it to the write future's cause -- a JUnit AssertionError would
            // bypass that catch and wedge the EventLoop.
            if (position != _transferred) throw new IOException($"transferTo position {position} != transferred() {_transferred}");
            if (_transferred < _count) { target.WriteByte(0x42); _transferred = _count; return 1; }
            Interlocked.Increment(ref TransferToCallsPastCompletion);
            target.WriteByte(0xff);
            return 1;
        }
        protected override void Deallocate()
        {
            // No native resources to release. The overshoot counter is observed on the
            // writer thread immediately after sync() returns; nothing else needs cleanup.
        }
        public new IFileRegion Retain() { base.Retain(); return this; }
        public override IFileRegion Retain(int increment) { base.Retain(increment); return this; }
        public new IFileRegion Touch() => this;
        public override IFileRegion Touch(object hint) => this;
    }
    private sealed class FileRegionWrapper(IFileRegion region) : IFileRegion
    {
        public int ReferenceCount => region.ReferenceCount;
        public long Position() => region.Position();
        public long Count() => region.Count();
        public long Transferred() => region.Transferred();
        public long TransferTo(Stream target, long position) => region.TransferTo(target, position);
        public bool Release() => region.Release();
        public bool Release(int decrement) => region.Release(decrement);
        public IFileRegion Retain() { region.Retain(); return this; }
        public IFileRegion Retain(int increment) { region.Retain(increment); return this; }
        public IFileRegion Touch() { region.Touch(); return this; }
        public IFileRegion Touch(object hint) { region.Touch(hint); return this; }
        IReferenceCounted IReferenceCounted.Retain() => Retain();
        IReferenceCounted IReferenceCounted.Retain(int increment) => Retain(increment);
        IReferenceCounted IReferenceCounted.Touch() => Touch();
        IReferenceCounted IReferenceCounted.Touch(object hint) => Touch(hint);
    }
}
#pragma warning restore xUnit1051
