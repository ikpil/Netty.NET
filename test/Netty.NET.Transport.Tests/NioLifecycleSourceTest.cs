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
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original operation ordering and independent cleanup.
public class NioEventLoopSourceTest
{
    [Fact(Timeout = 5000)]
    public async Task TestScheduleBigDelayNotOverflow()
    {
        var group = new DefaultEventLoopGroup(1);
        using var cancel = new CancellationTokenSource();
        try
        {
            var future = group.Next().ScheduleAsync(() => { /* NOOP */ }, TimeSpan.MaxValue, cancel.Token);
            Assert.NotSame(future, await Task.WhenAny(future, Task.Delay(1000, TestContext.Current.CancellationToken)));
            cancel.Cancel(); Assert.True(future.IsCanceled);
        }
        finally { await group.StopAsync(); }
    }
    [Fact(Timeout = 60000)]
    public async Task TestTaskRemovalOnShutdownThrowsNoUnsupportedOperationException()
    {
        Exception error = null;
        Action task = () => { /* NOOP */ };
        // Just run often enough to trigger it normally.
        for (int i = 0; i < 250; i++)
        {
            var group = new DefaultEventLoopGroup(1);
            var loop = group.Next();
            using var started = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try { for (;;) { loop.Execute(task); started.Set(); } }
                catch (Exception cause) { Interlocked.Exchange(ref error, cause); }
            }) { IsBackground = true };
            try
            {
                thread.Start(); started.Wait(TestContext.Current.CancellationToken);
                await group.StopAsync().WaitAsync(TestContext.Current.CancellationToken);
                Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
                Assert.IsType<RejectedExecutionException>(Volatile.Read(ref error));
                error = null;
            }
            finally { await group.StopAsync(); thread.Join(TimeSpan.FromSeconds(5)); }
        }
    }
    [Fact(Timeout = 3000)]
    public async Task TestChannelsRegistered()
    {
        var group = new DefaultEventLoopGroup(1);
        var loop = (DefaultEventLoop)group.Next();
        var ch1 = new TcpServerSocketChannel(); var ch2 = new TcpServerSocketChannel();
        try
        {
            Assert.Equal(0, await RegisteredChannels(loop));
            await loop.RegisterAsync(ch1); await loop.RegisterAsync(ch2);
            Assert.Equal(2, await RegisteredChannels(loop));
            await ch1.DeregisterAsync();
            int registered;
            // As SelectionKeys are removed in a lazy fashion in the JDK implementation we may need to query a few
            // times before we see the right number of registered chanels.
            // CLR registration is tracked directly; keep the original polling/owner barrier.
            while ((registered = await RegisteredChannels(loop)) == 2) await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.Equal(1, registered);
        }
        finally { await ch1.CloseAsync(); await ch2.CloseAsync(); await group.StopAsync(); }
    }
    // Only reliable if run from event loop
    private static Task<int> RegisteredChannels(DefaultEventLoop loop)
        => loop.SubmitAsync(loop.RegisteredChannels).WaitAsync(TimeSpan.FromSeconds(1));
}

public class TcpServerSocketSourceTest
{
    [Fact]
    public async Task TestCloseOnError()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var channel = new ProbeServer(socket);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            await group.RegisterAsync(channel); await channel.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            Assert.False(channel.ShouldClose(new IOException()));
            Assert.True(channel.ShouldClose(new ArgumentException()));
            await channel.CloseAsync();
        }
        finally { await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Fact]
    public async Task TestIsActiveFalseAfterClose()
    {
        var channel = new TcpServerSocketChannel(); var group = new DefaultEventLoopGroup(1);
        try
        {
            await group.RegisterAsync(channel); await channel.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            Assert.True(channel.IsActive); Assert.True(channel.IsOpen);
            await channel.CloseAsync(); Assert.False(channel.IsOpen); Assert.False(channel.IsActive);
        }
        finally { await channel.CloseAsync(); await group.StopAsync(); }
    }
    private sealed class ProbeServer(Socket socket) : TcpServerSocketChannel(socket)
    { internal bool ShouldClose(Exception error) => CloseOnReadError(error); }
}

public class AbstractSocketChannelOptionsSourceTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestNioChannelOption(bool server)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        IChannel channel = server ? new TcpServerSocketChannel(socket) : new TcpSocketChannel(socket);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            bool value1 = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0;
            Assert.True(channel.Config.TryGetOption(ChannelOption.SO_REUSEADDR, out bool value2));
            Assert.Equal(value1, value2);
            channel.Config.SetOption(ChannelOption.SO_REUSEADDR, !value2);
            bool value3 = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0;
            Assert.True(channel.Config.TryGetOption(ChannelOption.SO_REUSEADDR, out bool value4));
            Assert.Equal(value3, value4); Assert.NotEqual(value1, value4);
        }
        finally { await group.RegisterAsync(channel); await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestGetOptions(bool server)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        IChannel channel = server ? new TcpServerSocketChannel(socket) : new TcpSocketChannel(socket);
        var group = new DefaultEventLoopGroup(1);
        try { channel.Config.GetOptions(); }
        finally { await group.RegisterAsync(channel); await channel.CloseAsync(); await group.StopAsync(); }
    }
}
