/*
 * Copyright 2014 The Netty Project

 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:

 * https://www.apache.org/licenses/LICENSE-2.0

 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Source operations have explicit ownership; cleanup is independent of test cancellation.
public class AbstractChannelTest
{
    [Fact]
    public async Task EnsureInitialRegistrationFiresActive()
    {
        // This allows us to have a single-threaded test
        var loop = ImmediateLoop();
        var channel = new TestChannel();
        var handler = new LifecycleHandler();
        channel.Pipeline.AddLast(handler);
        try
        {
            await channel.RegisterAsync(loop); // Cause any exceptions to be thrown
            Assert.Equal(1, handler.Added);
            Assert.Equal(1, handler.Registered);
            Assert.Equal(1, handler.Active);
        }
        finally { await channel.CloseAsync(); }
    }

    [Fact]
    public async Task EnsureSubsequentRegistrationDoesNotFireActive()
    {
        // This allows us to have a single-threaded test
        var loop = ImmediateLoop();
        var channel = new TestChannel();
        var handler = new LifecycleHandler();
        channel.Pipeline.AddLast(handler);
        try
        {
            await channel.RegisterAsync(loop);
            await channel.DeregisterAsync();
            await channel.RegisterAsync(loop);
            Assert.Equal(1, handler.Added);
            // Should register twice
            Assert.Equal(2, handler.Registered);
            Assert.Equal(1, handler.Active);
            Assert.Equal(1, handler.Unregistered);
        }
        finally { await channel.CloseAsync(); }
    }

    [Fact]
    public void EnsureDefaultChannelId()
    {
        var channel = new TestChannel();
        Assert.IsType<DefaultChannelId>(channel.Id);
    }

    [Fact]
    public async Task TestClosedChannelExceptionCarryIOException()
    {
        var ioException = new IOException();
        var channel = new FailingChannel(ioException);
        var loop = new DefaultEventLoop();
        try
        {
            await channel.RegisterAsync(loop);
            await channel.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8888));
            Assert.Same(ioException, await Assert.ThrowsAsync<IOException>(() => channel.WriteAndFlushAsync("")));
            await AssertClosedChannelException(channel.WriteAndFlushAsync(""), ioException);
            await AssertClosedChannelException(channel.WriteAsync(""), ioException);
            await AssertClosedChannelException(channel.BindAsync(new IPEndPoint(IPAddress.Loopback, 8888)), ioException);
        }
        finally
        {
            await channel.CloseAsync();
            await loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task EnsureClosedChannelOnWriteReleaseMsg()
    {
        var ioException = new IOException();
        var channel = new FailingChannel(ioException);
        var loop = new DefaultEventLoop();
        try
        {
            await channel.RegisterAsync(loop);
            await channel.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8888));
            Assert.Same(ioException, await Assert.ThrowsAsync<IOException>(() => channel.WriteAndFlushAsync("")));
            ByteBuf message = Unpooled.WrappedBuffer(new byte[16]);
            Assert.Equal(1, message.ReferenceCount);
            var submitted = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            // The empty pipeline dispatches directly to the native transport head, replacing Unsafe.write.
            loop.Execute(() => submitted.SetResult(channel.WriteAsync(message)));
            Task write = await submitted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<IOException>(() => write.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(write.IsCompleted);
            Assert.Equal(0, message.ReferenceCount);
        }
        finally
        {
            await channel.CloseAsync();
            await loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task FlushOnUnconnectedOpenChannelFailsWithNotYetConnected()
    {
        var loop = ImmediateLoop();
        //Open, but never connected
        var channel = new TestChannel(false);
        try
        {
            await channel.RegisterAsync(loop);
            ByteBuf message = Unpooled.WrappedBuffer(new byte[16]);
            Task write = null;
            loop.Execute(() => { write = channel.WriteAsync(message); channel.Flush(); });
            Assert.True(write.IsCompleted);
            var error = await Assert.ThrowsAsync<SocketException>(() => write);
            Assert.Equal(SocketError.NotConnected, error.SocketErrorCode);
            Assert.Equal(0, message.ReferenceCount);
        }
        finally { await channel.CloseAsync(); }
    }

    [Fact]
    public async Task FlushOnUnconnectedClosedChannelFailsWithClosedChannel()
    {
        var loop = ImmediateLoop();
        var channel = new TestChannel(false);
        try
        {
            await channel.RegisterAsync(loop);
            ByteBuf message = Unpooled.WrappedBuffer(new byte[16]);
            Task write = null;
            loop.Execute(() =>
            {
                write = channel.WriteAsync(message);
                _ = channel.CloseAsync();
                channel.Flush();
            });
            Assert.True(write.IsCompleted);
            await Assert.ThrowsAsync<IOException>(() => write);
            Assert.Equal(0, message.ReferenceCount);
        }
        finally { await channel.CloseAsync(); }
    }

    private static async Task AssertClosedChannelException(Task operation, IOException expected)
    {
        // IOException is the CLR closed-channel representation; preserve the original cause identity.
        var cause = await Assert.ThrowsAsync<IOException>(() => operation);
        Assert.Same(expected, cause.InnerException);
    }

    private static IEventLoop ImmediateLoop()
    {
        var loop = new Mock<IEventLoop>();
        loop.Setup(value => value.InEventLoop()).Returns(true);
        loop.Setup(value => value.Execute(It.IsAny<Action>())).Callback<Action>(action => action());
        return loop.Object;
    }

    private class TestChannel : AbstractChannel
    {
        internal TestChannel(bool active = true) : base(null, new ChannelMetadata(false)) => SetActive(active);
        public override EndPoint LocalAddress => null;
        public override EndPoint RemoteAddress => null;
        protected override Task DoBindAsync(EndPoint address, CancellationToken token) => Task.CompletedTask;
        protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken token) =>
            Task.FromException(new NotSupportedException());
        protected override void DoClose() { }
        protected override void DoBeginRead() { }
        protected override Task DoWrite(object message) => Task.CompletedTask;
    }

    private sealed class FailingChannel(IOException error) : TestChannel(false)
    {
        protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken token)
        { SetActive(true); return Task.CompletedTask; }
        protected override Task DoWrite(object message) => Task.FromException(error);
    }

    private sealed class LifecycleHandler : ChannelInboundHandlerAdapter
    {
        internal int Added, Registered, Active, Unregistered;
        public override void HandlerAdded(ChannelHandlerContext context) => Added++;
        public override void ChannelRegistered(ChannelHandlerContext context) => Registered++;
        public override void ChannelActive(ChannelHandlerContext context) => Active++;
        public override void ChannelUnregistered(ChannelHandlerContext context) => Unregistered++;
    }
}
#pragma warning restore xUnit1051
