/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source closes both channels without waiting, independently of cancellation.
public class SocketMultipleConnectSourceTest : AbstractSocketTest
{
    [Fact(Timeout = 30000)]
    public async Task TestMultipleConnect()
    {
        await Run(TestContext.Current, TestMultipleConnect0);
    }

    // CLR: xUnit1024 forbids overloading the TestMultipleConnect test name.
    private async Task TestMultipleConnect0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel sc = null;
        IChannel cc = null;

        try
        {
            sb.ChildHandler(new ChannelInboundHandlerAdapter());
            sc = await sb.BindAsync(NetUtil.LOCALHOST, 0);

            cb.Handler(new ChannelInboundHandlerAdapter());
            cc = await cb.RegisterAsync();
            await cc.ConnectAsync(sc.LocalAddress);
            // CLR: java.nio.channels.AlreadyConnectedException has no BCL type; the CLR socket reports the same
            // condition as SocketException with SocketError.IsConnected
            // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror).
            SocketException cause = await Assert.ThrowsAsync<SocketException>(() => cc.ConnectAsync(sc.LocalAddress));
            Assert.Equal(SocketError.IsConnected, cause.SocketErrorCode);
        }
        finally
        {
            if (cc != null)
            {
                _ = cc.CloseAsync();
            }

            if (sc != null)
            {
                _ = sc.CloseAsync();
            }
        }
    }

    protected override List<TestsuitePermutation.IBootstrapComboFactory<ServerBootstrap, Bootstrap>> NewFactories()
    {
        List<TestsuitePermutation.IBootstrapComboFactory<ServerBootstrap, Bootstrap>> factories
                = new List<TestsuitePermutation.IBootstrapComboFactory<ServerBootstrap, Bootstrap>>();

        foreach (TestsuitePermutation.IBootstrapComboFactory<ServerBootstrap, Bootstrap> comboFactory
                in SocketTestPermutation.INSTANCE.SocketWithFastOpen())
        {
            IEventLoopGroup group = comboFactory.NewClientInstance().Config.Group;
            if (group is IIoEventLoopGroup && ((IIoEventLoopGroup) group).IsIoType(typeof(NioIoHandler)))
            {
                factories.Add(comboFactory);
            }
        }

        return factories;
    }

    // CLR: shared helpers of the earlier per-test fixtures, still used by SocketConnectTimingContractTest.
    internal static IByteBufAllocator SelectAllocator(bool adaptive)
    {
        return adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default;
    }

    internal static IPAddress Loopback()
    {
        return Socket.OSSupportsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
