/*
 * Copyright 2020 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Handler.Codec.HAProxy.Tests;

// CLR: the Transport port's bootstrap API is asynchronous, so sync() is an awaited BindAsync/ConnectAsync/
// WriteAndFlushAsync/CloseAsync/ShutdownGracefullyAsync; the raw ChannelInitializer and the anonymous
// SimpleChannelInboundHandler are the named nested classes ChildInitializer and MessageHandler; CountDownLatch is a
// TaskCompletionSource awaited for 5 seconds (latch.await(5, SECONDS)) and AtomicReference a Volatile field.
// LocalAddress(HAProxyIntegrationTest.class) is LocalAddress(typeof(HAProxyIntegrationTest)).
public class HAProxyIntegrationTest
{

    [Fact]
    public async Task TestBasicCase()
    {
        TaskCompletionSource latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MessageHolder msgHolder = new MessageHolder();
        LocalAddress localAddress = new LocalAddress(typeof(HAProxyIntegrationTest));

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(LocalIoHandler.NewFactory());
        ServerBootstrap sb = new ServerBootstrap();
        sb.Channel<LocalServerChannel>()
          .Group(group)
          .ChildHandler(new ChildInitializer(latch, msgHolder));
        IChannel serverChannel = await sb.BindAsync(localAddress, TestContext.Current.CancellationToken);

        Bootstrap b = new Bootstrap();
        IChannel clientChannel = await b.Channel<LocalChannel>()
                                 .Handler(HAProxyMessageEncoder.INSTANCE)
                                 .Group(group)
                                 .ConnectAsync(localAddress, TestContext.Current.CancellationToken);

        try
        {
            HAProxyMessage message = new HAProxyMessage(
                    HAProxyProtocolVersion.V1, HAProxyCommand.PROXY, HAProxyProxiedProtocol.TCP4,
                    "192.168.0.1", "192.168.0.11", 56324, 443);
            await clientChannel.WriteAndFlushAsync(message, TestContext.Current.CancellationToken);

            Assert.True(await Task.WhenAny(latch.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
                    == latch.Task);
            HAProxyMessage readMessage = msgHolder.Get();

            Assert.Equal(message.ProtocolVersion(), readMessage.ProtocolVersion());
            Assert.Equal(message.Command(), readMessage.Command());
            Assert.Equal(message.ProxiedProtocol(), readMessage.ProxiedProtocol());
            Assert.Equal(message.SourceAddress(), readMessage.SourceAddress());
            Assert.Equal(message.DestinationAddress(), readMessage.DestinationAddress());
            Assert.Equal(message.SourcePort(), readMessage.SourcePort());
            Assert.Equal(message.DestinationPort(), readMessage.DestinationPort());

            readMessage.Release();
        }
        finally
        {
            await clientChannel.CloseAsync(TestContext.Current.CancellationToken);
            await serverChannel.CloseAsync(TestContext.Current.CancellationToken);
            await group.ShutdownGracefullyAsync();
        }
    }

    private sealed class MessageHolder
    {
        private HAProxyMessage value;

        internal HAProxyMessage Get()
        {
            return Volatile.Read(ref value);
        }

        internal void Set(HAProxyMessage newValue)
        {
            Volatile.Write(ref value, newValue);
        }
    }

    private sealed class ChildInitializer : ChannelInitializer<IChannel>
    {
        private readonly TaskCompletionSource latch;
        private readonly MessageHolder msgHolder;

        internal ChildInitializer(TaskCompletionSource latch, MessageHolder msgHolder)
        {
            this.latch = latch;
            this.msgHolder = msgHolder;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(new HAProxyMessageDecoder());
            ch.Pipeline.AddLast(new MessageHandler(latch, msgHolder));
        }
    }

    private sealed class MessageHandler : SimpleChannelInboundHandler<HAProxyMessage>
    {
        private readonly TaskCompletionSource latch;
        private readonly MessageHolder msgHolder;

        internal MessageHandler(TaskCompletionSource latch, MessageHolder msgHolder)
        {
            this.latch = latch;
            this.msgHolder = msgHolder;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, HAProxyMessage msg)
        {
            msgHolder.Set(msg.Retain());
            latch.TrySetResult();
        }
    }
}
