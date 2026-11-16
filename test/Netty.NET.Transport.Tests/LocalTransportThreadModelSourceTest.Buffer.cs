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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent teardown.
public partial class LocalTransportThreadModelSourceTest
{
    [Fact(Timeout = 30000, Skip = "Disabled in the original Netty fixture")]
    public async Task TestConcurrentMessageBufferAccess()
    {
        var token = TestContext.Current.CancellationToken;
        var servers = new MultiThreadIoEventLoopGroup(4, LocalIoHandler.NewFactory());
        var clients = new MultiThreadIoEventLoopGroup(4, new DefaultThreadFactory("l"), LocalIoHandler.NewFactory());
        var executors = Enumerable.Range(1, 5)
            .Select(i => new DefaultEventExecutorGroup(4, new DefaultThreadFactory($"e{i}"))).ToArray();
        try
        {
            var server = await new ServerBootstrap().Group(servers).Channel<LocalServerChannel>()
                .ChildHandler(new DiscardInput()).BindAsync(LocalAddress.ANY, token);
            var channel = new LocalChannel();
            // inbound:  int -> byte[4] -> int -> int -> byte[4] -> int -> /dev/null
            // outbound: int -> int -> byte[4] -> int -> int -> byte[4] -> /dev/null
            var handlers = new[] { new MessageStage(Conversion.Encode), new MessageStage(Conversion.Decode),
                new MessageStage(Conversion.Forward), new MessageStage(Conversion.Encode),
                new MessageStage(Conversion.Decode), new MessageStage(Conversion.Discard) };
            channel.Pipeline.AddLast(handlers[0]);
            for (int i = 1; i < handlers.Length; i++)
                channel.Pipeline.AddLast(executors[i - 1], $"stage{i + 1}", handlers[i]);

            await clients.RegisterAsync(channel, token);
            await channel.ConnectAsync(server.LocalAddress, cancellationToken: token);
            const int rounds = 1024;
            const int elementsPerRound = 8192;
            const int total = rounds * elementsPerRound;

            for (int offset = 0; offset < total; offset += elementsPerRound)
            {
                int start = offset;
                channel.EventLoop.Execute(() =>
                {
                    for (int i = start; i < start + elementsPerRound; i++)
                        channel.Pipeline.FireChannelRead(i);
                });
            }
            await WaitForCountsAsync(handlers, total, inbound: true, token);

            var lastExecutor = channel.Pipeline.Context(handlers[^1]).Executor;
            for (int offset = 0; offset < total; offset += elementsPerRound)
            {
                int start = offset;
                lastExecutor.Execute(() =>
                {
                    for (int i = start; i < start + elementsPerRound; i++)
                        _ = channel.WriteAsync(i);
                    channel.Flush();
                });
            }
            await WaitForCountsAsync(handlers, total, inbound: false, token);
            await channel.CloseAsync(token);
        }
        finally
        {
            await clients.StopAsync();
            await Task.WhenAll(executors.Select(group => group.StopAsync()).Append(servers.StopAsync()));
        }
    }

    private static async Task WaitForCountsAsync(MessageStage[] handlers, int total, bool inbound, CancellationToken token)
    {
        while (handlers.Any(handler => (inbound ? handler.InboundCount : handler.OutboundCount) < total))
        {
            foreach (var handler in handlers)
                if (handler.Error is { } error) throw error;
            await Task.Delay(10, token);
        }
    }

    private enum Conversion
    {
        /**
         * Converts integers into a binary stream.
         */
        Encode,
        /**
         * Converts a binary stream into integers.
         */
        Decode,
        /**
         * Simply forwards the received object to the next handler.
         */
        Forward,
        /**
         * Discards all received messages.
         */
        Discard
    }

    private sealed class MessageStage(Conversion conversion) : ChannelDuplexHandler
    {
        private Thread _thread;
        private int _inboundCount;
        private int _outboundCount;
        private Exception _error;
        internal int InboundCount => Volatile.Read(ref _inboundCount);
        internal int OutboundCount => Volatile.Read(ref _outboundCount);
        internal Exception Error => Volatile.Read(ref _error);

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            _thread ??= Thread.CurrentThread;
            Assert.Same(_thread, Thread.CurrentThread);
            if (conversion == Conversion.Decode)
            {
                var input = (ByteBuf)msg;
                try
                {
                    int count = input.ReadableBytes / 4;
                    for (int i = 0; i < count; i++)
                    {
                        int value = input.ReadInt();
                        Assert.Equal(_inboundCount++, value);
                        ctx.FireChannelRead(value);
                    }
                }
                finally { input.Release(); }
                return;
            }

            int number = (int)msg;
            Assert.Equal(_inboundCount++, number);
            if (conversion == Conversion.Encode) ctx.FireChannelRead(ctx.Alloc().Buffer(4).WriteInt(number));
            else if (conversion == Conversion.Forward) ctx.FireChannelRead(msg);
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken token)
        {
            Assert.Same(_thread, Thread.CurrentThread);
            if (conversion == Conversion.Encode)
            {
                // Don't let the write request go to the server-side channel - just swallow.
                bool swallow = ReferenceEquals(this, ctx.Pipeline.First);
                var input = (ByteBuf)msg;
                try
                {
                    int count = input.ReadableBytes / 4;
                    for (int i = 0; i < count; i++)
                    {
                        int value = input.ReadInt();
                        Assert.Equal(_outboundCount++, value);
                        if (!swallow) _ = ctx.WriteAsync(value, token);
                    }
                    return ctx.WriteAndFlushAsync(Unpooled.EmptyBuffer, token);
                }
                finally { input.Release(); }
            }

            int number = (int)msg;
            Assert.Equal(_outboundCount++, number);
            return ctx.WriteAsync(conversion == Conversion.Decode ? ctx.Alloc().Buffer(4).WriteInt(number) : msg, token);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Interlocked.CompareExchange(ref _error, cause, null);
            //System.err.print("[" + Thread.currentThread().getName() + "] ");
            //cause.printStackTrace();
            if (conversion == Conversion.Forward) Console.Error.WriteLine($"[{Thread.CurrentThread.Name}] {cause}");
            ctx.FireExceptionCaught(cause);
        }
    }
}
