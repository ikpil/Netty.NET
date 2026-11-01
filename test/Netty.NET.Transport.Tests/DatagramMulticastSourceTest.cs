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
public class DatagramMulticastSourceTest
{
    private static readonly IPAddress GroupAddress = IPAddress.Parse("230.0.0.1");
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestMulticast(bool adaptive)
    {
        var iface = MulticastNetworkInterface();
        if (iface == null) Assert.Skip("No NetworkInterface found that supports multicast and IPv4");
        var handler = new MulticastTestHandler();
        var group = new DefaultEventLoopGroup(1);
        IByteBufAllocator allocator = adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default;
        DatagramChannel server = null, client = null;
        try
        {
            Bootstrap Create(IChannelHandler channelHandler) => new Bootstrap().Group(group).Channel<DatagramChannel>()
                .Option(ChannelOption.ALLOCATOR, allocator).Handler(channelHandler)
                .Option(ChannelOption.IP_MULTICAST_IF, iface).Option(ChannelOption.SO_REUSEADDR, true);
            var sb = Create(new DiscardHandler());
            var cb = Create(handler);
            int attempts = 5;
            for (;;)
            {
                if (server != null) await server.CloseAsync();
                server = (DatagramChannel)await sb.BindAsync(new IPEndPoint(InterfaceAddress(iface), 0));
                // skip the test for OIO, as it fails because of
                // No route to host which makes no sense.
                // Maybe a JDK bug ?
                // CLR has one asynchronous socket backend; there is no separate OIO permutation.
                AssertInterface(server, iface);
                try
                {
                    client = (DatagramChannel)await cb.BindAsync(new IPEndPoint(IPAddress.Any, ((IPEndPoint)server.LocalAddress).Port));
                    break;
                }
                catch when (--attempts > 0) { }
            }
            AssertInterface(client, iface);
            var address = new IPEndPoint(GroupAddress, ((IPEndPoint)server.LocalAddress).Port);
            await client.JoinGroupAsync(address, iface);
            await server.WriteAndFlushAsync(new DatagramPacket(Unpooled.Buffer(4).WriteInt(1), address));
            Assert.True(await handler.Await());
            // leave the group
            await client.LeaveGroupAsync(address, iface);
            // sleep a second to make sure we left the group
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            // we should not receive a message anymore as we left the group before
            await server.WriteAndFlushAsync(new DatagramPacket(Unpooled.Buffer(4).WriteInt(1), address));
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
    private static IPAddress InterfaceAddress(NetworkInterface iface) => iface.GetIPProperties().UnicastAddresses
        .First(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Address;
    private static NetworkInterface MulticastNetworkInterface()
    {
        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up || !iface.SupportsMulticast) continue;
            foreach (var address in iface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.Address.GetAddressBytes());
                try { socket.SendTo(new byte[] { 1, 2, 3, 4 }, new IPEndPoint(GroupAddress, 12345)); return iface; }
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
