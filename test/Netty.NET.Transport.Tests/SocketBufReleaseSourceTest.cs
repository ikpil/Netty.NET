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
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original close/check order and independently owned cleanup.
public class SocketBufReleaseSourceTest
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestBufRelease(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var serverHandler = new BufWriterHandler();
        var clientHandler = new BufWriterHandler();
        IChannel server = null, client = null;
        try
        {
            server = await fixture.Server.ChildHandler(serverHandler).BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
            client = await fixture.Client.Handler(clientHandler).ConnectAsync(server.LocalAddress, cancellationToken: TestContext.Current.CancellationToken);
            // Ensure the server socket accepted the client connection *and* initialized pipeline successfully.
            await serverHandler.Channel.Task.WaitAsync(TestContext.Current.CancellationToken);
            // and then close all sockets.
            await server.CloseAsync();
            await client.CloseAsync();
            await serverHandler.Check();
            await clientHandler.Check();
        }
        finally
        {
            if (client != null) await client.CloseAsync();
            if (server != null) await server.CloseAsync();
            serverHandler.Release(); clientHandler.Release();
        }
    }
    private sealed class BufWriterHandler : SimpleChannelInboundHandler<object>
    {
        internal readonly TaskCompletionSource<IChannel> Channel = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ByteBuf _buffer;
        public override void HandlerAdded(ChannelHandlerContext context) => Channel.TrySetResult(context.Channel);
        public override void ChannelActive(ChannelHandlerContext context)
        {
            var data = new byte[1024]; Random.Shared.NextBytes(data);
            _buffer = context.Allocator.Buffer();
            // call retain on it so it can't be put back on the pool
            _buffer.WriteBytes(data).Retain();
            _ = Observe(context.Channel.WriteAndFlushAsync(_buffer));
        }
        private async Task Observe(Task write)
        { try { await write.ConfigureAwait(false); } catch { } finally { _written.TrySetResult(); } }
        protected override void ChannelRead0(ChannelHandlerContext context, object message)
        {
            // discard
        }
        internal async Task Check()
        { await _written.Task.WaitAsync(TestContext.Current.CancellationToken); Assert.Equal(1, _buffer.ReferenceCount); }
        internal void Release() => _buffer?.Release();
    }
}
#pragma warning restore xUnit1051
