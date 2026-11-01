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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Sockets;
using Skip = Netty.NET.Transport.Channels.ChannelHandlerMask.SkipAttribute;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independently owned cleanup and original event/operation sequence.
public class DefaultChannelPipelineCompletionSourceTest
{
    [Fact(Timeout = 3000)]
    public async Task TestAddInListenerNio() => await CheckAddInListener(new TcpSocketChannel(), TestContext.Current.CancellationToken);
    [Fact(Timeout = 3000)]
    public async Task TestAddInListenerLocal() => await CheckAddInListener(new LocalChannel(), TestContext.Current.CancellationToken);
    private static async Task CheckAddInListener(IChannel channel, CancellationToken token)
    {
        var group = new DefaultEventLoopGroup(1);
        try
        {
            var value = new object();
            var captured = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task registered = channel.RegisterAsync(group.Next());
            // CLR Task continuations do not carry a Netty promise executor. Explicitly regain the registered
            // owner before executing the original listener body and its immediate HandlerAdded assertion.
            await registered.ContinueWith(completion => channel.EventLoop.SubmitAsync(() =>
            {
                completion.GetAwaiter().GetResult();
                var handler = new ListenerAdded(captured);
                channel.Pipeline.AddLast(handler);
                if (!handler.Added)
                { captured.TrySetException(new InvalidOperationException("handlerAdded(...) should have been called")); return; }
                // This event must be captured by the added handler.
                channel.Pipeline.FireUserEventTriggered(value);
            }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
            Assert.Same(value, await captured.Task.WaitAsync(token));
        }
        finally { await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Fact]
    public async Task TestSkipHandlerMethodsIfAnnotated()
    {
        var channel = new EmbeddedChannel(true);
        var pipeline = channel.Pipeline;
        var outbound = new OutboundCalledHandler();
        var skip = new SkipHandler();
        var inbound = new InboundCalledHandler();
        try
        {
            pipeline.AddLast(outbound, skip, inbound);
            pipeline.FireChannelRegistered();
            pipeline.FireChannelUnregistered();
            pipeline.FireChannelActive();
            pipeline.FireChannelInactive();
            pipeline.FireChannelRead("");
            pipeline.FireChannelReadComplete();
            pipeline.FireChannelWritabilityChanged();
            pipeline.FireUserEventTriggered("");
            pipeline.FireExceptionCaught(new Exception());
            await pipeline.DeregisterAsync();
            await pipeline.BindAsync(new LocalAddress("skip-bind"));
            await pipeline.ConnectAsync(new LocalAddress("skip-connect"));
            await pipeline.DisconnectAsync();
            await pipeline.CloseAsync();
            await pipeline.WriteAsync("");
            pipeline.Flush();
            pipeline.Read();
            pipeline.Remove(outbound);
            pipeline.Remove(inbound);
            pipeline.Remove(skip);
            Assert.False(channel.Finish());
            outbound.AssertCalled();
            inbound.AssertCalled();
            skip.AssertSkipped();
        }
        finally { channel.FinishAndReleaseAll(); }
    }
    private sealed class ListenerAdded(TaskCompletionSource<object> captured) : ChannelInboundHandlerAdapter
    {
        internal bool Added;
        public override void HandlerAdded(ChannelHandlerContext context) => Added = true;
        public override void UserEventTriggered(ChannelHandlerContext context, object evt) => captured.TrySetResult(evt);
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => captured.TrySetException(cause);
    }
    private sealed class SkipHandler : ChannelDuplexHandler
    {
        private int _state = 2;
        private Exception _error;
        private void Fail() => _error = new InvalidOperationException("Method should never been called");
        [Skip]
        public override Task BindAsync(ChannelHandlerContext c, EndPoint address, CancellationToken token) { Fail(); return c.BindAsync(address, token); }
        [Skip]
        public override Task ConnectAsync(ChannelHandlerContext c, EndPoint remote, EndPoint local, CancellationToken token) { Fail(); return c.ConnectAsync(remote, local, token); }
        [Skip]
        public override Task DisconnectAsync(ChannelHandlerContext c, CancellationToken token) { Fail(); return c.DisconnectAsync(token); }
        [Skip]
        public override Task CloseAsync(ChannelHandlerContext c, CancellationToken token) { Fail(); return c.CloseAsync(token); }
        [Skip]
        public override Task DeregisterAsync(ChannelHandlerContext c, CancellationToken token) { Fail(); return c.DeregisterAsync(token); }
        [Skip]
        public override Task WriteAsync(ChannelHandlerContext c, object message, CancellationToken token) { Fail(); return c.WriteAsync(message, token); }
        [Skip]
        public override void Read(ChannelHandlerContext c) { Fail(); c.Read(); }
        [Skip]
        public override void Flush(ChannelHandlerContext c) { Fail(); c.Flush(); }
        [Skip]
        public override void ChannelRegistered(ChannelHandlerContext c) { Fail(); c.FireChannelRegistered(); }
        [Skip]
        public override void ChannelUnregistered(ChannelHandlerContext c) { Fail(); c.FireChannelUnregistered(); }
        [Skip]
        public override void ChannelActive(ChannelHandlerContext c) { Fail(); c.FireChannelActive(); }
        [Skip]
        public override void ChannelInactive(ChannelHandlerContext c) { Fail(); c.FireChannelInactive(); }
        [Skip]
        public override void ChannelRead(ChannelHandlerContext c, object message) { Fail(); c.FireChannelRead(message); }
        [Skip]
        public override void ChannelReadComplete(ChannelHandlerContext c) { Fail(); c.FireChannelReadComplete(); }
        [Skip]
        public override void UserEventTriggered(ChannelHandlerContext c, object evt) { Fail(); c.FireUserEventTriggered(evt); }
        [Skip]
        public override void ChannelWritabilityChanged(ChannelHandlerContext c) { Fail(); c.FireChannelWritabilityChanged(); }
        [Skip]
        public override void ExceptionCaught(ChannelHandlerContext c, Exception cause) { Fail(); c.FireExceptionCaught(cause); }
        public override void HandlerAdded(ChannelHandlerContext c) => _state--;
        public override void HandlerRemoved(ChannelHandlerContext c) => _state--;
        internal void AssertSkipped() { Assert.Equal(0, _state); if (_error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_error).Throw(); }
    }
    private sealed class OutboundCalledHandler : ChannelOutboundHandlerAdapter
    {
        private int _mask;
        public override void HandlerAdded(ChannelHandlerContext c) => _mask |= 1 << 8;
        public override void HandlerRemoved(ChannelHandlerContext c) => _mask |= 1 << 9;
        public override Task BindAsync(ChannelHandlerContext c, EndPoint address, CancellationToken token) { _mask |= 1; return Task.CompletedTask; }
        public override Task ConnectAsync(ChannelHandlerContext c, EndPoint remote, EndPoint local, CancellationToken token) { _mask |= 1 << 1; return Task.CompletedTask; }
        public override Task DisconnectAsync(ChannelHandlerContext c, CancellationToken token) { _mask |= 1 << 2; return Task.CompletedTask; }
        public override Task CloseAsync(ChannelHandlerContext c, CancellationToken token) { _mask |= 1 << 3; return Task.CompletedTask; }
        public override Task DeregisterAsync(ChannelHandlerContext c, CancellationToken token) { _mask |= 1 << 4; return Task.CompletedTask; }
        public override void Read(ChannelHandlerContext c) => _mask |= 1 << 5;
        public override Task WriteAsync(ChannelHandlerContext c, object message, CancellationToken token) { _mask |= 1 << 6; return Task.CompletedTask; }
        public override void Flush(ChannelHandlerContext c) => _mask |= 1 << 7;
        internal void AssertCalled() => Assert.Equal((1 << 10) - 1, _mask);
    }
    private sealed class InboundCalledHandler : ChannelInboundHandlerAdapter
    {
        private int _mask;
        public override void HandlerAdded(ChannelHandlerContext c) => _mask |= 1 << 9;
        public override void HandlerRemoved(ChannelHandlerContext c) => _mask |= 1 << 10;
        public override void ChannelRegistered(ChannelHandlerContext c) => _mask |= 1;
        public override void ChannelUnregistered(ChannelHandlerContext c) => _mask |= 1 << 1;
        public override void ChannelActive(ChannelHandlerContext c) => _mask |= 1 << 2;
        public override void ChannelInactive(ChannelHandlerContext c) => _mask |= 1 << 3;
        public override void ChannelRead(ChannelHandlerContext c, object message) => _mask |= 1 << 4;
        public override void ChannelReadComplete(ChannelHandlerContext c) => _mask |= 1 << 5;
        public override void UserEventTriggered(ChannelHandlerContext c, object evt) => _mask |= 1 << 6;
        public override void ChannelWritabilityChanged(ChannelHandlerContext c) => _mask |= 1 << 7;
        public override void ExceptionCaught(ChannelHandlerContext c, Exception cause) => _mask |= 1 << 8;
        internal void AssertCalled() => Assert.Equal((1 << 11) - 1, _mask);
    }
}
