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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Flush;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Tests.Flush;

// CLR: the anonymous handlers are named nested classes, AtomicInteger is the Common AtomicInteger, readOutbound() with
// the (Long) cast is ReadOutbound<long>() (the boxed long the inbound handler wrote back) and readOutbound() alone is
// ReadOutbound(). IllegalStateException is InvalidOperationException; the anonymous Executable is a lambda.
// pipeline().remove(Class) is Pipeline.Remove<T>(). The write future's addListener(...) is a synchronous continuation,
// run when the write completes during flushOutbound(), as the listener of the source
// (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcontinuationoptions?view=net-10.0).
public class FlushConsolidationHandlerTest
{

    private const int EXPLICIT_FLUSH_AFTER_FLUSHES = 3;

    [Fact]
    public void TestFlushViaScheduledTask()
    {
        AtomicInteger flushCount = new AtomicInteger();
        EmbeddedChannel channel = NewChannel(flushCount,  true);
        // Flushes should not go through immediately, as they're scheduled as an async task
        // To ensure we not run the async task directly we will call trigger the flush() via the pipeline.
        channel.Pipeline.Flush();
        Assert.Equal(0, flushCount.Get());
        channel.Pipeline.Flush();
        Assert.Equal(0, flushCount.Get());
        // Trigger the execution of the async task
        channel.RunPendingTasks();
        Assert.Equal(1, flushCount.Get());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestFlushViaThresholdOutsideOfReadLoop()
    {
        AtomicInteger flushCount = new AtomicInteger();
        EmbeddedChannel channel = NewChannel(flushCount, true);

        // After a given threshold, the async task should be bypassed and a flush should be triggered immediately
        for (int i = 0; i < EXPLICIT_FLUSH_AFTER_FLUSHES; i++)
        {
            // To ensure we not run the async task directly we will call trigger the flush() via the pipeline.
            channel.Pipeline.Flush();
        }

        Assert.Equal(1, flushCount.Get());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestImmediateFlushOutsideOfReadLoop()
    {
        AtomicInteger flushCount = new AtomicInteger();
        EmbeddedChannel channel = NewChannel(flushCount, false);
        channel.Flush();
        Assert.Equal(1, flushCount.Get());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestFlushViaReadComplete()
    {
        AtomicInteger flushCount = new AtomicInteger();
        EmbeddedChannel channel = NewChannel(flushCount, false);
        // Flush should go through as there is no read loop in progress.
        channel.Flush();
        channel.RunPendingTasks();
        Assert.Equal(1, flushCount.Get());

        // Simulate read loop;
        channel.Pipeline.FireChannelRead(1L);
        Assert.Equal(1, flushCount.Get());
        channel.Pipeline.FireChannelRead(2L);
        Assert.Equal(1, flushCount.Get());
        Assert.Null(channel.ReadOutbound());
        channel.Pipeline.FireChannelReadComplete();
        Assert.Equal(2, flushCount.Get());
        // Now flush again as the read loop is complete.
        channel.Flush();
        channel.RunPendingTasks();
        Assert.Equal(3, flushCount.Get());
        Assert.Equal(1L, channel.ReadOutbound<long>());
        Assert.Equal(2L, channel.ReadOutbound<long>());
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestFlushViaClose()
    {
        AtomicInteger flushCount = new AtomicInteger();
        EmbeddedChannel channel = NewChannel(flushCount, false);
        // Simulate read loop;
        channel.Pipeline.FireChannelRead(1L);
        Assert.Equal(0, flushCount.Get());
        Assert.Null(channel.ReadOutbound());
        _ = channel.CloseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, flushCount.Get());
        Assert.Equal(1L, channel.ReadOutbound<long>());
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestFlushViaDisconnect()
    {
        AtomicInteger flushCount = new AtomicInteger();
        EmbeddedChannel channel = NewChannel(flushCount, false);
        // Simulate read loop;
        channel.Pipeline.FireChannelRead(1L);
        Assert.Equal(0, flushCount.Get());
        Assert.Null(channel.ReadOutbound());
        _ = channel.DisconnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, flushCount.Get());
        Assert.Equal(1L, channel.ReadOutbound<long>());
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestFlushViaException()
    {
        AtomicInteger flushCount = new AtomicInteger();
        EmbeddedChannel channel = NewChannel(flushCount, false);
        // Simulate read loop;
        channel.Pipeline.FireChannelRead(1L);
        Assert.Equal(0, flushCount.Get());
        Assert.Null(channel.ReadOutbound());
        channel.Pipeline.FireExceptionCaught(new InvalidOperationException());
        Assert.Equal(1, flushCount.Get());
        Assert.Equal(1L, channel.ReadOutbound<long>());
        Assert.Null(channel.ReadOutbound());
        Assert.Throws<InvalidOperationException>(() =>
        {
            channel.Finish();
        });
    }

    [Fact]
    public void TestFlushViaRemoval()
    {
        AtomicInteger flushCount = new AtomicInteger();
        EmbeddedChannel channel = NewChannel(flushCount, false);
        // Simulate read loop;
        channel.Pipeline.FireChannelRead(1L);
        Assert.Equal(0, flushCount.Get());
        Assert.Null(channel.ReadOutbound());
        channel.Pipeline.Remove<FlushConsolidationHandler>();
        Assert.Equal(1, flushCount.Get());
        Assert.Equal(1L, channel.ReadOutbound<long>());
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
    }

    /**
     * See https://github.com/netty/netty/issues/9923
     */
    [Fact]
    public void TestResend()
    {
        AtomicInteger flushCount = new AtomicInteger();
        EmbeddedChannel channel = NewChannel(flushCount, true);
        _ = channel.WriteAndFlushAsync(1L, TestContext.Current.CancellationToken).ContinueWith(
                future => channel.WriteAndFlushAsync(1L, TestContext.Current.CancellationToken),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        channel.FlushOutbound();
        Assert.Equal(1L, channel.ReadOutbound<long>());
        Assert.Equal(1L, channel.ReadOutbound<long>());
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
    }

    private static EmbeddedChannel NewChannel(AtomicInteger flushCount, bool consolidateWhenNoReadInProgress)
    {
        return new EmbeddedChannel(
                new FlushCountingHandler(flushCount),
                new FlushConsolidationHandler(EXPLICIT_FLUSH_AFTER_FLUSHES, consolidateWhenNoReadInProgress),
                new WriteBackHandler());
    }

    private sealed class FlushCountingHandler : ChannelOutboundHandlerAdapter
    {
        private readonly AtomicInteger flushCount;

        internal FlushCountingHandler(AtomicInteger flushCount)
        {
            this.flushCount = flushCount;
        }

        public override void Flush(IChannelHandlerContext ctx)
        {
            flushCount.IncrementAndGet();
            ctx.Flush();
        }
    }

    private sealed class WriteBackHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            _ = ctx.WriteAndFlushAsync(msg);
        }
    }
}
