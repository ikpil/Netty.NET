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
public class LocalTransportThreadModel3SourceTest
{
    [Fact(Timeout = 60000, Skip = "regression test")]
    public async Task TestConcurrentAddRemoveInboundEventsMultiple()
    {
        for (int i = 0; i < 50; i++)
            await RunConcurrentAddRemoveAsync(true, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 60000, Skip = "regression test")]
    public async Task TestConcurrentAddRemoveOutboundEventsMultiple()
    {
        for (int i = 0; i < 50; i++)
            await RunConcurrentAddRemoveAsync(false, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000, Skip = "needs a fix")]
    public Task TestConcurrentAddRemoveInboundEvents()
    {
        return RunConcurrentAddRemoveAsync(true, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000, Skip = "needs a fix")]
    public Task TestConcurrentAddRemoveOutboundEvents()
    {
        return RunConcurrentAddRemoveAsync(false, TestContext.Current.CancellationToken);
    }

    private static async Task RunConcurrentAddRemoveAsync(bool inbound, CancellationToken token)
    {
        var servers = new MultiThreadIoEventLoopGroup(4, LocalIoHandler.NewFactory());
        var clients = new MultiThreadIoEventLoopGroup(4, new DefaultThreadFactory("l"), LocalIoHandler.NewFactory());
        var executors = Enumerable.Range(1, 5)
            .Select(i => new DefaultEventExecutorGroup(4, new DefaultThreadFactory($"e{i}"))).ToArray();
        using var stopModifier = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task modifier = Task.CompletedTask;

        try
        {
            // Configure a test server
            var server = await new ServerBootstrap().Group(servers).Channel<LocalServerChannel>()
                .ChildHandler(new DiscardInput()).BindAsync(LocalAddress.ANY, token);
            var channel = new LocalChannel();

            if (!inbound)
            {
                channel.Config.SetAutoRead(false);
            }

            for (int i = 0; i < 5; i++)
                channel.Pipeline.AddLast(executors[0], $"h{i + 1}", new EventForwarder());

            var recorder = new EventRecorder(inbound);
            channel.Pipeline.AddLast(executors[0], "recorder", recorder);
            await clients.RegisterAsync(channel, token);
            await channel.ConnectAsync(server.LocalAddress, cancellationToken: token);

            var choices = inbound
                ? new[] { EventType.UserEvent, EventType.MessageReceived, EventType.MessageReceivedLast, EventType.ExceptionCaught }
                : new[] { EventType.Read, EventType.Write, EventType.ExceptionCaught };
            var expected = Enumerable.Range(0, 8192).Select(_ => choices[Random.Shared.Next(choices.Length)]).ToArray();
            var cause = new Exception();
            modifier = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(100, stopModifier.Token);
                    if (!channel.IsRegistered)
                    {
                        continue;
                    }

                    //EventForwardHandler forwardHandler = forwarders[random.nextInt(forwarders.length)];
                    var handler = channel.Pipeline.RemoveFirst();
                    channel.Pipeline.AddBefore(executors[Random.Shared.Next(executors.Length)], "recorder", Guid.NewGuid().ToString(), handler);
                }
            }, stopModifier.Token);

            foreach (var evt in expected)
            {
                switch (evt)
                {
                    case EventType.ExceptionCaught:
                        channel.Pipeline.FireExceptionCaught(cause);
                        break;
                    case EventType.MessageReceived:
                        channel.Pipeline.FireChannelRead("");
                        break;
                    case EventType.MessageReceivedLast:
                        channel.Pipeline.FireChannelReadComplete();
                        break;
                    case EventType.UserEvent:
                        channel.Pipeline.FireUserEventTriggered("");
                        break;
                    case EventType.Write:
                        _ = channel.Pipeline.WriteAsync("");
                        break;
                    case EventType.Read:
                        channel.Pipeline.Read();
                        break;
                }
            }

            await channel.CloseAsync(token);
            await recorder.Unregistered.Task.WaitAsync(token);
            Assert.Equal(new[] { EventType.Registered, EventType.Active }.Concat(expected)
                .Concat(new[] { EventType.Inactive, EventType.Unregistered }), recorder.Events.ToArray());
        }
        finally
        {
            stopModifier.Cancel();

            try
            {
                await modifier;
            }
            catch (OperationCanceledException) when (stopModifier.IsCancellationRequested)
            {
            }
            finally
            {
                await clients.StopAsync();
                await Task.WhenAll(executors.Select(group => group.StopAsync()).Append(servers.StopAsync()));
            }
        }
    }

    private enum EventType
    {
        ExceptionCaught, UserEvent, MessageReceivedLast, Inactive, Active, Unregistered,
        Registered, MessageReceived, Write, Read
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

    [Sharable]
    private sealed class EventForwarder : ChannelDuplexHandler { }

    private sealed class EventRecorder(bool inbound) : ChannelDuplexHandler
    {
        internal readonly ConcurrentQueue<EventType> Events = new();
        internal readonly TaskCompletionSource Unregistered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Events.Enqueue(EventType.ExceptionCaught);
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (inbound)
                Events.Enqueue(EventType.UserEvent);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (inbound)
                Events.Enqueue(EventType.MessageReceivedLast);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            Events.Enqueue(EventType.Inactive);
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            Events.Enqueue(EventType.Active);
        }

        public override void ChannelUnregistered(IChannelHandlerContext ctx)
        {
            Events.Enqueue(EventType.Unregistered);
            Unregistered.TrySetResult();
        }

        public override void ChannelRegistered(IChannelHandlerContext ctx)
        {
            Events.Enqueue(EventType.Registered);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (inbound)
                Events.Enqueue(EventType.MessageReceived);
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken token)
        {
            if (!inbound)
            {
                Events.Enqueue(EventType.Write);
            }

            return Task.CompletedTask;
        }

        public override void Read(IChannelHandlerContext ctx)
        {
            if (!inbound)
                Events.Enqueue(EventType.Read);
        }
    }
}
