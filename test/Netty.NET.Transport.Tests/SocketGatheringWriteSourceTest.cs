/*
 * Copyright 2013 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source waits for and closes its channels independently of the test cancellation.
public class SocketGatheringWriteSourceTest : AbstractSocketTest
{
    // CLR: xUnit's Fact.Timeout is an int number of milliseconds, so the source's long constant is an int constant.
    private const int TIMEOUT = 120000;

    // CLR: Random.Shared ("a thread-safe Random instance that may be used concurrently from any thread",
    // https://learn.microsoft.com/en-us/dotnet/api/system.random.shared) replaces the shared java.util.Random, whose
    // instances are threadsafe (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Random.html);
    // testGatheringWriteSameEventLoop draws from it on an event loop.
    private static readonly Random random = Random.Shared;
    internal static readonly byte[] data = new byte[1048576];

    static SocketGatheringWriteSourceTest()
    {
        // CLR: a System.Random seeded with random.Next() replaces new SplittableRandom(random.nextLong()) (the
        // Random(int Seed) constructor takes a 32-bit seed), and
        // Random.NextBytes ("Fills the elements of a specified array of bytes with random numbers",
        // https://learn.microsoft.com/en-us/dotnet/api/system.random.nextbytes) replaces
        // PlatformDependent.splittableRandomNextBytes.
        new Random(random.Next()).NextBytes(data);
    }

    // CLR: the source's @AfterAll compressHeapDumps() calls TestUtils.compressHeapDumps(), which xz-compresses the
    // *.hprof files written by TestUtils.dump(...) through HotSpotDiagnosticMXBean.dumpHeap ("Dumps the heap to the
    // outputFile file in the same format as the hprof heap dump",
    // https://docs.oracle.com/en/java/javase/21/docs/api/jdk.management/com/sun/management/HotSpotDiagnosticMXBean.html).
    // That bean is the "Diagnostic management interface for the HotSpot Virtual Machine"; a .NET process dump is
    // collected from outside the process with dotnet-dump instead ("a way to collect and analyze dumps on Windows,
    // Linux, and macOS without any native debugger involved",
    // https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump). This port's TestUtils therefore only
    // keeps testMethodName(TestInfo) (see TestUtils.cs), so neither the dump nor this @AfterAll compression step is
    // ported and no xUnit class fixture replaces it.

    [Fact(Timeout = TIMEOUT)]
    public async Task TestGatheringWrite()
    {
        await Run(TestContext.Current, TestGatheringWrite0);
    }

    // CLR: xUnit1024 forbids overloading the TestGatheringWrite test name.
    private async Task TestGatheringWrite0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestGatheringWrite0(sb, cb, data, false, true);
    }

    [Fact(Timeout = TIMEOUT)]
    public async Task TestGatheringWriteNotAutoRead()
    {
        await Run(TestContext.Current, TestGatheringWriteNotAutoRead0);
    }

    // CLR: xUnit1024 forbids overloading the TestGatheringWriteNotAutoRead test name.
    private async Task TestGatheringWriteNotAutoRead0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestGatheringWrite0(sb, cb, data, false, false);
    }

    [Fact(Timeout = TIMEOUT)]
    public async Task TestGatheringWriteWithComposite()
    {
        await Run(TestContext.Current, TestGatheringWriteWithComposite0);
    }

    // CLR: xUnit1024 forbids overloading the TestGatheringWriteWithComposite test name.
    private async Task TestGatheringWriteWithComposite0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestGatheringWrite0(sb, cb, data, true, true);
    }

    [Fact(Timeout = TIMEOUT)]
    public async Task TestGatheringWriteWithCompositeNotAutoRead()
    {
        await Run(TestContext.Current, TestGatheringWriteWithCompositeNotAutoRead0);
    }

    // CLR: xUnit1024 forbids overloading the TestGatheringWriteWithCompositeNotAutoRead test name.
    private async Task TestGatheringWriteWithCompositeNotAutoRead0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestGatheringWrite0(sb, cb, data, true, false);
    }

    // Test for https://github.com/netty/netty/issues/2647
    [Fact(Timeout = TIMEOUT)]
    public async Task TestGatheringWriteBig()
    {
        await Run(TestContext.Current, TestGatheringWriteBig0);
    }

    // CLR: xUnit1024 forbids overloading the TestGatheringWriteBig test name.
    private async Task TestGatheringWriteBig0(ServerBootstrap sb, Bootstrap cb)
    {
        // CLR: SplittableRandom(random.nextLong()) and splittableRandomNextBytes are replaced as for data above.
        Random rng = new Random(random.Next());
        byte[] bigData = new byte[1024 * 1024 * 50];
        rng.NextBytes(bigData);
        await TestGatheringWrite0(sb, cb, bigData, false, true);
    }

    private async Task TestGatheringWrite0(
            ServerBootstrap sb, Bootstrap cb, byte[] data, bool composite, bool autoRead)
    {
        sb.ChildOption(ChannelOption.AUTO_READ, autoRead);
        cb.Option(ChannelOption.AUTO_READ, autoRead);

        // CLR: ImmediateEventExecutor.INSTANCE.newPromise() is a TaskCompletionSource; this port completes Tasks
        // instead of exposing promises (IChannel.cs: "Tasks replace ChannelFuture/Promise").
        TaskCompletionSource serverDonePromise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TestServerHandler sh = new TestServerHandler(autoRead, serverDonePromise, data.Length);
        TestHandler ch = new TestHandler(autoRead);

        cb.Handler(ch);
        sb.ChildHandler(sh);

        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);

        // CLR: a System.Random seeded from random replaces new SplittableRandom(random.nextLong()).
        Random rng = new Random(random.Next());
        for (int i = 0; i < data.Length;)
        {
            int length = Math.Min(rng.Next(1024 * 8), data.Length - i);
            if (composite && i % 2 == 0)
            {
                int firstBufLength = length / 2;
                CompositeByteBuf comp = Unpooled.CompositeBuffer();
                comp.AddComponent(
                                RandomBufferType(cc.Alloc(), data, i, firstBufLength), true)
                    .AddComponent(
                            RandomBufferType(cc.Alloc(), data, i + firstBufLength, length - firstBufLength), true);
                _ = cc.WriteAsync(comp);
            }
            else
            {
                _ = cc.WriteAsync(RandomBufferType(cc.Alloc(), data, i, length));
            }
            i += length;
        }

        Task cf = cc.WriteAndFlushAsync(Unpooled.EmptyBuffer);
        // CLR: Channel.voidPromise() has no counterpart because every outbound operation returns its own Task
        // (IChannel.cs), so assertNotEquals(cc.voidPromise(), cf) checks that writeAndFlush returned that Task.
        Assert.NotNull(cf);
        try
        {
            Assert.True(await AwaitAsync(cf, 60000));
            await cf;
        }
        catch (Exception)
        {
            // TODO: Remove this once we fix this test.
            // CLR: TestUtils.dump(StringUtil.simpleClassName(this)) writes the JVM heap and thread dumps described at
            // compressHeapDumps above; it is not ported, so the failure is rethrown without a dump.
            throw;
        }

        await serverDonePromise.Task;
        await sh.channel.CloseAsync();
        await ch.channel.CloseAsync();
        await sc.CloseAsync();

        if (sh.exception != null && !(sh.exception is IOException))
        {
            throw sh.exception;
        }
        if (sh.exception != null)
        {
            throw sh.exception;
        }
        if (ch.exception != null && !(ch.exception is IOException))
        {
            throw ch.exception;
        }
        if (ch.exception != null)
        {
            throw ch.exception;
        }
        ByteBuf expected = Unpooled.WrappedBuffer(data);
        Assert.Equal(expected, sh.received);
        expected.Release();
        sh.received.Release();
    }

    [Fact(Timeout = 30000)]
    public async Task TestGatheringWriteSameEventLoop()
    {
        await Run(TestContext.Current, TestGatheringWriteSameEventLoop0);
    }

    // CLR: xUnit1024 forbids overloading the TestGatheringWriteSameEventLoop test name.
    private async Task TestGatheringWriteSameEventLoop0(ServerBootstrap sb, Bootstrap cb)
    {
        // Ensure all clients are on the same EventLoop.
        try
        {
            cb = cb.Clone(cb.Config.Group.Next());
        }
        catch (NotSupportedException)
        {
            // CLR: NotSupportedException ("thrown when an invoked method is not supported",
            // https://learn.microsoft.com/en-us/dotnet/api/system.notsupportedexception) is the
            // UnsupportedOperationException counterpart, and xUnit v3's Assert.Skip ("support dynamically skipping a
            // test at runtime", https://xunit.net/docs/getting-started/v3/whats-new) replaces TestAbortedException; it
            // takes no cause.
            Assert.Skip("Not supported by this EventLoopGroup: " + cb.Config.Group);
        }

        // CLR: AtomicInteger.getAndIncrement() is Interlocked.Increment(ref ...) - 1 on these fields.
        int sHandlersIdx = 0;
        int cHandlersIdx = 0;
        TestServerHandler[] sHandlers = new TestServerHandler[] {
                new TestServerHandler(true, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), data.Length),
                new TestServerHandler(true, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), data.Length)
        };
        TestHandler[] cHandlers = new TestHandler[] {
                new TestHandler(true),
                new TestHandler(true)
        };

        // CLR: ActionChannelInitializer carries the initChannel body of the source's anonymous ChannelInitializer.
        cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
                ch.Pipeline.AddLast(cHandlers[Interlocked.Increment(ref cHandlersIdx) - 1])));

        sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
                ch.Pipeline.AddLast(sHandlers[Interlocked.Increment(ref sHandlersIdx) - 1])));

        IChannel sc = await sb.BindAsync();
        IChannel cc1 = await cb.ConnectAsync(sc.LocalAddress);
        IChannel cc2 = await cb.ConnectAsync(sc.LocalAddress);

        Assert.Same(cc1.EventLoop, cc2.EventLoop);
        // CLR: newPromise() plus writeAndFlush(msg, promise) become TaskCompletionSources created here and completed
        // from the Tasks that WriteAndFlushAsync returns, because outbound operations return Tasks instead of
        // accepting a caller-supplied promise (IChannel.cs).
        TaskCompletionSource p1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource p2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cc1.EventLoop.Execute(() =>
        {
            Random rng = new Random(random.Next());
            for (int i = 0; i < data.Length;)
            {
                int length = Math.Min(rng.Next(1024 * 8), data.Length - i);
                _ = cc1.WriteAsync(RandomBufferType(cc1.Alloc(), data, i, length));
                _ = cc2.WriteAsync(RandomBufferType(cc2.Alloc(), data, i, length));
                i += length;
            }
            _ = Cascade(cc1.WriteAndFlushAsync(Unpooled.EmptyBuffer), p1);
            _ = Cascade(cc2.WriteAndFlushAsync(Unpooled.EmptyBuffer), p2);
        });

        Assert.True(await AwaitAsync(p1.Task, 60000));
        await p1.Task;
        Assert.True(await AwaitAsync(p2.Task, 60000));
        await p2.Task;

        for (int i = 0; i < sHandlers.Length; i++)
        {
            TestServerHandler sh = sHandlers[i];
            TestHandler ch = cHandlers[i];
            await sh.doneReadingPromise.Task;
            await sh.channel.CloseAsync();
            await ch.channel.CloseAsync();

            if (sh.exception != null && !(sh.exception is IOException))
            {
                throw sh.exception;
            }
            if (sh.exception != null)
            {
                throw sh.exception;
            }
            if (ch.exception != null && !(ch.exception is IOException))
            {
                throw ch.exception;
            }
            if (ch.exception != null)
            {
                throw ch.exception;
            }
            ByteBuf expected = Unpooled.WrappedBuffer(data);
            Assert.Equal(expected, sh.received);
            expected.Release();
            sh.received.Release();
        }
        await sc.CloseAsync();
    }

    // CLR: Future.await(long timeoutMillis) waits without rethrowing the cause and reports whether the future
    // completed in time. Task.WhenAny "always ends in the RanToCompletion state with its Result set to the first task
    // to complete" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenany), so racing the
    // Task against a delay gives the same answer; the delay is cancelled once the race is decided.
    private static async Task<bool> AwaitAsync(Task future, long timeoutMillis)
    {
        using CancellationTokenSource delayCancellation = new CancellationTokenSource();
        Task completed = await Task.WhenAny(future, Task.Delay(TimeSpan.FromMilliseconds(timeoutMillis), delayCancellation.Token));
        delayCancellation.Cancel();
        return completed == future;
    }

    // CLR: completes the up-front promise with the outcome of the write Task, as the promise passed to
    // writeAndFlush(msg, promise) would have been completed.
    private static async Task Cascade(Task future, TaskCompletionSource promise)
    {
        try
        {
            await future.ConfigureAwait(false);
            promise.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            promise.TrySetCanceled();
        }
        catch (Exception cause)
        {
            promise.TrySetException(cause);
        }
    }

    private sealed class TestServerHandler : TestHandler
    {
        private readonly int expectedBytes;
        internal readonly TaskCompletionSource doneReadingPromise;
        internal readonly ByteBuf received = Unpooled.Buffer();

        internal TestServerHandler(bool autoRead, TaskCompletionSource doneReadingPromise, int expectedBytes) : base(autoRead)
        {
            this.doneReadingPromise = doneReadingPromise;
            this.expectedBytes = expectedBytes;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
            received.WriteBytes(@in);
            if (received.ReadableBytes >= expectedBytes)
            {
                // CLR: SetResult throws when already completed, as Promise.setSuccess(null) does.
                doneReadingPromise.SetResult();
            }
        }

        internal override void HandleException(IChannelHandlerContext ctx, Exception cause)
        {
            doneReadingPromise.TrySetException(cause);
            base.HandleException(ctx, cause);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            // CLR: InvalidOperationException ("thrown when a method call is invalid for the object's current state",
            // https://learn.microsoft.com/en-us/dotnet/api/system.invalidoperationexception) is the
            // IllegalStateException counterpart.
            doneReadingPromise.TrySetException(new InvalidOperationException("server closed!"));
            base.ChannelInactive(ctx);
        }
    }

    private class TestHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly bool autoRead;
        internal volatile IChannel channel;
        // CLR: AtomicReference<Throwable>.compareAndSet(null, cause) is Interlocked.CompareExchange on this field.
        internal Exception exception;

        internal TestHandler(bool autoRead)
        {
            this.autoRead = autoRead;
        }

        public sealed override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;
            if (!autoRead)
            {
                ctx.Read();
            }
            base.ChannelActive(ctx);
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
        }

        public sealed override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
            base.ChannelReadComplete(ctx);
        }

        public sealed override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (Interlocked.CompareExchange(ref exception, cause, null) == null)
            {
                HandleException(ctx, cause);
            }
            base.ExceptionCaught(ctx, cause);
        }

        internal virtual void HandleException(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
