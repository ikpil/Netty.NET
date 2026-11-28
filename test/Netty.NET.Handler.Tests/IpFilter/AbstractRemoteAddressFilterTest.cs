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
using Netty.NET.Handler.IpFilter;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Tests.IpFilter;

// CLR: RuntimeException is Exception; assertThrows(RuntimeException.class,...) is Assert.Throws<Exception>, whose
// exact-type check holds because the recorded exception is that Exception. SocketUtils.socketAddress of a literal
// is new IPEndPoint(IPAddress.Parse(...), port), as in the codec ports, and remoteAddress0() is RemoteAddress0().
public class AbstractRemoteAddressFilterTest
{

    [Fact]
    public void ChannelIsClosedWhenAcceptThrows()
    {
        Exception boom = new Exception("boom");
        AbstractRemoteAddressFilter<IPEndPoint> filter = new ThrowingFilter(boom);

        EmbeddedChannel channel = new InetEmbeddedChannel(filter);

        // The channel must be closed rather than left open and unfiltered when accept() throws.
        Assert.False(channel.IsActive);
        Assert.False(channel.IsOpen);

        Assert.Throws<Exception>(() =>
        {
            channel.CheckException();
        });
    }

    private sealed class ThrowingFilter : AbstractRemoteAddressFilter<IPEndPoint>
    {
        private readonly Exception boom;

        internal ThrowingFilter(Exception boom)
        {
            this.boom = boom;
        }

        protected override bool Accept(IChannelHandlerContext ctx, IPEndPoint remoteAddress)
        {
            throw boom;
        }
    }

    private sealed class InetEmbeddedChannel : EmbeddedChannel
    {
        internal InetEmbeddedChannel(params IChannelHandler[] handlers) : base(handlers)
        {
        }

        protected override EndPoint RemoteAddress0()
        {
            return IsActive ? new IPEndPoint(IPAddress.Parse("10.0.0.1"), 1234) : null;
        }
    }
}
