/*
 * Copyright 2023 The Netty Project
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

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source's bind/register/connect/close calls do not observe the test cancellation.
// CLR: the source class is abstract and only its concrete transport subclasses run it: EpollSocketAddressesTest (TCP)
// and EpollDomainSocketAddressesTest (AF_UNIX). Epoll is not ported and the NIO testsuite has no concrete TCP
// subclass, so this class stays concrete and runs the TCP variant itself, with EpollSocketAddressesTest's
// newFactories() and assertAddress(); its DomainSocketAddressesSourceTest subclass selects the
// EpollDomainSocketAddressesTest overrides through the Domain hook.
public class SocketAddressesSourceTest : AbstractSocketTest
{
    protected virtual bool Domain => false;

    [Fact(Timeout = 30000)]
    public async Task TestAddresses()
    {
        await Run(TestContext.Current, (serverBootstrap, bootstrap) => TestAddresses0(serverBootstrap, bootstrap, true));
    }

    [Fact(Timeout = 30000)]
    public async Task TestAddressesConnectWithoutLocalAddress()
    {
        await Run(TestContext.Current, (serverBootstrap, bootstrap) => TestAddresses0(serverBootstrap, bootstrap, false));
    }

    // CLR: the source's abstract assertAddress(SocketAddress) takes the bodies of its two concrete subclasses.
    protected virtual void AssertAddress(EndPoint address)
    {
        if (Domain)
        {
            // CLR: EpollDomainSocketAddressesTest asserts a non-null DomainSocketAddress.path();
            // UnixDomainSocketEndPoint.ToString() "Gets the path represented by this UnixDomainSocketEndPoint
            // instance" (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.unixdomainsocketendpoint.tostring).
            Assert.NotNull(((UnixDomainSocketEndPoint) address).ToString());
            return;
        }
        // CLR: EpollSocketAddressesTest's body. InetAddress.isAnyLocalAddress() checks "if the InetAddress is a
        // wildcard address" (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/net/InetAddress.html);
        // the CLR wildcard addresses are IPAddress.Any and IPAddress.IPv6Any, which make a socket "listen for client
        // activity on all network interfaces" (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.any,
        // https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.ipv6any).
        Assert.True(((IPEndPoint) address).Port > 0);
        Assert.False(((IPEndPoint) address).Address.Equals(IPAddress.Any)
                || ((IPEndPoint) address).Address.Equals(IPAddress.IPv6Any));
    }

    // CLR: xUnit1024 forbids overloading the TestAddresses test name.
    private async Task TestAddresses0(ServerBootstrap sb, Bootstrap cb, bool withLocalAddress)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            // CLR: ImmediateEventExecutor.INSTANCE.newPromise() is a TaskCompletionSource<EndPoint>; setSuccess(...)
            // is SetResult(...) and get() awaits its Task.
            TaskCompletionSource<EndPoint> localAddressPromise =
                    new TaskCompletionSource<EndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<EndPoint> remoteAddressPromise =
                    new TaskCompletionSource<EndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            serverChannel = await sb.ChildHandler(new AddressesHandler(localAddressPromise, remoteAddressPromise))
                    .BindAsync();

            clientChannel = await cb.Handler(new ChannelInboundHandlerAdapter()).RegisterAsync();

            Assert.Null(clientChannel.LocalAddress);
            Assert.Null(clientChannel.RemoteAddress);

            if (withLocalAddress)
            {
                await clientChannel.ConnectAsync(serverChannel.LocalAddress, NewSocketAddress());
            }
            else
            {
                await clientChannel.ConnectAsync(serverChannel.LocalAddress);
            }

            AssertAddress(clientChannel.LocalAddress);
            AssertAddress(clientChannel.RemoteAddress);

            AssertAddress(await localAddressPromise.Task);
            AssertAddress(await remoteAddressPromise.Task);
        }
        finally
        {
            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }
            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
            }
        }
    }

    // CLR: C# has no anonymous classes; the source's anonymous ChannelInboundHandlerAdapter is this nested class,
    // whose constructor parameters carry the captured promises.
    private sealed class AddressesHandler(
            TaskCompletionSource<EndPoint> localAddressPromise, TaskCompletionSource<EndPoint> remoteAddressPromise)
            : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            localAddressPromise.SetResult(ctx.Channel.LocalAddress);
            remoteAddressPromise.SetResult(ctx.Channel.RemoteAddress);
        }
    }

    // CLR: EpollSocketAddressesTest.newFactories() returns socketWithFastOpen(), which equals socket() for NIO because
    // clientSocketWithFastOpen() returns clientSocket(). The DomainSocketAddressesSourceTest subclass is the
    // EpollDomainSocketAddressesTest counterpart; Java overrides newFactories()/newSocketAddress() there, this port
    // selects the same overrides through the Domain hook.
    protected override List<TestsuitePermutation.IBootstrapComboFactory<ServerBootstrap, Bootstrap>> NewFactories()
    {
        if (!Domain)
        {
            return SocketTestPermutation.INSTANCE.SocketWithFastOpen();
        }
        DomainSocketSourceTest.RequireDomainSockets();
        return DomainSocketTestPermutation.INSTANCE.DomainSocket();
    }

    protected override EndPoint NewSocketAddress()
    {
        return Domain ? DomainSocketTestPermutation.NewDomainSocketAddress() : base.NewSocketAddress();
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
