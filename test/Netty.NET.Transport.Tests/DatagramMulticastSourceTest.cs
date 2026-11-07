/*
 * Copyright 2012 The Netty Project
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
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original join/send/leave sequence and independently owned cleanup.
public partial class DatagramMulticastSourceTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task TestMulticast(bool adaptive, bool ipv6) => ipv6
        ? RunIPv6MulticastAsync(adaptive) : RunMulticastAsync(adaptive, AddressFamily.InterNetwork);

    private static async Task RunMulticastAsync(bool adaptive, AddressFamily family, bool mixed = false)
    {
        bool ipv6 = family == AddressFamily.InterNetworkV6;
        var any = ipv6 ? IPAddress.IPv6Any : IPAddress.Any;
        var groupFamily = mixed ? AddressFamily.InterNetwork : family;
        var iface = MulticastNetworkInterface(family, any, mixed);
        if (iface == null) Assert.Skip($"No NetworkInterface found that supports multicast and {family}");
        var handler = new MulticastTestHandler();
        var group = new DefaultEventLoopGroup(1);
        IByteBufAllocator allocator = adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default;
        DatagramChannel server = null, client = null;
        try
        {
            Bootstrap Create(IChannelHandler channelHandler) => new Bootstrap().Group(group).ChannelFactory(() => new DatagramChannel(family, mixed))
                .Option(ChannelOption.ALLOCATOR, allocator).Handler(channelHandler)
                .Option(ChannelOption.IP_MULTICAST_IF, iface).Option(ChannelOption.SO_REUSEADDR, true);
            var sb = Create(new DiscardHandler());
            var cb = Create(handler);
            var bindAddress = InterfaceAddress(iface, family);
            var multicast = MulticastGroup(iface, groupFamily);
            if (mixed && BoundSenderRouteError(iface, bindAddress, multicast) is { } routeError)
            {
                // The original bind can be refused by the kernel for IPv4 multicast. First verify the
                // same failure through the channel, then exercise membership on a routable wildcard bind.
                server = (DatagramChannel)await sb.BindAsync(new IPEndPoint(bindAddress, 0));
                var failure = await Assert.ThrowsAsync<SocketException>(() => server.WriteAndFlushAsync(
                    new DatagramPacket(Unpooled.Buffer(4).WriteInt(1), new IPEndPoint(multicast, 12345))));
                Assert.Equal(routeError, failure.SocketErrorCode);
                Assert.True(server.IsOpen);
                bindAddress = any;
            }
            int attempts = 5;
            for (;;)
            {
                if (server != null) await server.CloseAsync();
                server = (DatagramChannel)await sb.BindAsync(new IPEndPoint(bindAddress, 0));
                // skip the test for OIO, as it fails because of
                // No route to host which makes no sense.
                // Maybe a JDK bug ?
                // CLR has one asynchronous socket backend; there is no separate OIO permutation.
                AssertInterface(server, iface);
                try
                {
                    client = (DatagramChannel)await cb.BindAsync(new IPEndPoint(any, ((IPEndPoint)server.LocalAddress).Port));
                    break;
                }
                catch when (--attempts > 0) { }
            }
            AssertInterface(client, iface);
            var address = new IPEndPoint(multicast, ((IPEndPoint)server.LocalAddress).Port);
            await client.JoinGroupAsync(address, iface);
            await server.WriteAndFlushAsync(new DatagramPacket(Unpooled.Buffer(4).WriteInt(1), address));
            Assert.True(await handler.Await());
            // leave the group
            await client.LeaveGroupAsync(address, iface);
            // sleep a second to make sure we left the group
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            // we should not receive a message anymore as we left the group before
            await server.WriteAndFlushAsync(new DatagramPacket(Unpooled.Buffer(4).WriteInt(1), address));
            if (mixed) await Task.Delay(200, TestContext.Current.CancellationToken);
            await handler.Await();
            client.Config.SetOption(ChannelOption.IP_MULTICAST_LOOP_DISABLED, false);
            server.Config.SetOption(ChannelOption.IP_MULTICAST_LOOP_DISABLED, false);
            Assert.True(client.Config.TryGetOption(ChannelOption.IP_MULTICAST_LOOP_DISABLED, out bool clientDisabled));
            Assert.True(server.Config.TryGetOption(ChannelOption.IP_MULTICAST_LOOP_DISABLED, out bool serverDisabled));
            Assert.False(clientDisabled);
            Assert.False(serverDisabled);
            client.Config.SetOption(ChannelOption.IP_MULTICAST_LOOP_DISABLED, true);
            server.Config.SetOption(ChannelOption.IP_MULTICAST_LOOP_DISABLED, true);
            Assert.True(client.Config.TryGetOption(ChannelOption.IP_MULTICAST_LOOP_DISABLED, out clientDisabled));
            Assert.True(server.Config.TryGetOption(ChannelOption.IP_MULTICAST_LOOP_DISABLED, out serverDisabled));
            Assert.True(clientDisabled);
            Assert.True(serverDisabled);
        }
        finally
        {
            if (server != null) await server.CloseAsync();
            if (client != null) await client.CloseAsync();
            await group.StopAsync();
            if (allocator is AdaptiveByteBufAllocator a) a.Dispose();
        }
    }
    private static void AssertInterface(DatagramChannel channel, NetworkInterface iface)
    {
        Assert.True(channel.Config.TryGetOption(ChannelOption.IP_MULTICAST_IF, out var actual));
        Assert.Equal(iface.Id, actual.Id);
        Assert.True(channel.Config.TryGetOption(ChannelOption.IP_MULTICAST_ADDR, out var address));
        Assert.Contains(iface.GetIPProperties().UnicastAddresses, a => a.Address.Equals(address));
    }
    private static IPAddress InterfaceAddress(NetworkInterface iface, AddressFamily family) => iface.GetIPProperties().UnicastAddresses
        .First(a => a.Address.AddressFamily == family).Address;

    private static IPAddress MulticastGroup(NetworkInterface iface, AddressFamily family)
    {
        if (family == AddressFamily.InterNetwork) return IPAddress.Parse("230.0.0.1");
        var address = IPAddress.Parse("ff01::101");
        address.ScopeId = iface.GetIPProperties().GetIPv6Properties().Index;
        return address;
    }

    private static NetworkInterface MulticastNetworkInterface(AddressFamily family, IPAddress any, bool mixed = false)
    {
        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up || !iface.SupportsMulticast) continue;
            if (mixed && !iface.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                continue;
            foreach (var address in iface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != family) continue;
                using var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
                if (mixed) socket.DualMode = true;
                socket.Bind(new IPEndPoint(any, 0));
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                if (mixed)
                    SetIPv4MulticastInterface(socket, iface);
                else if (family == AddressFamily.InterNetworkV6)
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, iface.GetIPProperties().GetIPv6Properties().Index);
                else
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.Address.GetAddressBytes());
                var groupFamily = mixed ? AddressFamily.InterNetwork : family;
                try { socket.SendTo(new byte[] { 1, 2, 3, 4 }, new IPEndPoint(MulticastGroup(iface, groupFamily), 12345)); return iface; }
                catch (SocketException)
                {
                    // Try the next interface
                }
            }
        }
        return null;
    }
    [Sharable]
    private sealed class DiscardHandler : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, object message)
        {
            // Nothing will be sent.
        }
    }
    [Sharable]
    private sealed class MulticastTestHandler : SimpleChannelInboundHandler<DatagramPacket>
    {
        private readonly TaskCompletionSource _latch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _done;
        private volatile bool _fail;
        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket packet)
        {
            if (_done) _fail = true;
            Assert.Equal(1, packet.Content.ReadInt());
            _latch.TrySetResult();
            // mark the handler as done as we only are supposed to receive one message
            _done = true;
        }
        internal async Task<bool> Await()
        {
            bool success;
            try { await _latch.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); success = true; }
            catch (TimeoutException) { success = false; }
            if (_fail)
            {
                // fail if we receive an message after we are done
                Assert.Fail();
            }
            return success;
        }
    }
}
