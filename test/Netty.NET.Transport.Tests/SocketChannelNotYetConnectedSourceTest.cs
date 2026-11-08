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

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original ordering and independently owned cleanup.
public class SocketChannelNotYetConnectedSourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestShutdownNotYetConnected(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var channel = (TcpSocketChannel)await fixture.Client.Handler(new ChannelInboundHandlerAdapter())
            .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
        try
        {
            CheckThrowable(await Record.ExceptionAsync(() => channel.ShutdownInputAsync()));
            CheckThrowable(await Record.ExceptionAsync(() => channel.ShutdownOutputAsync()));
        }
        finally { await channel.CloseAsync(); }
    }
    private static void CheckThrowable(Exception cause)
    {
        // Depending on OIO / NIO both are ok
        Assert.IsType<SocketException>(cause);
        Assert.Equal(SocketError.NotConnected, ((SocketException)cause).SocketErrorCode);
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task ReadMustBePendingUntilChannelIsActive(bool adaptive) => ReadBeforeConnect(adaptive, true).WaitAsync(TestContext.Current.CancellationToken);

    internal static async Task ReadBeforeConnect(bool adaptive, bool autoRead)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        IChannel server = await fixture.Server.ChildHandler(new SendFortyTwo()).BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        using var reader = new ReadFortyTwo();
        IChannel client = null;
        try
        {
            client = await fixture.Client.Option(ChannelOption.AUTO_READ, autoRead).Handler(reader).ConnectAsync(server.LocalAddress);
            await reader.Read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { if (client != null) await client.CloseAsync(); await server.CloseAsync(); }
    }
    private sealed class SendFortyTwo : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context) =>
            _ = context.WriteAndFlushAsync(Unpooled.Buffer(4).WriteInt(42));
    }
    private sealed class ReadFortyTwo : ChannelInboundHandlerAdapter, IDisposable
    {
        private readonly ByteBuf _input = Unpooled.Buffer(4);
        internal readonly TaskCompletionSource Read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void HandlerAdded(ChannelHandlerContext context)
        {
            Assert.False(context.Channel.IsActive);
            context.Read();
        }
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            try
            {
                var input = Assert.IsAssignableFrom<ByteBuf>(message);
                _input.WriteBytes(input, input.ReadableBytes);
                Assert.True(_input.ReadableBytes <= sizeof(int));
                if (_input.ReadableBytes == sizeof(int)) { Assert.Equal(42, _input.ReadInt()); Read.TrySetResult(); }
            }
            catch (Exception error) { Read.TrySetException(error); }
            finally { ReferenceCountUtil.Release(message); }
        }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => Read.TrySetException(cause);
        public override void ChannelReadComplete(ChannelHandlerContext context)
        {
            // Preserve ByteToMessageDecoder's manual-read continuation when a partial integer has not decoded yet.
            if (!Read.Task.IsCompleted && _input.ReadableBytes > 0 && !context.Channel.Config.IsAutoRead()) context.Read();
            context.FireChannelReadComplete();
        }
        public void Dispose() => _input.Release();
    }
}
#pragma warning restore xUnit1051
