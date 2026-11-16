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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, whose polling loops have no timeout in the source.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls and polling sleeps do not observe the test cancellation.
public class SocketEchoSourceTest : AbstractSocketTest, IClassFixture<SocketEchoSourceTest.GroupFixture>
{

    private static readonly Random random = new Random();
    internal static readonly byte[] data = new byte[1048576];

    private static IEventExecutorGroup group;

    static SocketEchoSourceTest()
    {
        // CLR: PlatformDependent.splittableRandomNextBytes(new SplittableRandom(random.nextLong()), data) fills data
        // from a generator seeded by the shared random; SplittableRandom is not ported, so a Random created "using the
        // specified seed value" (https://learn.microsoft.com/en-us/dotnet/api/system.random.-ctor) fills it through
        // NextBytes, which "Fills the elements of a specified array of bytes with random numbers"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.random.nextbytes).
        new Random(random.Next()).NextBytes(data);
    }

    // CLR: JUnit runs the static @BeforeAll/@AfterAll methods once around all tests of the class. xUnit v3 creates a
    // class fixture of a test class that implements IClassFixture<> "Just before the first test" and, "once all the
    // tests have finished", cleans it up "by calling Dispose" (https://xunit.net/docs/shared-context), so this
    // fixture calls createGroup() and destroyGroup() at those points.
    public sealed class GroupFixture : IDisposable
    {
        public GroupFixture()
        {
            CreateGroup();
        }

        public void Dispose()
        {
            DestroyGroup();
        }
    }

    // CLR: internal instead of the source's public, because xUnit1013 reports public non-test methods on a test class.
    internal static void CreateGroup()
    {
        group = new DefaultEventExecutorGroup(2);
    }

    internal static void DestroyGroup()
    {
        // CLR: shutdownGracefully().sync() blocks until the group terminated; xUnit calls the fixture's Dispose()
        // synchronously, so the shutdown Task is waited for there.
        group.ShutdownGracefullyAsync().GetAwaiter().GetResult();
    }

    [Fact(Timeout = 30000)]
    public async Task TestSimpleEcho()
    {
        await Run(TestContext.Current, TestSimpleEcho0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleEcho test name.
    private async Task TestSimpleEcho0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestSimpleEcho0(sb, cb, false, true);
    }

    [Fact(Timeout = 30000)]
    public async Task TestSimpleEchoNotAutoRead()
    {
        await Run(TestContext.Current, TestSimpleEchoNotAutoRead0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleEchoNotAutoRead test name.
    private async Task TestSimpleEchoNotAutoRead0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestSimpleEcho0(sb, cb, false, false);
    }

    [Fact]
    public async Task TestSimpleEchoWithAdditionalExecutor()
    {
        await Run(TestContext.Current, TestSimpleEchoWithAdditionalExecutor0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleEchoWithAdditionalExecutor test name.
    private async Task TestSimpleEchoWithAdditionalExecutor0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestSimpleEcho0(sb, cb, true, true);
    }

    [Fact]
    public async Task TestSimpleEchoWithAdditionalExecutorNotAutoRead()
    {
        await Run(TestContext.Current, TestSimpleEchoWithAdditionalExecutorNotAutoRead0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleEchoWithAdditionalExecutorNotAutoRead test name.
    private async Task TestSimpleEchoWithAdditionalExecutorNotAutoRead0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestSimpleEcho0(sb, cb, true, false);
    }

    // CLR: testSimpleEchoWithVoidPromise, testSimpleEchoWithVoidPromiseNotAutoRead and
    // testSimpleEchoWithAdditionalExecutorAndVoidPromise are clr-not-applicable (transport porting manifest): they
    // write with cc.voidPromise() and assert that writeAndFlush returns that same VoidPromise. CLR writes return a
    // Task and take no caller-supplied completion object, so there is no VoidPromise identity to assert, and
    // testSimpleEcho0's voidPromise parameter is not ported.

    // CLR: supplemental AF_UNIX coverage that the source does not have: the additional-executor echo body runs over
    // DomainSocketTestPermutation.DomainSocket() for both AUTO_READ settings, with both harness allocators.
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DomainEchoWithAdditionalExecutor(bool autoRead)
    {
        DomainSocketSourceTest.RequireDomainSockets();
        domain = true;
        await Run(TestContext.Current, (sb, cb) => TestSimpleEcho0(sb, cb, true, autoRead));
    }

    private bool domain;

    protected override List<IBootstrapComboFactory<ServerBootstrap, Bootstrap>> NewFactories()
    {
        return domain ? DomainSocketTestPermutation.INSTANCE.DomainSocket() : base.NewFactories();
    }

    protected override EndPoint NewSocketAddress()
    {
        return domain ? DomainSocketTestPermutation.NewDomainSocketAddress() : base.NewSocketAddress();
    }

    private static async Task TestSimpleEcho0(
            ServerBootstrap sb, Bootstrap cb, bool additionalExecutor, bool autoRead)
    {

        EchoHandler sh = new EchoHandler(autoRead);
        EchoHandler ch = new EchoHandler(autoRead);

        if (additionalExecutor)
        {
            // CLR: C# has no anonymous classes, so the source's anonymous ChannelInitializer bodies are
            // ActionChannelInitializer delegates (src/Netty.NET.Transport/Bootstrapping/Bootstrap.cs). This port's
            // pipeline has no addLast(EventExecutorGroup, ChannelHandler...) overload; the source's overload calls
            // addLast(executor, null, h) for each handler (transport/src/main/java/io/netty/channel/
            // DefaultChannelPipeline.java), which AddLast(group, null, handler) does here.
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(c =>
            {
                c.Pipeline.AddLast(group, null, sh);
            }));
            cb.Handler(new ActionChannelInitializer<IChannel>(c =>
            {
                c.Pipeline.AddLast(group, null, ch);
            }));
        }
        else
        {
            sb.ChildHandler(sh);
            sb.Handler(new StackTracePrintingHandler());
            cb.Handler(ch);
        }
        sb.ChildOption(ChannelOption.AUTO_READ, autoRead);
        cb.Option(ChannelOption.AUTO_READ, autoRead);

        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);

        // CLR: new SplittableRandom(random.nextLong()) is a Random created with a seed from the shared random
        // (https://learn.microsoft.com/en-us/dotnet/api/system.random.-ctor); Next(maxValue) keeps nextInt(bound)'s
        // exclusive upper bound.
        Random rng = new Random(random.Next());
        for (int i = 0; i < data.Length;)
        {
            int length = Math.Min(rng.Next(1024 * 64), data.Length - i);
            ByteBuf buf = RandomBufferType(cc.Alloc(), data, i, length);
            // CLR: the source asserts assertNotEquals(cc.voidPromise(), cc.writeAndFlush(buf)) here and never waits
            // for the write; a CLR write always returns its own Task, which is not observed either.
            _ = cc.WriteAndFlushAsync(buf);
            i += length;
        }

        // CLR: Thread.sleep(50) is an awaited Task.Delay(50), "a task that completes after a specified number of
        // milliseconds" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.delay).
        while (ch.counter < data.Length)
        {
            if (sh.exception.Get() != null)
            {
                break;
            }
            if (ch.exception.Get() != null)
            {
                break;
            }

            await Task.Delay(50);
        }

        while (sh.counter < data.Length)
        {
            if (sh.exception.Get() != null)
            {
                break;
            }
            if (ch.exception.Get() != null)
            {
                break;
            }

            await Task.Delay(50);
        }

        await sh.channel.CloseAsync();
        await ch.channel.CloseAsync();
        await sc.CloseAsync();

        if (sh.exception.Get() != null && !(sh.exception.Get() is IOException))
        {
            throw sh.exception.Get();
        }
        if (ch.exception.Get() != null && !(ch.exception.Get() is IOException))
        {
            throw ch.exception.Get();
        }
        if (sh.exception.Get() != null)
        {
            throw sh.exception.Get();
        }
        if (ch.exception.Get() != null)
        {
            throw ch.exception.Get();
        }
    }

    // CLR: C# has no anonymous classes; this is the source's anonymous server-channel ChannelInboundHandlerAdapter.
    private sealed class StackTracePrintingHandler : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            PrintStackTrace(cause);
        }
    }

    // CLR: Throwable.printStackTrace() "Prints this throwable and its backtrace to the standard error stream"
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Throwable.html). Console.Error "Gets
    // the standard error output stream" (https://learn.microsoft.com/en-us/dotnet/api/system.console.error), and
    // Exception.ToString() includes the message, inner exception and stack trace
    // (https://learn.microsoft.com/en-us/dotnet/api/system.exception.tostring).
    private static void PrintStackTrace(Exception cause)
    {
        Console.Error.WriteLine(cause);
    }

    private class EchoHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly bool autoRead;
        internal volatile IChannel channel;
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();
        internal volatile int counter;

        internal EchoHandler(bool autoRead)
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

            // Update the counter before calling write(...) as write could in theory trigger another channelRead(...)
            // which then would use the wrong lastIdx.
            counter += actual.Length;

            if (channel.Parent != null)
            {
                _ = channel.WriteAsync(RandomBufferType(channel.Alloc(), actual, 0, actual.Length));
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            try
            {
                ctx.Flush();
            }
            finally
            {
                if (!autoRead)
                {
                    ctx.Read();
                }
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx,
                Exception cause)
        {
            if (exception.CompareAndSet(null, cause))
            {
                PrintStackTrace(cause);
                _ = ctx.CloseAsync();
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
