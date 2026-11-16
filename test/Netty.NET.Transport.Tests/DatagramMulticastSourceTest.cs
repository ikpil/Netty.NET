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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // The source's sync()/await() calls and its channel teardown take no cancellation.
public partial class DatagramMulticastSourceTest : AbstractDatagramTest
{
    [Fact]
    public async Task TestMulticast()
    {
        await Run(TestContext.Current, TestMulticast0);
    }

    // CLR: xUnit1024 forbids overloading the TestMulticast test name.
    protected virtual async Task TestMulticast0(Bootstrap sb, Bootstrap cb)
    {
        NetworkInterface iface = MulticastNetworkInterface();
        // CLR: assumeTrue is xUnit v3's Assert.SkipUnless, which "will skip the test if condition is false"
        // (https://xunit.net/docs/getting-started/v3/whats-new).
        Assert.SkipUnless(iface != null, "No NetworkInterface found that supports multicast and " +
                             SocketInternetProtocalFamily());

        MulticastTestHandler mhandler = new MulticastTestHandler();

        sb.Handler(new NothingSentHandler());

        cb.Handler(mhandler);

        sb.Option(ChannelOption.IP_MULTICAST_IF, iface);
        sb.Option(ChannelOption.SO_REUSEADDR, true);

        cb.Option(ChannelOption.IP_MULTICAST_IF, iface);
        cb.Option(ChannelOption.SO_REUSEADDR, true);

        // CLR: with an INET6 socket and an INET group (EpollDatagramMulticastIpv6WithIpv4AddrTest, see
        // RunMulticastAsync), the kernel can refuse IPv4 multicast sent from the exact IPv6 interface address the
        // source binds. The same failure is first verified through the channel, then the membership assertions run
        // on a routable wildcard bind.
        IPEndPoint routableAddress = MixedFamily ? await BoundSenderRouteFailure(sb, iface) : null;

        IDatagramChannel sc = null;

        int attempts = 5;
        Task<IChannel> clientFuture;
        do
        {
            if (sc != null)
            {
                await sc.CloseAsync();
            }
            sc = (IDatagramChannel) await sb.BindAsync(routableAddress ?? NewSocketAddress(iface));
            // CLR: OioDatagramChannel is deprecated-not-ported, so no channel takes the source's OIO early return:
            // skip the test for OIO, as it fails because of
            // No route to host which makes no sense.
            // Maybe a JDK bug ?
            AssertNetworkInterface(iface, ((IDatagramChannelConfig) sc.Config).GetNetworkInterface());
            AssertInterfaceAddress(iface, ((IDatagramChannelConfig) sc.Config).GetInterface());

            IPEndPoint addr = (IPEndPoint) sc.LocalAddress;
            // CLR: AbstractBootstrap.localAddress(int inetPort) sets new InetSocketAddress(inetPort), the wildcard
            // address; this port's Bootstrap takes the EndPoint, with the IPAddress.Any wildcard that its
            // BindAsync(int) uses too.
            cb.LocalAddress(new IPEndPoint(IPAddress.Any, addr.Port));
            clientFuture = cb.BindAsync();
            // CLR: ChannelFuture.await() waits without rethrowing a failure; ConfigureAwaitOptions.SuppressThrowing
            // "Avoids throwing an exception at the completion of awaiting a Task that ends in the Faulted or Canceled
            // state" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.configureawaitoptions).
            await ((Task) clientFuture).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        } while (!clientFuture.IsCompletedSuccessfully && --attempts > 0);
        IDatagramChannel cc = (IDatagramChannel) await clientFuture;
        AssertNetworkInterface(iface, ((IDatagramChannelConfig) cc.Config).GetNetworkInterface());
        AssertInterfaceAddress(iface, ((IDatagramChannelConfig) cc.Config).GetInterface());

        IPEndPoint groupAddress = GroupSocketAddress(iface, ((IPEndPoint) sc.LocalAddress).Port);

        await cc.JoinGroupAsync(groupAddress, iface);

        await sc.WriteAndFlushAsync(new DatagramPacket(Unpooled.CopyInt(1), groupAddress));
        Assert.True(await mhandler.Await());

        // leave the group
        await cc.LeaveGroupAsync(groupAddress, iface);

        // sleep a second to make sure we left the group
        // CLR: Thread.sleep is Task.Delay, which "Creates a task that completes after a specified number of
        // milliseconds" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.delay), so the
        // asynchronous body does not block a thread.
        await Task.Delay(1000);

        // we should not receive a message anymore as we left the group before
        await sc.WriteAndFlushAsync(new DatagramPacket(Unpooled.CopyInt(1), groupAddress));
        if (MixedFamily)
        {
            // CLR: on the mixed-family route a late duplicate is given 200 ms to arrive before the duplicate check.
            await Task.Delay(200);
        }
        await mhandler.Await();

        ((IDatagramChannelConfig) cc.Config).SetLoopbackModeDisabled(false);
        ((IDatagramChannelConfig) sc.Config).SetLoopbackModeDisabled(false);

        Assert.False(((IDatagramChannelConfig) cc.Config).IsLoopbackModeDisabled());
        Assert.False(((IDatagramChannelConfig) sc.Config).IsLoopbackModeDisabled());

        ((IDatagramChannelConfig) cc.Config).SetLoopbackModeDisabled(true);
        ((IDatagramChannelConfig) sc.Config).SetLoopbackModeDisabled(true);

        Assert.True(((IDatagramChannelConfig) cc.Config).IsLoopbackModeDisabled());
        Assert.True(((IDatagramChannelConfig) sc.Config).IsLoopbackModeDisabled());

        // CLR: awaitUninterruptibly() waits without rethrowing a failure, as ConfigureAwaitOptions.SuppressThrowing.
        await sc.CloseAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await cc.CloseAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    // CLR: java.net.NetworkInterface.equals compares the interfaces; a NetworkInterface is a separate object per
    // enumeration ("the GetAllNetworkInterfaces method returns an array that contains one instance of this class for
    // each network interface", https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.networkinterface),
    // so assertEquals(iface, ...) compares NetworkInterface.Id, "the identifier of the network adapter"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.networkinterface.id).
    private static void AssertNetworkInterface(NetworkInterface expected, NetworkInterface actual)
    {
        Assert.Equal(expected.Id, actual.Id);
    }

    // CLR: NetworkInterface.getInetAddresses() is the interface's GetIPProperties().UnicastAddresses.
    private static void AssertInterfaceAddress(NetworkInterface networkInterface, IPAddress expected)
    {
        foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
        {
            if (expected.Equals(address.Address))
            {
                return;
            }
        }
        Assert.Fail();
    }

    // CLR: the CountDownLatch(1) is a TaskCompletionSource; await() waits ten seconds for it.
    [Sharable]
    private sealed class MulticastTestHandler : SimpleChannelInboundHandler<DatagramPacket>
    {
        private readonly TaskCompletionSource latch = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool done;
        private volatile bool fail;

        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket msg)
        {
            if (done)
            {
                fail = true;
            }

            Assert.Equal(1, msg.Content.ReadInt());

            latch.TrySetResult();

            // mark the handler as done as we only are supposed to receive one message
            done = true;
        }

        public async Task<bool> Await()
        {
            bool success;
            try
            {
                await latch.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                success = true;
            }
            catch (TimeoutException)
            {
                success = false;
            }
            if (fail)
            {
                // fail if we receive an message after we are done
                Assert.Fail();
            }
            return success;
        }
    }

    protected override List<TestsuitePermutation.IBootstrapComboFactory<Bootstrap, Bootstrap>> NewFactories()
    {
        return SocketTestPermutation.INSTANCE.Datagram(SocketInternetProtocalFamily());
    }

    // CLR: InetAddress.getByName of the wildcard literals is IPAddress.Parse.
    private IPEndPoint NewAnySocketAddress()
    {
        switch (SocketInternetProtocalFamily())
        {
            case Channels.Sockets.SocketProtocolFamily.Inet:
                return new IPEndPoint(IPAddress.Parse("0.0.0.0"), 0);
            case Channels.Sockets.SocketProtocolFamily.Inet6:
                return new IPEndPoint(IPAddress.Parse("::"), 0);
            default:
                // CLR: java.lang.AssertionError is UnreachableException, as in AbstractDatagramTest.
                throw new UnreachableException();
        }
    }

    private IPEndPoint NewSocketAddress(NetworkInterface iface)
    {
        foreach (UnicastIPAddressInformation addressInformation in iface.GetIPProperties().UnicastAddresses)
        {
            IPAddress address = addressInformation.Address;
            if (IsSupported(SocketInternetProtocalFamily(), address))
            {
                return new IPEndPoint(address, 0);
            }
        }
        throw new UnreachableException();
    }

    private NetworkInterface MulticastNetworkInterface()
    {
        foreach (NetworkInterface iface in NetUtil.NETWORK_INTERFACES)
        {
            // CLR: isUp() and supportsMulticast() are OperationalStatus.Up and SupportsMulticast.
            if (iface.OperationalStatus == OperationalStatus.Up && iface.SupportsMulticast)
            {
                // CLR: an INET group needs an IPv4 address of the interface for IP_MULTICAST_IF (see
                // SetNetworkInterface); with an INET6 socket family the interface may have none, and then cannot send
                // the probe, which the source reports as the IOException that tries the next interface.
                if (GroupInternetProtocalFamily() == Channels.Sockets.SocketProtocolFamily.Inet &&
                        !iface.GetIPProperties().UnicastAddresses.Any(
                                a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                {
                    continue;
                }
                foreach (UnicastIPAddressInformation addressInformation in iface.GetIPProperties().UnicastAddresses)
                {
                    IPAddress address = addressInformation.Address;
                    if (IsSupported(SocketInternetProtocalFamily(), address))
                    {
                        // CLR: java.net.MulticastSocket(SocketAddress) is a BCL UDP Socket of the bind address's
                        // family bound to that address. The JDK socket is dual-stack; the BCL socket enables DualMode
                        // only when its INET6 family must reach an INET group. setReuseAddress is
                        // SocketOptionName.ReuseAddress, "Allows the socket to be bound to an address that is already
                        // in use" (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketoptionname).
                        IPEndPoint anyAddress = NewAnySocketAddress();
                        Socket socket = new Socket(anyAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                        if (MixedFamily)
                        {
                            socket.DualMode = true;
                        }
                        socket.Bind(anyAddress);
                        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        SetNetworkInterface(socket, iface);
                        try
                        {
                            socket.SendTo(new byte[] { 1, 2, 3, 4 }, 4, SocketFlags.None,
                                    GroupSocketAddress(iface, 12345));
                            return iface;
                        }
                        // CLR: java.io.IOException covers the JDK SocketException; the BCL send reports SocketException.
                        catch (SocketException)
                        {
                            // Try the next interface
                        }
                        finally
                        {
                            socket.Close();
                        }
                    }
                }
            }
        }
        return null;
    }

    // CLR: MulticastSocket.setNetworkInterface(iface) selects the interface for outgoing multicast packets. The BCL
    // option is SocketOptionName.MulticastInterface, "Set the interface for outgoing multicast packets"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketoptionname): IP_MULTICAST_IF takes an
    // IPv4 address of the interface for an INET group, IPV6_MULTICAST_IF the interface index for an INET6 group.
    private void SetNetworkInterface(Socket socket, NetworkInterface iface)
    {
        if (GroupInternetProtocalFamily() == Channels.Sockets.SocketProtocolFamily.Inet)
        {
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                    InterfaceAddress(iface, AddressFamily.InterNetwork).GetAddressBytes());
        }
        else
        {
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface,
                    iface.GetIPProperties().GetIPv6Properties().Index);
        }
    }

    private string GroupAddress()
    {
        return GroupInternetProtocalFamily() == Channels.Sockets.SocketProtocolFamily.Inet ?
                "230.0.0.1" : "FF01:0:0:0:0:0:0:101";
    }

    // CLR: SocketUtils.socketAddress(groupAddress(), port) and new InetSocketAddress(groupAddress(), port) resolve the
    // group literal; IPAddress.Parse parses it. The interface-local IPv6 group also carries the selected interface's
    // index as its scope id, which "identifies a network interface"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.-ctor).
    private IPEndPoint GroupSocketAddress(NetworkInterface iface, int port)
    {
        IPAddress address = IPAddress.Parse(GroupAddress());
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            address.ScopeId = iface.GetIPProperties().GetIPv6Properties().Index;
        }
        return new IPEndPoint(address, port);
    }

    // CLR: the first address of the family on the interface; DatagramMulticastSourceTest.Mixed.cs uses it too.
    private static IPAddress InterfaceAddress(NetworkInterface iface, AddressFamily family) =>
        iface.GetIPProperties().UnicastAddresses.First(a => a.Address.AddressFamily == family).Address;

    // CLR: true only for the EpollDatagramMulticastIpv6WithIpv4AddrTest overrides (INET6 socket, INET group).
    private bool MixedFamily => SocketInternetProtocalFamily() != GroupInternetProtocalFamily();

    // CLR: see TestMulticast0. Returns the IPv6 wildcard to bind instead of the interface address when a socket bound
    // to that address cannot route the INET group (BoundSenderRouteError in DatagramMulticastSourceTest.Mixed.cs),
    // after verifying that the channel reports the same error and stays open.
    private async Task<IPEndPoint> BoundSenderRouteFailure(Bootstrap sb, NetworkInterface iface)
    {
        IPEndPoint bindAddress = NewSocketAddress(iface);
        IPEndPoint multicast = GroupSocketAddress(iface, 12345);
        if (BoundSenderRouteError(iface, bindAddress.Address, multicast.Address) is not { } routeError)
        {
            return null;
        }
        IChannel server = await sb.BindAsync(bindAddress);
        try
        {
            SocketException failure = await Assert.ThrowsAsync<SocketException>(() => server.WriteAndFlushAsync(
                    new DatagramPacket(Unpooled.CopyInt(1), multicast)));
            Assert.Equal(routeError, failure.SocketErrorCode);
            Assert.True(server.IsOpen);
        }
        finally
        {
            await server.CloseAsync();
        }
        return new IPEndPoint(IPAddress.IPv6Any, 0);
    }

    // CLR: entry point of the TestMixedFamilyMulticast theory in DatagramMulticastSourceTest.Mixed.cs, the counterpart
    // of EpollDatagramMulticastIpv6WithIpv4AddrTest. That fixture extends DatagramMulticastTest and overrides
    // socketInternetProtocalFamily() to INET6 and groupInternetProtocalFamily() to INET; the private
    // FamilyOverrideTest carries those overrides and runs the inherited testMulticast body through the harness with
    // the theory's allocator row. DatagramMulticastIPv6SourceTest inherits the theory but is not that fixture's base
    // class, so the theory is skipped there.
    private async Task RunMulticastAsync(bool adaptive, AddressFamily family, bool mixed = false)
    {
        Assert.SkipUnless(GetType() == typeof(DatagramMulticastSourceTest),
                "EpollDatagramMulticastIpv6WithIpv4AddrTest extends DatagramMulticastTest only.");
        FamilyOverrideTest test = new FamilyOverrideTest(family, mixed,
                TestsuitePermutation.Allocator()[adaptive ? 1 : 0]);
        await test.Run(TestContext.Current, test.TestMulticast0);
    }

    // CLR: private, so xUnit does not discover it as a test class.
    private sealed class FamilyOverrideTest : DatagramMulticastSourceTest
    {
        private readonly SocketProtocolFamily socketFamily;
        private readonly SocketProtocolFamily groupFamily;
        private readonly IByteBufAllocator allocator;

        public FamilyOverrideTest(AddressFamily family, bool mixed, IByteBufAllocator allocator)
        {
            socketFamily = family == AddressFamily.InterNetworkV6 ?
                    Channels.Sockets.SocketProtocolFamily.Inet6 : Channels.Sockets.SocketProtocolFamily.Inet;
            groupFamily = mixed ? Channels.Sockets.SocketProtocolFamily.Inet : socketFamily;
            this.allocator = allocator;
        }

        protected override List<IByteBufAllocator> NewAllocators()
        {
            return new List<IByteBufAllocator> { allocator };
        }

        protected override SocketProtocolFamily GroupInternetProtocalFamily()
        {
            return groupFamily;
        }

        protected override SocketProtocolFamily SocketInternetProtocalFamily()
        {
            return socketFamily;
        }
    }

    // CLR: C# has no anonymous classes; the source's anonymous handler overriding isSharable() is this class.
    private sealed class NothingSentHandler : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            // Nothing will be sent.
        }

        public override bool IsSharable => true;
    }
}
#pragma warning restore xUnit1051
