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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Preserve source assertion windows and independent cleanup.
public class SocketExceptionHandlingSourceTest
{
    [Theory(Timeout = 15000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestReadPendingIsResetAfterEachRead(bool adaptive)
    {
        await using var fixture = new ShutdownFixture(adaptive);
        var exceptions = new ExceptionHandler();
        IChannel server = await fixture.Server.Option(ChannelOption.SO_BACKLOG, 1024)
            .ChildHandler(new ActionChannelInitializer<IChannel>(channel => channel.Pipeline.AddLast(new BuggyChannelHandler(), exceptions)))
            .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
        IChannel client = null;
        try
        {
            client = await fixture.Client.Handler(new ActionChannelInitializer<IChannel>(channel => channel.Pipeline.AddLast(new BuggyChannelHandler(), new ExceptionHandler())))
                .ConnectAsync(server.LocalAddress, cancellationToken: TestContext.Current.CancellationToken);
            var data = new byte[1024];
            ByteBuf message = Random.Shared.Next(2) == 0 ? client.Allocator.DirectBuffer().WriteBytes(data) : Unpooled.WrappedBuffer(data);
            await client.WriteAndFlushAsync(message);
            // We expect to get 2 exceptions (1 from BuggyChannelHandler and 1 from ExceptionHandler).
            await exceptions.First.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            // After we get the first exception, we should get no more, this is expected to timeout.
            Assert.NotSame(exceptions.Excess.Task, await Task.WhenAny(exceptions.Excess.Task, Task.Delay(1000, TestContext.Current.CancellationToken)));
        }
        finally { if (client != null) await client.CloseAsync(); await server.CloseAsync(); }
    }
    private sealed class BuggyChannelHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { ReferenceCountUtil.Release(message); throw new NullReferenceException("I am a bug!"); }
    }
    private sealed class ExceptionHandler : ChannelInboundHandlerAdapter
    {
        private long _count;
        /**
         * We expect to get 1 call to {@link #exceptionCaught(ChannelHandlerContext, Throwable)}.
         */
        internal readonly TaskCompletionSource First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Excess = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        {
            if (Interlocked.Increment(ref _count) <= 2) First.TrySetResult(); else Excess.TrySetResult();
            // This should not throw any exception.
            _ = context.CloseAsync();
        }
    }
}
#pragma warning restore xUnit1051
