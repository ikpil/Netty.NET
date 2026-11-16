/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The Runner body observes TestContext.Current.CancellationToken.
#pragma warning disable xUnit1051 // Always close independently of the test cancellation.
public class SocketCancelWriteSourceTest : AbstractSocketTest
{
    [Fact(Timeout = 30000)]
    public async Task TestCancelWrite()
    {
        await Run(TestContext.Current, TestCancelWrite0);
    }

    // CLR: xUnit1024 forbids overloading the TestCancelWrite test name.
    private async Task TestCancelWrite0(ServerBootstrap sb, Bootstrap cb)
    {
        var cancellation = TestContext.Current.CancellationToken;
        TestHandler sh = new TestHandler();
        TestHandler ch = new TestHandler();
        ByteBuf a = Unpooled.Buffer().WriteByte('a');
        ByteBuf b = Unpooled.Buffer().WriteByte('b');
        ByteBuf c = Unpooled.Buffer().WriteByte('c');
        ByteBuf d = Unpooled.Buffer().WriteByte('d');
        ByteBuf e = Unpooled.Buffer().WriteByte('e');

        cb.Handler(ch);
        sb.ChildHandler(sh);

        IChannel sc = await sb.BindAsync(cancellation);
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress, cancellation);

        // CLR: ChannelFuture.cancel(false) becomes cancelling the token of a still-queued write Task; the event-loop
        // barrier makes sure the write is queued, and an OperationCanceledException is its cancelled outcome.
        await CancelQueuedWrite(cc, a, cancellation);
        Task fb = cc.WriteAndFlushAsync(b, cancellation);
        Task fc = cc.WriteAsync(c, cancellation);
        await CancelQueuedWrite(cc, d, cancellation);
        Task fe = cc.WriteAndFlushAsync(e, cancellation);
        await Task.WhenAll(fb, fc, fe).WaitAsync(cancellation);

        // CLR: the source polls counter/exception every 50 ms; the handler completes this Task at three bytes or on
        // the first exception instead.
        await Task.WhenAny(sh.Done.Task, ch.Done.Task).WaitAsync(cancellation);
        await sh.channel.CloseAsync();
        await ch.channel.CloseAsync();
        await sc.CloseAsync();

        if (sh.exception != null && !(sh.exception is System.IO.IOException))
        {
            throw sh.exception;
        }
        if (sh.exception != null)
        {
            throw sh.exception;
        }
        if (ch.exception != null && !(ch.exception is System.IO.IOException))
        {
            throw ch.exception;
        }
        if (ch.exception != null)
        {
            throw ch.exception;
        }
        Assert.Equal(0, ch.counter);
        Assert.Equal(Unpooled.WrappedBuffer(new byte[] { (byte)'b', (byte)'c', (byte)'e' }), sh.received);
        // CLR: supplemental check that both cancelled writes released their buffers.
        Assert.Equal(0, a.RefCnt);
        Assert.Equal(0, d.RefCnt);
    }

    private static async Task CancelQueuedWrite(IChannel channel, ByteBuf message, CancellationToken cancellation)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Task write = channel.WriteAsync(message, cancel.Token);
        await channel.EventLoop.SubmitAsync(() => { }, cancellation);
        Assert.False(write.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
    }

    private sealed class TestHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        internal volatile IChannel channel;
        // CLR: AtomicReference<Throwable>.compareAndSet(null, cause) is Interlocked.CompareExchange on this field.
        internal Exception exception;
        internal volatile int counter;
        internal readonly ByteBuf received = Unpooled.Buffer();
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
            counter += @in.ReadableBytes;
            received.WriteBytes(@in);
            if (counter >= 3)
            {
                Done.TrySetResult();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx,
                Exception cause)
        {
            if (Interlocked.CompareExchange(ref exception, cause, null) == null)
            {
                Done.TrySetResult();
                _ = ctx.CloseAsync();
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
