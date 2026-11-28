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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source waits for and closes its channels independently of the test cancellation.
public class SocketFileRegionSourceTest : AbstractSocketTest
{
    internal static readonly byte[] data = new byte[1048576 * 10];

    static SocketFileRegionSourceTest()
    {
        // CLR: Random.Shared ("a thread-safe Random instance that may be used concurrently from any thread",
        // https://learn.microsoft.com/en-us/dotnet/api/system.random.shared) replaces ThreadLocalRandom.current().
        Random.Shared.NextBytes(data);
    }

    protected virtual bool Domain => false;

    // CLR: the DomainSocketFileRegionSourceTest subclass is the EpollDomainSocketFileRegionTest counterpart; Java
    // overrides newFactories()/newSocketAddress() there, this port selects the same overrides through the Domain hook.
    protected override List<IBootstrapComboFactory<ServerBootstrap, Bootstrap>> NewFactories()
    {
        if (!Domain)
        {
            return base.NewFactories();
        }

        DomainSocketSourceTest.RequireDomainSockets();

        return DomainSocketTestPermutation.INSTANCE.DomainSocket();
    }

    protected override EndPoint NewSocketAddress()
    {
        return Domain ? DomainSocketTestPermutation.NewDomainSocketAddress() : base.NewSocketAddress();
    }

    [Fact]
    public async Task TestFileRegion()
    {
        await Run(TestContext.Current, TestFileRegion0);
    }

    protected virtual bool SupportsCustomFileRegion()
    {
        return true;
    }

    [Fact]
    public async Task TestCustomFileRegion()
    {
        // CLR: xUnit v3's Assert.SkipUnless "will skip the test if condition is false"
        // (https://xunit.net/docs/getting-started/v3/whats-new), as assumeTrue(...) aborts it; it requires a reason.
        Assert.SkipUnless(SupportsCustomFileRegion(), "assumption is not true");
        await Run(TestContext.Current, TestCustomFileRegion0);
    }

    [Fact]
    public async Task TestFileRegionNotAutoRead()
    {
        await Run(TestContext.Current, TestFileRegionNotAutoRead0);
    }

    [Fact]
    public async Task TestFileRegionVoidPromise()
    {
        await Run(TestContext.Current, TestFileRegionVoidPromise0);
    }

    [Fact]
    public async Task TestFileRegionVoidPromiseNotAutoRead()
    {
        await Run(TestContext.Current, TestFileRegionVoidPromiseNotAutoRead0);
    }

    [Fact]
    public async Task TestFileRegionCountLargerThenFile()
    {
        await Run(TestContext.Current, TestFileRegionCountLargerThenFile0);
    }

    [Fact(Timeout = 30000)]
    public async Task TestFileRegionDrainStopsAtCompletion()
    {
        Assert.SkipUnless(SupportsCustomFileRegion(), "assumption is not true");
        await Run(TestContext.Current, TestFileRegionDrainStopsAtCompletion0);
    }

    // CLR: xUnit1024 forbids overloading the TestFileRegion test name; the same applies to the six bodies below.
    private async Task TestFileRegion0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestFileRegion0(sb, cb, false, true, true);
    }

    private async Task TestCustomFileRegion0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestFileRegion0(sb, cb, false, true, false);
    }

    private async Task TestFileRegionVoidPromise0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestFileRegion0(sb, cb, true, true, true);
    }

    private async Task TestFileRegionNotAutoRead0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestFileRegion0(sb, cb, false, false, true);
    }

    private async Task TestFileRegionVoidPromiseNotAutoRead0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestFileRegion0(sb, cb, true, false, true);
    }

    private async Task TestFileRegionCountLargerThenFile0(ServerBootstrap sb, Bootstrap cb)
    {
        FileInfo file = PlatformDependent.CreateTempFile("netty-", ".tmp", null);
        DeleteOnExit(file);

        FileStream @out = new FileStream(file.FullName, FileMode.Create, FileAccess.Write);
        @out.Write(data);
        @out.Close();

        sb.ChildHandler(new DropHandler());
        cb.Handler(new ChannelInboundHandlerAdapter());

        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);

        // Request file region which is bigger then the underlying file.
        IFileRegion region = new DefaultFileRegion(
                OpenRead(file), 0, data.Length + 1024);

        // CLR: Future.await() waits without rethrowing; awaiting Task.WhenAny does the same because its Task "always
        // ends in the RanToCompletion state" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenany).
        // Task.Exception wraps the cause in an AggregateException, and Assert.IsAssignableFrom is assertInstanceOf.
        Task write = cc.WriteAndFlushAsync(region);
        await Task.WhenAny(write);
        Assert.IsAssignableFrom<IOException>(write.Exception?.InnerException);
        await cc.CloseAsync();
        await sc.CloseAsync();
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
    private async Task TestFileRegionDrainStopsAtCompletion0(ServerBootstrap sb, Bootstrap cb)
    {
        // Region size > 1 so any chunked transport (e.g. io_uring's generic FileRegion fallback)
        // that sizes its chunk buffer from count() retains spare capacity after the first
        // (and only) source byte is written -- without that spare capacity an inner drain loop
        // would exit on writableBytes() == 0 and the overshoot path would not be exercised.
        const int regionSize = 16;

        sb.ChildHandler(new DrainHandler());
        cb.Handler(new ChannelInboundHandlerAdapter());

        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);

        try
        {
            // OioByteStreamChannel#doWriteFileRegion drains using a locally-tracked
            // bytes-written counter as the transferTo position (ignoring transferred()), so an
            // ill-behaved FileRegion that advances transferred() past actual bytes written --
            // the exact pattern this fixture uses to exercise the overshoot path -- cannot
            // satisfy OIO's loop without violating the position invariant. The overshoot
            // detection is meaningless on OIO for the same reason; skip the permutation.
            // CLR: OioSocketChannel is deprecated-not-ported and the harness adds no OIO permutation, so
            // assumeFalse(cc instanceof OioSocketChannel, "OIO transport does not honour transferred() for drain-loop
            // termination") always holds and has no CLR statement.

            OvershootDetectingFileRegion region = new OvershootDetectingFileRegion(regionSize);
            // sync() blocks until the write future completes, by which point every
            // transferTo() call the transport is going to make has already been made --
            // the fixture's call counter is observable here without any timing wait. Ref
            // ownership is transferred to the pipeline on writeAndFlush(), which releases
            // it as the write completes (success or failure).
            await cc.WriteAndFlushAsync(region);
            int overshoot = Volatile.Read(ref region.transferToCallsPastCompletion);
            // CLR: xUnit's Assert.Equal has no message argument, so Assert.True carries the source's message.
            Assert.True(0 == overshoot,
                    "transferTo() invoked " + overshoot
                            + " time(s) after region.transferred() reached region.count()="
                            + regionSize);
        }
        finally
        {
            await cc.CloseAsync();
            await sc.CloseAsync();
        }
    }

    private static async Task TestFileRegion0(
            ServerBootstrap sb, Bootstrap cb, bool voidPromise, bool autoRead, bool defaultFileRegion)
    {
        sb.ChildOption(ChannelOption.AUTO_READ, autoRead);
        cb.Option(ChannelOption.AUTO_READ, autoRead);

        const int bufferSize = 1024;
        FileInfo file = PlatformDependent.CreateTempFile("netty-", ".tmp", null);
        DeleteOnExit(file);

        FileStream @out = new FileStream(file.FullName, FileMode.Create, FileAccess.Write);
        // CLR: Random.Shared replaces ThreadLocalRandom.current(); FileStream.WriteByte((byte) random.Next()) writes
        // the low eight bits, as OutputStream.write(int) does.
        Random random = Random.Shared;

        // Prepend random data which will not be transferred, so that we can test non-zero start offset
        int startOffset = random.Next(8192);

        for (int i = 0; i < startOffset; i ++)
        {
            @out.WriteByte((byte) random.Next());
        }

        // .. and here comes the real data to transfer.
        @out.Write(data, bufferSize, data.Length - bufferSize);

        // .. and then some extra data which is not supposed to be transferred.
        for (int i = random.Next(8192); i > 0; i --)
        {
            @out.WriteByte((byte) random.Next());
        }

        @out.Close();

        IChannelHandler ch = new TestClientHandler(autoRead);
        TestHandler sh = new TestHandler(autoRead);

        sb.ChildHandler(sh);
        cb.Handler(ch);

        IChannel sc = await sb.BindAsync();

        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);
        IFileRegion region = new DefaultFileRegion(
                OpenRead(file), startOffset, data.Length - bufferSize);
        IFileRegion emptyRegion = new DefaultFileRegion(OpenRead(file), 0, 0);

        if (!defaultFileRegion)
        {
            region = new FileRegionWrapper(region);
            emptyRegion = new FileRegionWrapper(emptyRegion);
        }

        // Do write ByteBuf and then FileRegion to ensure that mixed writes work
        // Also, write an empty FileRegion to test if writing an empty FileRegion does not cause any issues.
        //
        // See https://github.com/netty/netty/issues/2769
        //     https://github.com/netty/netty/issues/2964
        // CLR: Channel.voidPromise() has no counterpart because every outbound operation returns its own Task
        // (IChannel.cs); a write with the void promise is a write whose Task is discarded, so the voidPromise branch
        // discards the Tasks and the other branch checks that each write returned its Task.
        if (voidPromise)
        {
            _ = cc.WriteAsync(
                    RandomBufferType(cc.Alloc(), data, 0, bufferSize));
            _ = cc.WriteAsync(emptyRegion);
            _ = cc.WriteAndFlushAsync(region);
        }
        else
        {
            Assert.NotNull(cc.WriteAsync(
                    RandomBufferType(cc.Alloc(), data, 0, bufferSize)));
            Assert.NotNull(cc.WriteAsync(emptyRegion));
            Assert.NotNull(cc.WriteAndFlushAsync(region));
        }

        while (sh.counter < data.Length)
        {
            if (Volatile.Read(ref sh.exception) != null)
            {
                break;
            }

            // CLR: Thread.sleep(50) is an awaited Task.Delay(50), so the asynchronous body does not block a thread.
            await Task.Delay(50);
        }

        await sh.channel.CloseAsync();
        await cc.CloseAsync();
        await sc.CloseAsync();

        if (sh.exception != null && !(sh.exception is IOException))
        {
            throw sh.exception;
        }

        if (sh.exception != null)
        {
            throw sh.exception;
        }

        // Make sure we did not receive more than we expected.
        Assert.Equal(data.Length, sh.counter);
    }

    // CLR: new RandomAccessFile(file, "r").getChannel() is a read-only FileStream, the channel type DefaultFileRegion
    // takes in this port; DefaultFileRegion reads it positionally through RandomAccess.Read, which "Reads a sequence
    // of bytes from given file at given offset" (https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess.read).
    private static FileStream OpenRead(FileInfo file)
    {
        return new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
    }

    // CLR: File.deleteOnExit() "Requests that the file or directory denoted by this abstract pathname be deleted when
    // the virtual machine terminates" (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/io/File.html);
    // the BCL has no such method, so an AppDomain.ProcessExit handler, which "can perform termination activities, such
    // as closing files" (https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.processexit), deletes the file
    // when the test host exits. Like deleteOnExit, a failed deletion is ignored.
    private static void DeleteOnExit(FileInfo file)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        };
    }

    // CLR: the anonymous SimpleChannelInboundHandler<ByteBuf> of testFileRegionCountLargerThenFile.
    private sealed class DropHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            // Just drop the message.
        }
    }

    // CLR: the anonymous SimpleChannelInboundHandler<ByteBuf> of testFileRegionDrainStopsAtCompletion.
    private sealed class DrainHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            // drain
        }
    }

    // CLR: the anonymous SimpleChannelInboundHandler<Object> client handler of testFileRegion0.
    private sealed class TestClientHandler(bool autoRead) : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }

    private class TestHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly bool autoRead;
        internal volatile IChannel channel;
        // CLR: AtomicReference<Throwable>.compareAndSet(null, cause) is Interlocked.CompareExchange on this field.
        internal Exception exception;
        internal volatile int counter;

        internal TestHandler(bool autoRead)
        {
            this.autoRead = autoRead;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;

            if (!autoRead)
            {
                ctx.Read();
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
            byte[] actual = new byte[@in.ReadableBytes];
            @in.ReadBytes(actual);

            int lastIdx = counter;

            for (int i = 0; i < actual.Length; i ++)
            {
                Assert.Equal(data[i + lastIdx], actual[i]);
            }

            counter += actual.Length;
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx,
                Exception cause)
        {
            if (Interlocked.CompareExchange(ref exception, cause, null) == null)
            {
                _ = ctx.CloseAsync();
            }
        }
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
    private sealed class OvershootDetectingFileRegion : AbstractReferenceCounted, IFileRegion
    {
        private readonly long count;
        private long transferred;
        // CLR: the AtomicInteger is an int updated with Interlocked.Increment and read with Volatile.Read.
        internal int transferToCallsPastCompletion;

        internal OvershootDetectingFileRegion(long count)
        {
            // count must be > 1 so the chunk buffer retains writable capacity after the first
            // source byte is written -- otherwise the drain-loop overshoot path is not exercised.
            this.count = ObjectUtil.CheckInRange(count, 2L, long.MaxValue, "count");
        }

        public long Position()
        {
            return 0;
        }

        public long Count()
        {
            return count;
        }

        // CLR: the deprecated transfered() alias is folded into Transferred() in this port's IFileRegion.
        public long Transferred()
        {
            return transferred;
        }

        // CLR: IFileRegion.TransferTo writes into a Stream where FileRegion.transferTo writes into a
        // WritableByteChannel (IFileRegion.cs). Stream.Write has no partial-write count (a write that returns has
        // accepted the byte), so the source's n > 0 branch always applies: transferred advances to count and 1 is
        // returned, as target.write(ByteBuffer.wrap(new byte[] { ... })) returns 1 when the byte is accepted.
        public long TransferTo(Stream target, long position)
        {
            // Per FileRegion's contract, the caller passes transferred() as position. Surface
            // a violation via IOException so the transport's catch (Exception) at the write
            // site routes it to the write future's cause -- a JUnit AssertionError would
            // bypass that catch and wedge the EventLoop.
            if (position != transferred)
            {
                throw new IOException("transferTo position " + position + " != transferred() "
                        + transferred);
            }

            if (transferred < count)
            {
                target.WriteByte(0x42);
                transferred = count;
                return 1;
            }

            Interlocked.Increment(ref transferToCallsPastCompletion);
            target.WriteByte(0xFF);
            return 1;
        }

        protected override void Deallocate()
        {
            // No native resources to release. The overshoot counter is observed on the
            // writer thread immediately after sync() returns; nothing else needs cleanup.
        }

        public new IFileRegion Retain()
        {
            base.Retain();
            return this;
        }

        public override IFileRegion Retain(int increment)
        {
            base.Retain(increment);
            return this;
        }

        public new IFileRegion Touch()
        {
            return this;
        }

        public override IFileRegion Touch(object hint)
        {
            return this;
        }
    }

    private sealed class FileRegionWrapper : IFileRegion
    {
        private readonly IFileRegion region;

        internal FileRegionWrapper(IFileRegion region)
        {
            this.region = region;
        }

        public int RefCnt => region.RefCnt;

        public long Position()
        {
            return region.Position();
        }

        // CLR: the deprecated transfered() override is folded into Transferred() in this port's IFileRegion.

        public bool Release()
        {
            return region.Release();
        }

        public long Transferred()
        {
            return region.Transferred();
        }

        public long Count()
        {
            return region.Count();
        }

        public bool Release(int decrement)
        {
            return region.Release(decrement);
        }

        public long TransferTo(Stream target, long position)
        {
            return region.TransferTo(target, position);
        }

        public IFileRegion Retain()
        {
            region.Retain();
            return this;
        }

        public IFileRegion Retain(int increment)
        {
            region.Retain(increment);
            return this;
        }

        public IFileRegion Touch()
        {
            region.Touch();
            return this;
        }

        public IFileRegion Touch(object hint)
        {
            region.Touch(hint);
            return this;
        }

        // CLR: IFileRegion narrows the IReferenceCounted return types with new members, so the inherited interface
        // members are implemented explicitly and forward to the IFileRegion overloads.
        IReferenceCounted IReferenceCounted.Retain()
        {
            return Retain();
        }

        IReferenceCounted IReferenceCounted.Retain(int increment)
        {
            return Retain(increment);
        }

        IReferenceCounted IReferenceCounted.Touch()
        {
            return Touch();
        }

        IReferenceCounted IReferenceCounted.Touch(object hint)
        {
            return Touch(hint);
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
