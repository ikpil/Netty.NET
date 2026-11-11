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
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Preserve the source's independently owned cleanup.
public class SocketAddressesSourceTest
{
    protected virtual bool Domain => false;

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestAddresses(bool adaptive) => Addresses(adaptive, true, TestContext.Current.CancellationToken);

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestAddressesConnectWithoutLocalAddress(bool adaptive) => Addresses(adaptive, false, TestContext.Current.CancellationToken);

    private EndPoint NewAddress() => Domain ? DomainSocketSourceTest.NewAddress() : new IPEndPoint(IPAddress.Loopback, 0);
    private void AssertAddress(EndPoint address)
    {
        if (Domain)
        {
            Assert.NotNull(Assert.IsType<UnixDomainSocketEndPoint>(address).ToString());
        }
        else
        {
            var inet = Assert.IsType<IPEndPoint>(address);
            Assert.True(inet.Port > 0);
            Assert.False(inet.Address.Equals(IPAddress.Any) || inet.Address.Equals(IPAddress.IPv6Any));
        }
    }
    private async Task Addresses(bool adaptive, bool withLocalAddress, System.Threading.CancellationToken cancellation)
    {
        if (Domain) DomainSocketSourceTest.RequireDomainSockets();
        await using var fixture = new ShutdownFixture(adaptive, Domain);
        var local = new TaskCompletionSource<EndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new TaskCompletionSource<EndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = null, client = null;
        try
        {
            server = await fixture.Server.ChildHandler(new AddressesHandler(local, remote)).BindAsync(NewAddress(), cancellation);
            client = await fixture.Client.Handler(new ChannelInboundHandlerAdapter()).RegisterAsync(cancellation);
            Assert.Null(client.LocalAddress);
            Assert.Null(client.RemoteAddress);
            await client.ConnectAsync(server.LocalAddress, withLocalAddress ? NewAddress() : null, cancellation);
            AssertAddress(client.LocalAddress);
            AssertAddress(client.RemoteAddress);
            AssertAddress(await local.Task.WaitAsync(cancellation));
            AssertAddress(await remote.Task.WaitAsync(cancellation));
        }
        finally { if (client != null) await client.CloseAsync(); if (server != null) await server.CloseAsync(); }
    }
    private sealed class AddressesHandler(TaskCompletionSource<EndPoint> local, TaskCompletionSource<EndPoint> remote) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context)
        {
            local.TrySetResult(context.Channel.LocalAddress);
            remote.TrySetResult(context.Channel.RemoteAddress);
        }
    }
}
#pragma warning restore xUnit1051
