/*
 * Copyright 2016 The Netty Project
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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original operation order and independently owned cleanup.
public class CombinedChannelDuplexHandlerSourceTest
{
    private static readonly object Msg = new();
    private static readonly EndPoint LocalAddress = new IPEndPoint(IPAddress.Any, 0);
    private static readonly EndPoint RemoteAddress = new IPEndPoint(IPAddress.Any, 0);
    private static readonly Exception Cause = new();
    private static readonly object UserEvent = new();
    private enum Event
    {
        Registered, Unregistered, Active, Inactive, ChannelRead, ChannelReadComplete, ExceptionCaught,
        UserEventTriggered, ChannelWritabilityChanged, HandlerAdded, HandlerRemoved,
        Bind, Connect, Write, Flush, Read, Register, Deregister, Close, Disconnect
    }
    private sealed class UninitializedCombined : CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler> { }
    private sealed class SharableCombined : CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler>
    {
        public override bool IsSharable => true;
    }
    [Fact]
    public void TestInboundRemoveBeforeAdded()
    {
        var handler = new CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler>(
            new ChannelInboundHandlerAdapter(), new ChannelOutboundHandlerAdapter());
        Assert.Throws<InvalidOperationException>(handler.RemoveInboundHandler);
    }
    [Fact]
    public void TestOutboundRemoveBeforeAdded()
    {
        var handler = new CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler>(
            new ChannelInboundHandlerAdapter(), new ChannelOutboundHandlerAdapter());
        Assert.Throws<InvalidOperationException>(handler.RemoveOutboundHandler);
    }
    [Fact]
    public void TestInboundHandlerImplementsOutboundHandler() => Assert.Throws<ArgumentException>(() =>
        new CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler>(
            new ChannelDuplexHandler(), new ChannelOutboundHandlerAdapter()));
    [Fact]
    public void TestOutboundHandlerImplementsInboundHandler() => Assert.Throws<ArgumentException>(() =>
        new CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler>(
            new ChannelInboundHandlerAdapter(), new ChannelDuplexHandler()));
    [Fact]
    public void TestInitNotCalledBeforeAdded() => Assert.Throws<InvalidOperationException>(() => new UninitializedCombined().HandlerAdded(null));
    [Fact]
    public void TestNotSharable() => Assert.Throws<InvalidOperationException>(() => new SharableCombined());

    private sealed class InboundExceptionHandler(Queue<IChannelHandler> queue, Exception exception, bool forward) : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        {
            Assert.Same(exception, cause);
            queue.Enqueue(this);
            if (forward) context.FireExceptionCaught(cause);
        }
    }
    private sealed class OutboundExceptionHandler(Queue<IChannelHandler> queue, Exception exception) : ChannelOutboundHandlerAdapter
    {
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        {
            Assert.Same(exception, cause);
            queue.Enqueue(this);
            context.FireExceptionCaught(cause);
        }
    }
    [Fact]
    public void TestExceptionCaughtBothCombinedHandlers()
    {
        var exception = new Exception();
        var queue = new Queue<IChannelHandler>();
        var inboundHandler = new InboundExceptionHandler(queue, exception, true);
        var outboundHandler = new OutboundExceptionHandler(queue, exception);
        var lastHandler = new InboundExceptionHandler(queue, exception, false);
        var channel = new EmbeddedChannel(new CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler>(
            inboundHandler, outboundHandler), lastHandler);
        try
        {
            channel.Pipeline.FireExceptionCaught(exception);
            Assert.False(channel.Finish());
            Assert.Same(inboundHandler, queue.Dequeue());
            Assert.Same(outboundHandler, queue.Dequeue());
            Assert.Same(lastHandler, queue.Dequeue());
            Assert.Empty(queue);
        }
        finally { channel.FinishAndReleaseAll(); }
    }
    [Fact]
    public void TestInboundEvents()
    {
        var inboundHandler = new InboundEventHandler();
        var handler = new CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler>(
            inboundHandler, new ChannelOutboundHandlerAdapter());
        var channel = new EmbeddedChannel();
        try
        {
            channel.Pipeline.AddLast(handler);
            Assert.Equal(Event.HandlerAdded, inboundHandler.PollEvent());
            DoInboundOperations(channel);
            foreach (var evt in new[] { Event.Registered, Event.Active, Event.ChannelRead, Event.ChannelReadComplete,
                Event.ExceptionCaught, Event.UserEventTriggered, Event.ChannelWritabilityChanged, Event.Inactive, Event.Unregistered })
                Assert.Equal(evt, inboundHandler.PollEvent());
            handler.RemoveInboundHandler();
            Assert.Equal(Event.HandlerRemoved, inboundHandler.PollEvent());
            // These should not be handled by the inboundHandler anymore as it was removed before
            DoInboundOperations(channel);
            // Should have not received any more events as it was removed before via removeInboundHandler()
            Assert.Null(inboundHandler.PollEvent());
            Assert.Same(Cause, Assert.Throws<Exception>(channel.CheckException));
            Assert.True(channel.Finish());
            Assert.Null(inboundHandler.PollEvent());
        }
        finally { channel.FinishAndReleaseAll(); }
    }
    [Fact]
    public async Task TestOutboundEvents()
    {
        var outboundHandler = new OutboundEventHandler();
        var handler = new CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler>(
            new ChannelInboundHandlerAdapter(), outboundHandler);
        var channel = new EmbeddedChannel();
        try
        {
            channel.Pipeline.AddLast(new OutboundEventHandler());
            channel.Pipeline.AddLast(handler);
            Assert.Equal(Event.HandlerAdded, outboundHandler.PollEvent());
            await DoOutboundOperations(channel);
            foreach (var evt in new[] { Event.Bind, Event.Connect, Event.Write, Event.Flush, Event.Read, Event.Close, Event.Close, Event.Deregister })
                Assert.Equal(evt, outboundHandler.PollEvent());
            handler.RemoveOutboundHandler();
            Assert.Equal(Event.HandlerRemoved, outboundHandler.PollEvent());
            // These should not be handled by the inboundHandler anymore as it was removed before
            await DoOutboundOperations(channel);
            // Should have not received any more events as it was removed before via removeInboundHandler()
            Assert.Null(outboundHandler.PollEvent());
            Assert.False(channel.Finish());
            Assert.Null(outboundHandler.PollEvent());
        }
        finally { channel.FinishAndReleaseAll(); }
    }
    private static async Task DoOutboundOperations(IChannel channel)
    {
        await channel.Pipeline.BindAsync(LocalAddress);
        await channel.Pipeline.ConnectAsync(RemoteAddress, LocalAddress);
        await channel.Pipeline.WriteAsync(Msg);
        channel.Pipeline.Flush();
        channel.Pipeline.Read();
        await channel.Pipeline.DisconnectAsync();
        await channel.Pipeline.CloseAsync();
        await channel.Pipeline.DeregisterAsync();
    }
    [Fact(Timeout = 3000)]
    public async Task TestPromisesPassed()
    {
        var outboundHandler = new OutboundEventHandler();
        var channel = new EmbeddedChannel(outboundHandler,
            new CombinedChannelDuplexHandler<IChannelInboundHandler, IChannelOutboundHandler>(
                new ChannelInboundHandlerAdapter(), new ChannelOutboundHandlerAdapter()));
        try
        {
            var pipeline = channel.Pipeline;
            var timeout = TestContext.Current.CancellationToken;
            await pipeline.BindAsync(LocalAddress).WaitAsync(timeout);
            await pipeline.ConnectAsync(RemoteAddress, LocalAddress).WaitAsync(timeout);
            await pipeline.CloseAsync().WaitAsync(timeout);
            await pipeline.DisconnectAsync().WaitAsync(timeout);
            await pipeline.WriteAsync(Msg).WaitAsync(timeout);
            await pipeline.DeregisterAsync().WaitAsync(timeout);
            channel.Finish();
        }
        finally { channel.FinishAndReleaseAll(); }
    }
    private static void DoInboundOperations(IChannel channel)
    {
        channel.Pipeline.FireChannelRegistered();
        channel.Pipeline.FireChannelActive();
        channel.Pipeline.FireChannelRead(Msg);
        channel.Pipeline.FireChannelReadComplete();
        channel.Pipeline.FireExceptionCaught(Cause);
        channel.Pipeline.FireUserEventTriggered(UserEvent);
        channel.Pipeline.FireChannelWritabilityChanged();
        channel.Pipeline.FireChannelInactive();
        channel.Pipeline.FireChannelUnregistered();
    }
    private sealed class InboundEventHandler : ChannelInboundHandlerAdapter
    {
        private readonly Queue<Event> _queue = new();
        internal Event? PollEvent() => _queue.TryDequeue(out var evt) ? evt : null;
        public override void HandlerAdded(ChannelHandlerContext context) => _queue.Enqueue(Event.HandlerAdded);
        public override void HandlerRemoved(ChannelHandlerContext context) => _queue.Enqueue(Event.HandlerRemoved);
        public override void ChannelRegistered(ChannelHandlerContext context) => _queue.Enqueue(Event.Registered);
        public override void ChannelUnregistered(ChannelHandlerContext context) => _queue.Enqueue(Event.Unregistered);
        public override void ChannelActive(ChannelHandlerContext context) => _queue.Enqueue(Event.Active);
        public override void ChannelInactive(ChannelHandlerContext context) => _queue.Enqueue(Event.Inactive);
        public override void ChannelRead(ChannelHandlerContext context, object message) => _queue.Enqueue(Event.ChannelRead);
        public override void ChannelReadComplete(ChannelHandlerContext context) => _queue.Enqueue(Event.ChannelReadComplete);
        public override void UserEventTriggered(ChannelHandlerContext context, object evt) => _queue.Enqueue(Event.UserEventTriggered);
        public override void ChannelWritabilityChanged(ChannelHandlerContext context) => _queue.Enqueue(Event.ChannelWritabilityChanged);
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => _queue.Enqueue(Event.ExceptionCaught);
    }
    private sealed class OutboundEventHandler : ChannelOutboundHandlerAdapter
    {
        private readonly Queue<Event> _queue = new();
        internal Event? PollEvent() => _queue.TryDequeue(out var evt) ? evt : null;
        public override void HandlerAdded(ChannelHandlerContext context) => _queue.Enqueue(Event.HandlerAdded);
        public override void HandlerRemoved(ChannelHandlerContext context) => _queue.Enqueue(Event.HandlerRemoved);
        public override Task BindAsync(ChannelHandlerContext context, EndPoint localAddress, CancellationToken cancellationToken)
        { Assert.Same(LocalAddress, localAddress); _queue.Enqueue(Event.Bind); return Task.CompletedTask; }
        public override Task ConnectAsync(ChannelHandlerContext context, EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken)
        { Assert.Same(RemoteAddress, remoteAddress); Assert.Same(LocalAddress, localAddress); _queue.Enqueue(Event.Connect); return Task.CompletedTask; }
        public override Task DisconnectAsync(ChannelHandlerContext context, CancellationToken cancellationToken)
        {  _queue.Enqueue(Event.Disconnect); return Task.CompletedTask; }
        public override Task CloseAsync(ChannelHandlerContext context, CancellationToken cancellationToken)
        {  _queue.Enqueue(Event.Close); return Task.CompletedTask; }
        public override Task DeregisterAsync(ChannelHandlerContext context, CancellationToken cancellationToken)
        {  _queue.Enqueue(Event.Deregister); return Task.CompletedTask; }
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken cancellationToken)
        { Assert.Same(Msg, message); _queue.Enqueue(Event.Write); return Task.CompletedTask; }
        public override void Read(ChannelHandlerContext context) => _queue.Enqueue(Event.Read);
        public override void Flush(ChannelHandlerContext context) => _queue.Enqueue(Event.Flush);
    }
}
