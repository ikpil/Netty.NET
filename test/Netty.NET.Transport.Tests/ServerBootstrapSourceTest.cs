/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original independent group/channel cleanup.
public class ServerBootstrapSourceTest
{
    [Fact]
    public async Task TestSetOptionsThrow()
    {
        var group = new DefaultEventLoopGroup(1);
        SourceTestChannel created = null;
        try
        {
            var registered = new ServerBootstrap().Group(group).ChannelFactory(() => created = new SourceTestChannel())
                .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 4242).Handler(new ChannelInboundHandlerAdapter())
                .ChildHandler(new ChannelInboundHandlerAdapter()).RegisterAsync();
            await Assert.ThrowsAsync<NotSupportedException>(() => registered);
            Assert.False(created.IsActive);
        }
        finally { await group.StopAsync(); }
    }
    [Fact(Timeout = 5000)]
    public async Task TestHandlerRegister()
    {
        var added = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            var bootstrap = new ServerBootstrap().Channel<LocalServerChannel>().Group(group)
                .ChildHandler(new ChannelInboundHandlerAdapter()).Handler(new ParentHandler(context =>
                {
                    Exception error = null;
                    try { Assert.True(context.Executor.InEventLoop()); }
                    catch (Exception cause) { error = cause; }
                    finally { added.TrySetResult(error); }
                }));
            await bootstrap.RegisterAsync();
            Assert.Null(await added.Task.WaitAsync(TestContext.Current.CancellationToken));
        }
        finally { await group.StopAsync(); }
    }
    [Fact(Timeout = 3000)]
    public Task TestParentHandler() => ParentHandlerCase(false, TestContext.Current.CancellationToken);
    [Fact(Timeout = 3000)]
    public Task TestParentHandlerViaChannelInitializer() => ParentHandlerCase(true, TestContext.Current.CancellationToken);
    private static async Task ParentHandlerCase(bool initializer, CancellationToken timeout)
    {
        var address = new LocalAddress(Guid.NewGuid().ToString());
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var init = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ParentHandler(_ => init.TrySetResult(), (_, _) => read.TrySetResult());
        var group = new DefaultEventLoopGroup(1);
        IChannel server = null, client = null;
        try
        {
            var sb = new ServerBootstrap().Channel<LocalServerChannel>().Group(group).ChildHandler(new ChannelInboundHandlerAdapter());
            sb.Handler(initializer ? new ActionChannelInitializer<IChannel>(channel => channel.Pipeline.AddLast(handler)) : handler);
            var cb = new Bootstrap().Group(group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter());
            server = await sb.BindAsync(address);
            client = await cb.ConnectAsync(address);
            await init.Task.WaitAsync(timeout);
            await read.Task.WaitAsync(timeout);
        }
        finally
        {
            if (server != null) await server.CloseAsync();
            if (client != null) await client.CloseAsync();
            await group.StopAsync();
        }
    }
    [Fact]
    public async Task OptionsAndAttributesMustBeAvailableOnChildChannelInit()
    {
        var group = new DefaultEventLoopGroup(1);
        var address = new LocalAddress(Guid.NewGuid().ToString());
        var key = AttributeKey<string>.ValueOf(Guid.NewGuid().ToString());
        bool requestServed = false;
        IChannel server = null, client = null;
        try
        {
            var sb = new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .ChildOption(ChannelOption.CONNECT_TIMEOUT_MILLIS, 4242).ChildAttr(key, "value")
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel =>
                {
                    Assert.True(channel.Config.TryGetOption(ChannelOption.CONNECT_TIMEOUT_MILLIS, out int option));
                    Assert.Equal(4242, option);
                    Assert.Equal("value", channel.Attr(key).Get());
                    Volatile.Write(ref requestServed, true);
                }));
            server = await sb.BindAsync(address);
            client = await new Bootstrap().Group(group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(address);
        }
        finally
        {
            if (server != null) await server.CloseAsync();
            if (client != null) await client.CloseAsync();
            await group.StopAsync();
        }
        Assert.True(Volatile.Read(ref requestServed));
    }
    private sealed class ParentHandler(Action<ChannelHandlerContext> added, Action<ChannelHandlerContext, object> read = null) : ChannelInboundHandlerAdapter
    {
        public override void HandlerAdded(ChannelHandlerContext context) => added(context);
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { read?.Invoke(context, message); base.ChannelRead(context, message); }
    }
}
