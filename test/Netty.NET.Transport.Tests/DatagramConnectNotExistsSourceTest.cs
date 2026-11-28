/*
 * Copyright 2017 The Netty Project
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
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source's syncUninterruptibly() calls and its close take no cancellation.
public class DatagramConnectNotExistsSourceTest : AbstractClientSocketTest
{
    protected override List<TestsuitePermutation.IBootstrapFactory<Bootstrap>> NewFactories()
    {
        return SocketTestPermutation.INSTANCE.DatagramSocket();
    }

    [Fact(Timeout = 10000)]
    public async Task TestConnectNotExists()
    {
        await Run(TestContext.Current, TestConnectNotExists0);
    }

    // CLR: xUnit1024 forbids overloading the TestConnectNotExists test name.
    private async Task TestConnectNotExists0(Bootstrap cb)
    {
        // Currently not works on windows
        // See https://github.com/netty/netty/issues/11285
        // CLR: assumeFalse is xUnit v3's Assert.SkipWhen, which "will skip the test if condition is true"
        // (https://xunit.net/docs/getting-started/v3/whats-new).
        Assert.SkipWhen(PlatformDependent.IsWindows(), "assumeFalse(PlatformDependent.isWindows())");
        // CLR: the ImmediateEventExecutor Promise<Throwable> is a TaskCompletionSource<Exception>; trySuccess is
        // TrySetResult.
        TaskCompletionSource<Exception> promise =
                new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        cb.Handler(new ExceptionPromiseHandler(promise));
        Task<IChannel> future = cb.ConnectAsync(NetUtil.LOCALHOST, SocketTestPermutation.BAD_PORT);

        try
        {
            IChannel datagramChannel = await future;
            Assert.True(datagramChannel.IsActive);
            await datagramChannel.WriteAndFlushAsync(
                    Unpooled.CopiedBuffer("test", Encoding.ASCII));
            // CLR: OioDatagramChannel is deprecated-not-ported, so every channel takes the source's non-OIO branch.
            // java.net.PortUnreachableException "Signals that an ICMP Port Unreachable message has been received on a
            // connected datagram"
            // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/net/PortUnreachableException.html);
            // Linux reports that ICMP error on the connected UDP socket as ECONNREFUSED, "No receiver was associated
            // with the destination address" (https://man7.org/linux/man-pages/man7/udp.7.html), which the BCL
            // surfaces as SocketError.ConnectionRefused
            // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror).
            SocketException cause = Assert.IsType<SocketException>(await promise.Task);
            Assert.Equal(SocketError.ConnectionRefused, cause.SocketErrorCode);
        }
        finally
        {
            // CLR: a failed connect Task carries no channel; Bootstrap.ConnectAsync closes the channel of a failed
            // connect itself, so only a connected channel is closed here, without waiting, as in the source.
            if (future.IsCompletedSuccessfully)
            {
                _ = future.Result.CloseAsync();
            }
        }
    }

    // CLR: C# has no anonymous classes.
    private sealed class ExceptionPromiseHandler(TaskCompletionSource<Exception> promise) : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            promise.TrySetResult(cause);
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
