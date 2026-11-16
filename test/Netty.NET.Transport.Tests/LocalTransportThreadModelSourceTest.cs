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
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent teardown.
public partial class LocalTransportThreadModelSourceTest
{
    [Fact(Timeout = 5000)]
    public Task TestStagedExecution() => RunStagedExecutionAsync(TestContext.Current.CancellationToken);

    [Fact(Timeout = 30000, Skip = "regression test")]
    public async Task TestStagedExecutionMultiple()
    {
        for (int i = 0; i < 10; i++)
            await RunStagedExecutionAsync(TestContext.Current.CancellationToken);
    }

    private static async Task RunStagedExecutionAsync(CancellationToken token)
    {
        var servers = new MultiThreadIoEventLoopGroup(4, LocalIoHandler.NewFactory());
        var clients = new MultiThreadIoEventLoopGroup(4, new DefaultThreadFactory("l"), LocalIoHandler.NewFactory());
        var first = new DefaultEventExecutorGroup(4, new DefaultThreadFactory("e1"));
        var second = new DefaultEventExecutorGroup(4, new DefaultThreadFactory("e2"));
        try
        {
            // Configure a test server
            var server = await new ServerBootstrap().Group(servers).Channel<LocalServerChannel>()
                .ChildHandler(new DiscardInput()).BindAsync(LocalAddress.ANY, token);
            var h1 = new ThreadAuditor();
            var h2 = new ThreadAuditor();
            var h3 = new ThreadAuditor(discard: true);
            var channel = new LocalChannel();
            // With no EventExecutor specified, h1 will be always invoked by EventLoop 'l'.
            channel.Pipeline.AddLast(h1);
            // h2 will be always invoked by EventExecutor 'e1'.
            channel.Pipeline.AddLast(first, "h2", h2);
            // h3 will be always invoked by EventExecutor 'e2'.
            channel.Pipeline.AddLast(second, "h3", h3);
            await clients.RegisterAsync(channel, token);
            await channel.ConnectAsync(server.LocalAddress, cancellationToken: token);

            var caller = Thread.CurrentThread;
            // Fire inbound events from all possible starting points.
            channel.Pipeline.FireChannelRead("1");
            channel.Pipeline.Context(h1).FireChannelRead("2");
            channel.Pipeline.Context(h2).FireChannelRead("3");
            channel.Pipeline.Context(h3).FireChannelRead("4");
            // Fire outbound events from all possible starting points.
            _ = channel.Pipeline.WriteAsync("5");
            _ = channel.Pipeline.Context(h3).WriteAsync("6");
            _ = channel.Pipeline.Context(h2).WriteAsync("7");
            await channel.Pipeline.Context(h1).WriteAndFlushAsync("8", token).WaitAsync(token);
            await channel.CloseAsync(token);

            // Wait until all events are handled completely.
            while (h1.Outbound.Count < 3 || h3.Inbound.Count < 3 || h1.Removed.Count < 1)
            {
                if ((h1.Error ?? h2.Error ?? h3.Error) is { } error) throw error;
                await Task.Delay(10, token);
            }

            AssertAudit(h1, "l-", caller, 1, 3);
            AssertAudit(h2, "e1-", caller, 2, 2);
            AssertAudit(h3, "e2-", caller, 3, 1);
        }
        finally
        {
            await clients.StopAsync();
            await Task.WhenAll(first.StopAsync(), second.StopAsync(), servers.StopAsync());
        }
    }

    private static void AssertAudit(ThreadAuditor audit, string prefix, Thread caller, int inbound, int outbound)
    {
        var threads = audit.Inbound.Concat(audit.Outbound).Concat(audit.Removed).ToArray();
        // Events should never be handled from the current thread.
        Assert.DoesNotContain(caller, threads);
        // Assert that events were handled by the correct executor.
        Assert.All(threads, thread => Assert.StartsWith(prefix, thread.Name));
        // Assert that the events for the same handler were handled by the same thread.
        Assert.Single(threads.Distinct());
        // Count the number of events
        Assert.Equal(inbound, audit.Inbound.Count);
        Assert.Equal(outbound, audit.Outbound.Count);
        Assert.Single(audit.Removed);
    }

    [Sharable]
    private sealed class DiscardInput : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            // Discard
            ReferenceCountUtil.Release(msg);
        }
    }

    private sealed class ThreadAuditor(bool discard = false) : ChannelDuplexHandler
    {
        internal readonly ConcurrentQueue<Thread> Inbound = new();
        internal readonly ConcurrentQueue<Thread> Outbound = new();
        internal readonly ConcurrentQueue<Thread> Removed = new();
        private Exception _error;
        internal Exception Error => Volatile.Read(ref _error);

        public override void HandlerRemoved(IChannelHandlerContext ctx) => Removed.Enqueue(Thread.CurrentThread);

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            Inbound.Enqueue(Thread.CurrentThread);
            if (!discard) ctx.FireChannelRead(msg);
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken token)
        {
            Outbound.Enqueue(Thread.CurrentThread);
            return ctx.WriteAsync(msg, token);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Interlocked.CompareExchange(ref _error, cause, null);
            ctx.FireExceptionCaught(cause);
        }
    }
}
