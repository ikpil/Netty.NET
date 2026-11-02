/*
 * Copyright 2013 The Netty Project
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Resolver;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original independent channel/resolver/group teardown.
public class BootstrapResolutionSourceTest
{
    [Fact(Timeout = 10000)]
    public async Task TestCancelDuringAddressResolutionClosesChannel()
    {
        var group = new DefaultEventLoopGroup(1);
        using var resolver = new PendingResolverGroup();
        using var cancellation = new CancellationTokenSource();
        TcpSocketChannel channel = null;
        Task<IChannel> connecting = null;
        try
        {
            var bootstrap = new Bootstrap().Group(group).ChannelFactory(() => channel = new TcpSocketChannel())
                .Resolver(resolver).Handler(new ChannelInboundHandlerAdapter());
            connecting = bootstrap.ConnectAsync(new DnsEndPoint("netty.io", 443), cancellationToken: cancellation.Token);
            await resolver.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            cancellation.Cancel();
            await channel.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(channel.IsOpen);
            Assert.NotNull(resolver.Producer);
            Assert.False(resolver.Producer.Task.IsCompleted);
            Assert.True(resolver.Producer.TrySetException(new SocketException((int)SocketError.HostNotFound)));
            await channel.EventLoop.SubmitAsync(() => { });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
            Assert.True(connecting.IsCanceled);
        }
        finally
        {
            cancellation.Cancel();
            resolver.Producer.TrySetException(new SocketException((int)SocketError.HostNotFound));
            if (channel != null) await channel.CloseAsync();
            await group.StopAsync();
        }
    }
    [Fact(Timeout = 10000)]
    public async Task TestCancelBeforeRegistrationDoesNotResolveAddress()
    {
        var group = new DefaultEventLoopGroup(1);
        using var resolver = new PendingResolverGroup();
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LocalChannel channel = null;
        try
        {
            group.Next().Execute(() => { entered.SetResult(); release.Wait(); });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var bootstrap = new Bootstrap().Group(group).ChannelFactory(() => channel = new LocalChannel())
                .Resolver(resolver).Handler(new ChannelInboundHandlerAdapter());
            var connecting = bootstrap.ConnectAsync(LocalAddress.ANY, cancellationToken: cancellation.Token);
            cancellation.Cancel();
            release.Set();
            await channel.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(channel.IsOpen);
            Assert.Equal(0, resolver.Count);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
            Assert.True(connecting.IsCanceled);
        }
        finally { release.Set(); if (channel != null) await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Fact]
    public async Task TestGetResolverFailed()
    {
        var groupA = new DefaultEventLoopGroup(1);
        var groupB = new DefaultEventLoopGroup(1);
        using var resolver = new FailingResolverGroup();
        IChannel server = null;
        LocalChannel channel = null;
        try
        {
            var bootstrapA = new Bootstrap().Group(groupA).ChannelFactory(() => channel = new LocalChannel())
                .Resolver(resolver).Handler(new ChannelInboundHandlerAdapter());
            var bootstrapB = new ServerBootstrap().Group(groupB).Channel<LocalServerChannel>().ChildHandler(new ChannelInboundHandlerAdapter());
            server = await bootstrapB.BindAsync(LocalAddress.ANY);
            // Connect to the server using the asynchronous resolver.
            var connecting = bootstrapA.ConnectAsync(server.LocalAddress);
            // Should fail with the IllegalStateException.
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => connecting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.IsType<TestException>(error.InnerException);
            await channel.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(channel.IsOpen);
        }
        finally
        {
            if (channel != null) await channel.CloseAsync();
            if (server != null) await server.CloseAsync();
            await groupA.StopAsync(); await groupB.StopAsync();
        }
    }
    private sealed class TestException : Exception { }
    private sealed class FailingResolverGroup : AddressResolverGroup<EndPoint>
    { protected override IAddressResolver<EndPoint> NewResolver(IEventExecutor executor) => throw new TestException(); }
    private sealed class PendingResolverGroup : AddressResolverGroup<EndPoint>
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<EndPoint> Producer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Count;
        protected override IAddressResolver<EndPoint> NewResolver(IEventExecutor executor) => new Pending(executor, this);
        private sealed class Pending(IEventExecutor executor, PendingResolverGroup owner) : AbstractAddressResolver<EndPoint>(executor)
        {
            protected override bool DoIsResolved(EndPoint address) => false;
            protected override Task<EndPoint> DoResolveAsync(EndPoint address, CancellationToken token)
            {
                Interlocked.Increment(ref owner.Count);
                owner.Started.TrySetResult();
                return owner.Producer.Task;
            }
            protected override Task<IReadOnlyList<EndPoint>> DoResolveAllAsync(EndPoint address, CancellationToken token) => throw new NotSupportedException();
        }
    }
}
