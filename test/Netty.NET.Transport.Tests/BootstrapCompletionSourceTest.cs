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

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051, xUnit1031 // Original registration ordering and independent cleanup.
public class BootstrapCompletionSourceTest
{
    [Fact]
    public Task TestLateRegisterSuccess() => LateBind(false);
    [Fact]
    public Task TestLateRegisterSuccessBindFailed() => LateBind(true);
    private static async Task LateBind(bool fail)
    {
        var group = new HeldRegistrationGroup();
        RecordingServer channel = null;
        try
        {
            var bootstrap = new ServerBootstrap().Group(group).ChannelFactory(() => channel = new RecordingServer(fail))
                .ChildHandler(new ChannelInboundHandlerAdapter()).LocalAddress(new LocalAddress(typeof(BootstrapCompletionSourceTest).FullName));
            var future = bootstrap.BindAsync();
            Assert.False(future.IsCompleted);
            Assert.True(channel.IsRegistered);
            Assert.False(channel.IsActive);
            Assert.False(channel.BoundOnOwner);
            group.Producer.SetResult();
            bool success;
            try { await future; success = true; }
            catch (SocketException) { success = false; }
            var queue = new Queue<bool>();
            // Task consumers explicitly regain their owner; Tasks do not inherit Netty Future listener affinity.
            await channel.EventLoop.SubmitAsync(() =>
            {
                queue.Enqueue(channel.EventLoop.InEventLoop(Thread.CurrentThread));
                queue.Enqueue(success);
            });
            Assert.True(queue.Dequeue());
            Assert.Equal(!fail, queue.Dequeue());
            Assert.True(channel.BoundOnOwner);
        }
        finally { group.Producer.TrySetResult(); await group.StopAsync(); }
    }
    [Fact(Timeout = 10000)]
    public async Task TestLateRegistrationConnect()
    {
        var group = new QueuedRegistrationGroup();
        try
        {
            var bootstrap = new Bootstrap().Group(group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter());
            await Assert.ThrowsAsync<SocketException>(() => bootstrap.ConnectAsync(LocalAddress.ANY).WaitAsync(TestContext.Current.CancellationToken));
        }
        finally { await group.StopAsync(); }
    }
    [Fact]
    public void TestResolverDefault()
    {
        var bootstrap = new Bootstrap();
        Assert.Contains("resolver:", bootstrap.Config.ToString());
        Assert.NotNull(bootstrap.Config.Resolver);
        Assert.IsType<DefaultAddressResolverGroup>(bootstrap.Config.Resolver);
    }
    [Fact]
    public void TestResolverDisabled()
    {
        var bootstrap = new Bootstrap().DisableResolver();
        Assert.DoesNotContain("resolver:", bootstrap.Config.ToString());
        Assert.Null(bootstrap.Config.Resolver);
    }
    [Fact]
    public async Task TestAsyncResolutionSuccess()
    {
        var groupA = new DefaultEventLoopGroup(1);
        var groupB = new DefaultEventLoopGroup(1);
        using var resolver = new TestAddressResolverGroup(true);
        try
        {
            var bootstrapA = new Bootstrap().Group(groupA).Channel<LocalChannel>().Resolver(resolver).Handler(new ChannelInboundHandlerAdapter());
            var bootstrapB = new ServerBootstrap().Group(groupB).Channel<LocalServerChannel>().ChildHandler(new ChannelInboundHandlerAdapter());
            Assert.Contains("resolver:", bootstrapA.Config.ToString());
            Assert.IsType<TestAddressResolverGroup>(bootstrapA.Config.Resolver);
            var local = (await bootstrapB.BindAsync(LocalAddress.ANY)).LocalAddress;
            // Connect to the server using the asynchronous resolver.
            await bootstrapA.ConnectAsync(local);
        }
        finally { await groupA.StopAsync(); await groupB.StopAsync(); }
    }
    [Fact]
    public async Task TestAsyncResolutionFailure()
    {
        var groupA = new DefaultEventLoopGroup(1);
        var groupB = new DefaultEventLoopGroup(1);
        using var resolver = new TestAddressResolverGroup(false);
        LocalChannel channel = null;
        try
        {
            var bootstrapA = new Bootstrap().Group(groupA).ChannelFactory(() => channel = new LocalChannel()).Resolver(resolver).Handler(new ChannelInboundHandlerAdapter());
            var bootstrapB = new ServerBootstrap().Group(groupB).Channel<LocalServerChannel>().ChildHandler(new ChannelInboundHandlerAdapter());
            var local = (await bootstrapB.BindAsync(LocalAddress.ANY)).LocalAddress;
            // Connect to the server using the asynchronous resolver.
            var future = bootstrapA.ConnectAsync(local);
            // Should fail with the UnknownHostException.
            var failure = await Assert.ThrowsAsync<SocketException>(() => future.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(SocketError.HostNotFound, failure.SocketErrorCode);
            await channel.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(channel.IsOpen);
        }
        finally { await groupA.StopAsync(); await groupB.StopAsync(); }
    }
    private sealed class HeldRegistrationGroup : DefaultEventLoopGroup
    {
        internal readonly TaskCompletionSource Producer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HeldRegistrationGroup() : base(1) { }
        public override Task RegisterAsync(IChannel channel, CancellationToken token = default)
        { base.RegisterAsync(channel, token).GetAwaiter().GetResult(); return Producer.Task; }
    }
    private sealed class QueuedRegistrationGroup : DefaultEventLoopGroup
    {
        internal QueuedRegistrationGroup() : base(1) { }
        public override Task RegisterAsync(IChannel channel, CancellationToken token = default)
        {
            var producer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // Delay registration
            Next().Execute(async () =>
            {
                try { await base.RegisterAsync(channel, token); producer.SetResult(); }
                catch (OperationCanceledException error) { producer.TrySetCanceled(error.CancellationToken); }
                catch (Exception error) { producer.SetException(error); }
            });
            return producer.Task;
        }
    }
    private sealed class RecordingServer(bool fail) : LocalServerChannel
    {
        internal bool BoundOnOwner;
        protected override Task DoBindAsync(EndPoint address, CancellationToken token)
        {
            BoundOnOwner = EventLoop.InEventLoop();
            if (!fail) return base.DoBindAsync(address, token);
            // Close the Channel to emulate what NIO and others impl do on bind failure
            // See https://github.com/netty/netty/issues/2586
            _ = CloseAsync();
            return Task.FromException(new SocketException());
        }
    }
    private sealed class TestAddressResolverGroup(bool success) : AddressResolverGroup<EndPoint>
    {
        protected override IAddressResolver<EndPoint> NewResolver(IEventExecutor executor) => new Resolver(executor, success);
        private sealed class Resolver(IEventExecutor executor, bool success) : AbstractAddressResolver<EndPoint>(executor)
        {
            protected override bool DoIsResolved(EndPoint address) => false;
            protected override Task<EndPoint> DoResolveAsync(EndPoint address, CancellationToken token)
            {
                var producer = new TaskCompletionSource<EndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
                Executor.Execute(() => { if (success) producer.SetResult(address); else producer.SetException(new SocketException((int)SocketError.HostNotFound)); });
                return producer.Task;
            }
            protected override Task<IReadOnlyList<EndPoint>> DoResolveAllAsync(EndPoint address, CancellationToken token)
            {
                var producer = new TaskCompletionSource<IReadOnlyList<EndPoint>>(TaskCreationOptions.RunContinuationsAsynchronously);
                Executor.Execute(() => { if (success) producer.SetResult(new[] { address }); else producer.SetException(new SocketException((int)SocketError.HostNotFound)); });
                return producer.Task;
            }
        }
    }
}
