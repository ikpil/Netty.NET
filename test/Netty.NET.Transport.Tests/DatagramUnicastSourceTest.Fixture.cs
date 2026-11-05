/*
 * Copyright 2019 The Netty Project
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
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public partial class DatagramUnicastSourceTest
{
    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly DefaultEventLoopGroup Group = new(1);
        internal readonly AddressFamily Family;
        private readonly IByteBufAllocator _allocator;
        private readonly bool _mapped;
        internal Fixture(bool ipv6, bool adaptive, bool mapped)
        {
            if (ipv6 && !Socket.OSSupportsIPv6) Assert.Skip("IPv6 not supported");
            _mapped = mapped;
            Family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            _allocator = adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default;
        }
        // Explicit use IPv4 wildcard for bind while the channel is using SocketProtocolFamily.INET6.
        internal IPEndPoint Address() => new(_mapped ? IPAddress.Any :
            Family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0);

        internal IPEndPoint Target(IChannel channel)
        {
            var address = (IPEndPoint)channel.LocalAddress;
            return _mapped && (address.Address.Equals(IPAddress.Any) || address.Address.Equals(IPAddress.IPv6Any))
                ? new IPEndPoint(IPAddress.Loopback, address.Port) : address;
        }
        internal Bootstrap Bootstrap(IChannelHandler handler) => new Bootstrap().Group(Group).ChannelFactory(() => new DatagramChannel(Family, _mapped)).Option(ChannelOption.ALLOCATOR, _allocator).Handler(handler);
        public async ValueTask DisposeAsync()
        { await Group.StopAsync(); if (_allocator is AdaptiveByteBufAllocator adaptive) adaptive.Dispose(); }
    }
}
