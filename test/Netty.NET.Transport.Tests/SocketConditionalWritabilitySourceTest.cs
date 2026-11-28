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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, whose latch wait has no timeout in the source.
#pragma warning disable xUnit1051 // The source's bind/connect/close calls and latch wait do not observe the test cancellation.
public class SocketConditionalWritabilitySourceTest : AbstractSocketTest
{
    [Fact(Timeout = 30000)]
    public async Task TestConditionalWritability()
    {
        await Run(TestContext.Current, TestConditionalWritability0);
    }

    // CLR: xUnit1024 forbids overloading the TestConditionalWritability test name.
    private async Task TestConditionalWritability0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;

        try
        {
            int expectedBytes = 100 * 1024 * 1024;
            int maxWriteChunkSize = 16 * 1024;
            // CLR: CountDownLatch(1) is a TaskCompletionSource: countDown() is TrySetResult(), which returns false
            // instead of failing once the Task is complete, as countDown() does nothing at a zero count
            // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetresult,
            // https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/concurrent/CountDownLatch.html),
            // and await() awaits its Task.
            TaskCompletionSource latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sb.ChildOption(ChannelOption.WRITE_BUFFER_WATER_MARK, new WriteBufferWaterMark(8 * 1024, 16 * 1024));
            // CLR: C# has no anonymous classes, so the source's anonymous ChannelInitializer bodies are
            // ActionChannelInitializer delegates (src/Netty.NET.Transport/Bootstrapping/Bootstrap.cs) and its anonymous
            // handlers are the nested classes below, whose constructor parameters carry the captured final locals.
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new WriterHandler(expectedBytes, maxWriteChunkSize));
            }));

            serverChannel = await sb.BindAsync();

            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new ReaderHandler(expectedBytes, latch));
            }));
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
            await latch.Task;
        }
        finally
        {
            if (serverChannel != null)
            {
                _ = serverChannel.CloseAsync();
            }

            if (clientChannel != null)
            {
                _ = clientChannel.CloseAsync();
            }
        }
    }

    private sealed class WriterHandler(int expectedBytes, int maxWriteChunkSize) : ChannelDuplexHandler
    {
        private int bytesWritten;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            WriteRemainingBytes(ctx);
        }

        public override void Flush(IChannelHandlerContext ctx)
        {
            if (ctx.Channel.IsWritable)
            {
                WriteRemainingBytes(ctx);
            }
            else
            {
                ctx.Flush();
            }
        }

        public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
        {
            if (ctx.Channel.IsWritable)
            {
                WriteRemainingBytes(ctx);
            }

            ctx.FireChannelWritabilityChanged();
        }

        private void WriteRemainingBytes(IChannelHandlerContext ctx)
        {
            while (ctx.Channel.IsWritable && bytesWritten < expectedBytes)
            {
                int chunkSize = Math.Min(expectedBytes - bytesWritten, maxWriteChunkSize);
                bytesWritten += chunkSize;
                _ = ctx.WriteAsync(ctx.Alloc().Buffer(chunkSize).WriteZero(chunkSize));
            }

            ctx.Flush();
        }
    }

    private sealed class ReaderHandler(int expectedBytes, TaskCompletionSource latch) : ChannelInboundHandlerAdapter
    {
        private int totalRead;

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.WriteAndFlushAsync(ctx.Alloc().Buffer(1).WriteByte(0));
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is ByteBuf)
            {
                totalRead += ((ByteBuf) msg).ReadableBytes;
                if (totalRead == expectedBytes)
                {
                    latch.TrySetResult();
                }
            }

            ReferenceCountUtil.Release(msg);
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
