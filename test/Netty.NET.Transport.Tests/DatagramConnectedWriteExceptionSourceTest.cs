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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.SocketMultipleConnectSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent teardown.
public class DatagramConnectedWriteExceptionSourceTest
{
    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestWriteThrowsPortUnreachableException(bool adaptive)
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The original NIO fixture excludes Windows.");

        var cancellation = TestContext.Current.CancellationToken;
        var address = Loopback();
        var group = new DefaultEventLoopGroup(2);
        var allocator = SelectAllocator(adaptive);
        var receiver = new FirstMessage();
        try
        {
            var clientBootstrap = new Bootstrap().Group(group).ChannelFactory(() => new DatagramChannel(address.AddressFamily))
                .Option(ChannelOption.ALLOCATOR, allocator).Handler(new IgnorePacket());
            var server = await clientBootstrap.Clone().Option(ChannelOption.SO_BROADCAST, false).Handler(receiver)
                .BindAsync(new IPEndPoint(address, 0), cancellation);
            var client = await clientBootstrap.Option(ChannelOption.AUTO_READ, false)
                .ConnectAsync(server.LocalAddress, cancellationToken: cancellation);

            var first = client.WriteAndFlushAsync(Unpooled.WrappedBuffer("First message"u8.ToArray()), cancellation);
            await receiver.Received.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            await first.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            await server.CloseAsync();

            var writes = new Task<Exception>[10];
            for (int i = 0; i < writes.Length; i++)
            {
                writes[i] = CaptureFailure(client.WriteAndFlushAsync(
                    Unpooled.CopiedBuffer($"Message {i}", Encoding.UTF8), cancellation));
                await Task.Delay(50, cancellation);
            }
            var outcomes = await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            var error = Assert.IsType<SocketException>(outcomes.FirstOrDefault(outcome => outcome != null));
            Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
            Assert.True(client.IsOpen);
        }
        finally
        {
            await group.StopAsync();
            if (allocator is AdaptiveByteBufAllocator owned) owned.Dispose();
        }
    }

    private static async Task<Exception> CaptureFailure(Task write)
    {
        try { await write.ConfigureAwait(false); return null; }
        catch (Exception error) { return error; }
    }

    private sealed class FirstMessage : SimpleChannelInboundHandler<DatagramPacket>
    {
        internal readonly TaskCompletionSource Received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket packet) => Received.TrySetResult();
    }

    private sealed class IgnorePacket : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket packet)
        {
            // no-op
        }
    }
}
