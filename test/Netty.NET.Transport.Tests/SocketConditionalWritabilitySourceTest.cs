/*
 * Copyright 2018 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Source producer and native channel cleanup have independent lifetimes.
public class SocketConditionalWritabilitySourceTest
{
    private const int ExpectedBytes = 100 * 1024 * 1024;
    private const int MaxWriteChunkSize = 16 * 1024;
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestConditionalWritability(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IChannel server = await fixture.Server.ChildOption(ChannelOption.WRITE_BUFFER_WATER_MARK, new WriteBufferWaterMark(8 * 1024, 16 * 1024))
            .ChildHandler(new ActionChannelInitializer<IChannel>(channel => channel.Pipeline.AddLast(new Writer(received))))
            .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
        IChannel client = null;
        try
        {
            client = await fixture.Client.Handler(new Reader(received)).ConnectAsync(server.LocalAddress, cancellationToken: TestContext.Current.CancellationToken);
            await received.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally { if (client != null) await client.CloseAsync(); await server.CloseAsync(); }
    }
    private sealed class Writer(TaskCompletionSource received) : ChannelDuplexHandler
    {
        private int _bytesWritten;
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { ReferenceCountUtil.Release(message); WriteRemainingBytes(context); }
        public override void Flush(ChannelHandlerContext context)
        { if (context.Channel.IsWritable) WriteRemainingBytes(context); else context.Flush(); }
        public override void ChannelWritabilityChanged(ChannelHandlerContext context)
        { if (context.Channel.IsWritable) WriteRemainingBytes(context); context.FireChannelWritabilityChanged(); }
        private void WriteRemainingBytes(ChannelHandlerContext context)
        {
            while (context.Channel.IsWritable && _bytesWritten < ExpectedBytes)
            {
                int chunk = Math.Min(ExpectedBytes - _bytesWritten, MaxWriteChunkSize);
                _bytesWritten += chunk;
                _ = context.WriteAsync(context.Allocator.Buffer(chunk).WriteZero(chunk));
            }
            context.Flush();
        }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => received.TrySetException(error);
    }
    private sealed class Reader(TaskCompletionSource received) : ChannelInboundHandlerAdapter
    {
        private int _totalRead;
        public override void ChannelActive(ChannelHandlerContext context) => _ = context.WriteAndFlushAsync(context.Allocator.Buffer(1).WriteByte(0));
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            if (message is ByteBuf buffer) { _totalRead += buffer.ReadableBytes; if (_totalRead == ExpectedBytes) received.TrySetResult(); }
            ReferenceCountUtil.Release(message);
        }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => received.TrySetException(error);
    }
}
#pragma warning restore xUnit1051
