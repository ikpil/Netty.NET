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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.SocketMultipleConnectSourceTest;

namespace Netty.NET.Transport.Tests;

public class DatagramConnectNotExistsSourceTest
{
    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestConnectNotExists(bool adaptive)
    {
        // Currently not works on windows
        // See https://github.com/netty/netty/issues/11285
        if (OperatingSystem.IsWindows()) Assert.Skip("The original NIO fixture excludes Windows.");

        var cancellation = TestContext.Current.CancellationToken;
        var address = Loopback();
        var group = new DefaultEventLoopGroup(1);
        var failure = new ReadFailure();
        var allocator = SelectAllocator(adaptive);
        try
        {
            var channel = await new Bootstrap().Group(group).ChannelFactory(() => new DatagramChannel(address.AddressFamily))
                .Option(ChannelOption.ALLOCATOR, allocator).Handler(failure)
                .ConnectAsync(new IPEndPoint(address, 65535), cancellationToken: cancellation);
            Assert.True(channel.IsActive);
            await channel.WriteAndFlushAsync(Unpooled.WrappedBuffer("test"u8.ToArray()), cancellation);
            var error = Assert.IsType<SocketException>(await failure.Error.Task.WaitAsync(cancellation));
            Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
            Assert.True(channel.IsOpen);
        }
        finally
        {
            await group.StopAsync();
            if (allocator is AdaptiveByteBufAllocator owned) owned.Dispose();
        }
    }

    private sealed class ReadFailure : ChannelInboundHandlerAdapter
    {
        internal readonly TaskCompletionSource<Exception> Error = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => Error.TrySetResult(error);
    }
}
