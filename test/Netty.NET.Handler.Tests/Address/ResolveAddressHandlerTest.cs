/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Handler.Address;
using Netty.NET.Resolver;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Tests.Address;

// CLR: the static @BeforeAll createEventLoop()/@AfterAll destroyEventLoop() run from a class fixture, which xUnit
// creates "Just before the first test" and cleans up "once all the tests have finished ... by calling Dispose"
// (https://xunit.net/docs/shared-context), as SocketEchoSourceTest does in Transport. UUID.randomUUID() is
// Guid.NewGuid(), UnknownHostException is Common's UnknownHostException (as DefaultNameResolver reports it) and
// AddressResolverGroup<SocketAddress> is AddressResolverGroup<EndPoint>, so the handler is
// ResolveAddressHandler<EndPoint> (see ResolveAddressHandler.cs). Promise<SocketAddress> is the Task<EndPoint> that
// doResolve returns. sb.bind(...).syncUninterruptibly().channel() is the awaited bind Task<IChannel>;
// cb.connect(...).awaitUninterruptibly() waits without rethrowing, as ConfigureAwaitOptions.SuppressThrowing, then
// future.cause() is the inner exception of the faulted Task and isSuccess() is IsCompletedSuccessfully.
// future.channel() has no Task<IChannel> counterpart when the connect failed: Bootstrap closes the channel of a failed
// connect itself (Bootstrap.DoResolveAndConnect0), so only a connected channel is closed here; resolverGroup.close() is
// Dispose(). The channel operations receive TestContext.Current.CancellationToken (xUnit1051).
public class ResolveAddressHandlerTest : IClassFixture<ResolveAddressHandlerTest.EventLoopFixture>
{

    private static readonly LocalAddress UNRESOLVED = new LocalAddress("unresolved-" + Guid.NewGuid());
    private static readonly LocalAddress RESOLVED = new LocalAddress("resolved-" + Guid.NewGuid());
    private static readonly Exception ERROR = new UnknownHostException();

    private static IEventLoopGroup group;

    public sealed class EventLoopFixture : IDisposable
    {
        public EventLoopFixture()
        {
            CreateEventLoop();
        }

        public void Dispose()
        {
            DestroyEventLoop();
        }
    }

    // CLR: internal instead of the source's public, because xUnit1013 reports public non-test methods on a test class.
    internal static void CreateEventLoop()
    {
        group = new MultiThreadIoEventLoopGroup(LocalIoHandler.NewFactory());
    }

    // CLR: shutdownGracefully() returns a future the source does not wait for; the Task is not awaited either.
    internal static void DestroyEventLoop()
    {
        if (group != null)
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public async Task TestResolveSuccessful()
    {
        await TestResolve(false);
    }

    [Fact]
    public async Task TestResolveFails()
    {
        await TestResolve(true);
    }

    private static async Task TestResolve(bool fail)
    {
        AddressResolverGroup<EndPoint> resolverGroup = new TestResolverGroup(fail);
        Bootstrap cb = new Bootstrap();
        cb.Group(group).Channel<LocalChannel>().Handler(new ResolveAddressHandler<EndPoint>(resolverGroup));

        ServerBootstrap sb = new ServerBootstrap();
        sb.Group(group)
                .Channel<LocalServerChannel>()
                .ChildHandler(new ClosingHandler());

        // Start server
        IChannel sc = await sb.BindAsync(RESOLVED, TestContext.Current.CancellationToken);
        Task<IChannel> future = cb.ConnectAsync(UNRESOLVED, TestContext.Current.CancellationToken);
        await ((Task) future).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        try
        {
            if (fail)
            {
                Assert.Same(ERROR, future.Exception?.InnerException);
            }
            else
            {
                Assert.True(future.IsCompletedSuccessfully);
            }
            if (future.IsCompletedSuccessfully)
            {
                await future.Result.CloseAsync(TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            if (future.IsCompletedSuccessfully)
            {
                await future.Result.CloseAsync(TestContext.Current.CancellationToken);
            }
            await sc.CloseAsync(TestContext.Current.CancellationToken);
            resolverGroup.Dispose();
        }
    }

    private sealed class ClosingHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.CloseAsync();
        }
    }

    private sealed class TestResolverGroup : AddressResolverGroup<EndPoint>
    {
        private readonly bool fail;

        internal TestResolverGroup(bool fail)
        {
            this.fail = fail;
        }

        protected override IAddressResolver<EndPoint> NewResolver(IEventExecutor executor)
        {
            return new TestResolver(executor, fail);
        }
    }

    private sealed class TestResolver : AbstractAddressResolver<EndPoint>
    {
        private readonly bool fail;

        internal TestResolver(IEventExecutor executor, bool fail) : base(executor)
        {
            this.fail = fail;
        }

        protected override bool DoIsResolved(EndPoint address)
        {
            return address == RESOLVED;
        }

        protected override Task<EndPoint> DoResolveAsync(EndPoint unresolvedAddress, CancellationToken cancellationToken)
        {
            Assert.Same(UNRESOLVED, unresolvedAddress);
            if (fail)
            {
                return Task.FromException<EndPoint>(ERROR);
            }
            else
            {
                return Task.FromResult<EndPoint>(RESOLVED);
            }
        }

        protected override Task<IReadOnlyList<EndPoint>> DoResolveAllAsync(EndPoint unresolvedAddress,
                                                                            CancellationToken cancellationToken)
        {
            Assert.Fail();
            return null;
        }
    }
}
