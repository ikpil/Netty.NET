/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

public class DefaultChannelPipelineTailTest : IAsyncLifetime
{
#pragma warning disable xUnit1051 // Preserve the original one-second latch waits; connect/close use the test token.
    // The original one-thread LocalIoHandler group is represented by the CLR event loop:
    // the test's MyChannel has no native I/O, and every callback still crosses the real executor queue.
    private readonly DefaultEventLoopGroup _group = new(1);
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public async ValueTask DisposeAsync() => await _group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
    private Task<IChannel> Connect(MyChannel channel) => new Bootstrap().ChannelFactory(() => channel)
        .Group(_group).Handler(new ChannelInboundHandlerAdapter()).RemoteAddress(new IPEndPoint(IPAddress.Any, 0))
        .ConnectAsync(TestContext.Current.CancellationToken);
    [Fact]
    public async Task TestOnUnhandledInboundChannelActive()
    {
        using var latch = new CountdownEvent(1);
        IChannel channel = await Connect(new MyChannel { Active = () => latch.Signal() });
        try
        { Assert.True(latch.Wait(TimeSpan.FromSeconds(1))); }
        finally { await channel.CloseAsync(TestContext.Current.CancellationToken); }
    }
    [Fact]
    public async Task TestOnUnhandledInboundChannelInactive()
    {
        using var latch = new CountdownEvent(1);
        IChannel channel = await Connect(new MyChannel { Inactive = () => latch.Signal() });
        await channel.CloseAsync(TestContext.Current.CancellationToken);
        Assert.True(latch.Wait(TimeSpan.FromSeconds(1)));
    }
    [Fact]
    public async Task TestOnUnhandledInboundException()
    {
        using var latch = new CountdownEvent(1);
        Exception cause = null;
        IChannel channel = await Connect(new MyChannel { Error = error => { cause = error; latch.Signal(); } });
        try
        {
            var error = new IOException("testOnUnhandledInboundException");
            channel.Pipeline.FireExceptionCaught(error);
            Assert.True(latch.Wait(TimeSpan.FromSeconds(1)));
            Assert.Same(error, cause);
        }
        finally { await channel.CloseAsync(TestContext.Current.CancellationToken); }
    }
    [Fact]
    public async Task TestOnUnhandledInboundMessage()
    {
        using var latch = new CountdownEvent(1);
        IChannel channel = await Connect(new MyChannel { Message = _ => latch.Signal() });
        try
        { channel.Pipeline.FireChannelRead("testOnUnhandledInboundMessage"); Assert.True(latch.Wait(TimeSpan.FromSeconds(1))); }
        finally { await channel.CloseAsync(TestContext.Current.CancellationToken); }
    }
    [Fact]
    public async Task TestOnUnhandledInboundReadComplete()
    {
        using var latch = new CountdownEvent(1);
        IChannel channel = await Connect(new MyChannel { ReadComplete = () => latch.Signal() });
        try
        { channel.Pipeline.FireChannelReadComplete(); Assert.True(latch.Wait(TimeSpan.FromSeconds(1))); }
        finally { await channel.CloseAsync(TestContext.Current.CancellationToken); }
    }
    [Fact]
    public async Task TestOnUnhandledInboundUserEventTriggered()
    {
        using var latch = new CountdownEvent(1);
        IChannel channel = await Connect(new MyChannel { UserEvent = _ => latch.Signal() });
        try
        { channel.Pipeline.FireUserEventTriggered("testOnUnhandledInboundUserEventTriggered"); Assert.True(latch.Wait(TimeSpan.FromSeconds(1))); }
        finally { await channel.CloseAsync(TestContext.Current.CancellationToken); }
    }
    [Fact]
    public async Task TestOnUnhandledInboundWritabilityChanged()
    {
        using var latch = new CountdownEvent(1);
        IChannel channel = await Connect(new MyChannel { Writability = () => latch.Signal() });
        try
        { channel.Pipeline.FireChannelWritabilityChanged(); Assert.True(latch.Wait(TimeSpan.FromSeconds(1))); }
        finally { await channel.CloseAsync(TestContext.Current.CancellationToken); }
    }
    private sealed class MyChannel : AbstractChannel
    {
        internal Action Active, Inactive, ReadComplete, Writability;
        internal Action<object> Message, UserEvent;
        internal Action<Exception> Error;
        public MyChannel() : base(null, new ChannelMetadata(false)) { }
        protected override DefaultChannelPipeline NewChannelPipeline() => new MyChannelPipeline(this);
        public override EndPoint LocalAddress => null;
        public override EndPoint RemoteAddress => null;
        protected override Task DoBindAsync(EndPoint address, CancellationToken token) => Task.CompletedTask;
        protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken token) { SetActive(true); return Task.CompletedTask; }
        protected override void DoClose() { }
        protected override void DoBeginRead() { }
        protected override Task DoWriteAsync(object message) => Task.FromException(new IOException());
        private sealed class MyChannelPipeline(MyChannel channel) : DefaultChannelPipeline(channel)
        {
            protected override void OnUnhandledInboundChannelActive() => channel.Active?.Invoke();
            protected override void OnUnhandledInboundChannelInactive() => channel.Inactive?.Invoke();
            protected override void OnUnhandledInboundException(Exception cause) => channel.Error?.Invoke(cause);
            protected override void OnUnhandledInboundMessage(object message) => channel.Message?.Invoke(message);
            protected override void OnUnhandledInboundChannelReadComplete() => channel.ReadComplete?.Invoke();
            protected override void OnUnhandledInboundUserEventTriggered(object evt) => channel.UserEvent?.Invoke(evt);
            protected override void OnUnhandledChannelWritabilityChanged() => channel.Writability?.Invoke();
        }
    }
}
#pragma warning restore xUnit1051
