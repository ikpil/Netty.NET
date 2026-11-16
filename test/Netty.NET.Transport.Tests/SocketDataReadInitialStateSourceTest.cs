/*
 * Copyright 2018 The Netty Project
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

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Channels.ChannelOption;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, whose latch waits have no timeout in the source.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls, sleeps and latch waits do not observe the test cancellation.
public class SocketDataReadInitialStateSourceTest : AbstractSocketTest
{
    protected virtual bool Domain => false;

    [Fact(Timeout = 10000)]
    public async Task TestAutoReadOffNoDataReadUntilReadCalled()
    {
        await Run(TestContext.Current, TestAutoReadOffNoDataReadUntilReadCalled0);
    }

    // CLR: xUnit1024 forbids overloading the TestAutoReadOffNoDataReadUntilReadCalled test name.
    private async Task TestAutoReadOffNoDataReadUntilReadCalled0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        int sleepMs = 100;
        try
        {
            sb.Option(AUTO_READ, false);
            sb.ChildOption(AUTO_READ, false);
            cb.Option(AUTO_READ, false);
            // CLR: each CountDownLatch(1) is a TaskCompletionSource: countDown() is TrySetResult(), which returns
            // false instead of failing once the Task is complete, as countDown() does nothing at a zero count
            // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetresult,
            // https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/concurrent/CountDownLatch.html).
            // await() awaits its Task, and getCount() is still 1 exactly while the Task is not completed.
            TaskCompletionSource serverReadyLatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource acceptorReadLatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource serverReadLatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource clientReadLatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            AtomicReference<IChannel> serverConnectedChannelRef = new AtomicReference<IChannel>();

            // CLR: C# has no anonymous classes, so the source's anonymous ChannelInitializer bodies are
            // ActionChannelInitializer delegates (src/Netty.NET.Transport/Bootstrapping/Bootstrap.cs) and its anonymous
            // handlers are the nested classes below, whose constructor parameters carry the captured latches.
            sb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new AcceptorReadHandler(acceptorReadLatch));
            }));

            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                serverConnectedChannelRef.Set(ch);
                ch.Pipeline.AddLast(new EchoReadHandler(serverReadLatch));
                serverReadyLatch.TrySetResult();
            }));

            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new ClientReadHandler(clientReadLatch));
            }));

            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
            await clientChannel.WriteAndFlushAsync(clientChannel.Alloc().Buffer().WriteZero(1));

            // The acceptor shouldn't read any data until we call read() below, but give it some time to see if it will.
            // CLR: Thread.sleep(sleepMs) is an awaited Task.Delay(sleepMs), "a task that completes after a specified
            // number of milliseconds" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.delay).
            await Task.Delay(sleepMs);
            Assert.False(acceptorReadLatch.Task.IsCompleted);
            serverChannel.Read();
            await serverReadyLatch.Task;

            IChannel serverConnectedChannel = serverConnectedChannelRef.Get();
            Assert.NotNull(serverConnectedChannel);

            // Allow some amount of time for the server peer to receive the message (which isn't expected to happen
            // until we call read() below).
            await Task.Delay(sleepMs);
            Assert.False(serverReadLatch.Task.IsCompleted);
            serverConnectedChannel.Read();
            await serverReadLatch.Task;

            // Allow some amount of time for the client to read the echo.
            await Task.Delay(sleepMs);
            Assert.False(clientReadLatch.Task.IsCompleted);
            clientChannel.Read();
            await clientReadLatch.Task;
        }
        finally
        {
            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
            }
            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }
        }
    }

    [Fact(Timeout = 10000)]
    public async Task TestAutoReadOnDataReadImmediately()
    {
        await Run(TestContext.Current, TestAutoReadOnDataReadImmediately0);
    }

    // CLR: xUnit1024 forbids overloading the TestAutoReadOnDataReadImmediately test name.
    private async Task TestAutoReadOnDataReadImmediately0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            sb.Option(AUTO_READ, true);
            sb.ChildOption(AUTO_READ, true);
            cb.Option(AUTO_READ, true);
            TaskCompletionSource serverReadLatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource clientReadLatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new EchoReadHandler(serverReadLatch));
            }));

            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new ClientReadHandler(clientReadLatch));
            }));

            serverChannel = await sb.BindAsync();
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
            await clientChannel.WriteAndFlushAsync(clientChannel.Alloc().Buffer().WriteZero(1));
            await serverReadLatch.Task;
            await clientReadLatch.Task;
        }
        finally
        {
            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
            }
            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }
        }
    }

    private sealed class AcceptorReadHandler(TaskCompletionSource acceptorReadLatch) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            acceptorReadLatch.TrySetResult();
            ctx.FireChannelRead(msg);
        }
    }

    private sealed class EchoReadHandler(TaskCompletionSource serverReadLatch) : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            _ = ctx.WriteAndFlushAsync(msg.RetainedDuplicate());
            serverReadLatch.TrySetResult();
        }
    }

    private sealed class ClientReadHandler(TaskCompletionSource clientReadLatch) : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            clientReadLatch.TrySetResult();
        }
    }

    // CLR: the DomainSocketDataReadInitialStateSourceTest subclass is the EpollDomainSocketDataReadInitialStateTest
    // counterpart; Java overrides newFactories()/newSocketAddress() there, this port selects the same overrides
    // through the Domain hook.
    protected override List<TestsuitePermutation.IBootstrapComboFactory<ServerBootstrap, Bootstrap>> NewFactories()
    {
        if (!Domain)
        {
            return base.NewFactories();
        }
        DomainSocketSourceTest.RequireDomainSockets();
        return DomainSocketTestPermutation.INSTANCE.DomainSocket();
    }

    protected override EndPoint NewSocketAddress() =>
        Domain ? DomainSocketTestPermutation.NewDomainSocketAddress() : base.NewSocketAddress();
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
