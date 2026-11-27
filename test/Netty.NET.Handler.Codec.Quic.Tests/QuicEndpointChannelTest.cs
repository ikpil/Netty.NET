/*
 * Copyright 2026 The Netty Project
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
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR-only tests of QuicEndpointChannel (no Java counterpart; the channel takes the datagram channel's place,
// docs/codec-classes-quic-porting.md).
public class QuicEndpointChannelTest : AbstractQuicTest
{
    // An event loop that shuts down closes the datagram channel under a Java QUIC codec (NioIoHandler closes its
    // registered channels); the endpoint closes from a shutdown hook of its event loop.
    [Theory(Timeout = TIMEOUT)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndpointClosesWhenItsEventLoopShutsDown(bool server)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        IChannelHandler codec = server
            ? QuicTestUtils.NewQuicServerBuilder(ImmediateExecutor.INSTANCE)
                .Handler(QuicTestUtils.NOOP_HANDLER)
                .StreamHandler(QuicTestUtils.NOOP_HANDLER)
                .TokenHandler(InsecureQuicTokenHandler.INSTANCE)
                .Build()
            : QuicTestUtils.NewQuicClientBuilder(ImmediateExecutor.INSTANCE).Build();
        IChannel endpoint = await new Bootstrap()
            .Group(group)
            .Channel<QuicEndpointChannel>()
            .Handler(codec)
            .BindAsync(new IPEndPoint(NetUtil.LOCALHOST4, 0), cancellationToken);
        Assert.True(endpoint.IsActive);

        await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5)).WaitAsync(cancellationToken);
        await endpoint.CloseFuture.WaitAsync(cancellationToken);
        Assert.False(endpoint.IsOpen);
    }
}
