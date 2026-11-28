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
using System.Collections.Generic;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Timeout;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Tests.Timeout;

// CLR: the tests drive the EmbeddedChannel frozen clock as Java does (FreezeTime/AdvanceTimeBy/RunPendingTasks);
// advanceTimeBy(n, unit) takes a TimeSpan and new IdleStateHandler(observeOutput, r, w, a, SECONDS) takes TimeSpan
// arguments. TimeSpan counts 100 ns ticks ("The smallest unit of time is the tick, which is equal to 100
// nanoseconds", https://learn.microsoft.com/dotnet/api/system.timespan.ticks), so anyNotIdle's delayInNanos / 2L +
// 1L nanoseconds is rounded up to the next tick (500,000,100 ns), keeping the action strictly after half the idle
// time. The functional interface Action is a delegate with the same name; AssertJ
// assertThat(expected.length).isGreaterThanOrEqualTo(1) is Assert.True. ObservableChannel keeps the written
// messages in its ChannelOutboundBuffer as the empty Java doWrite(ChannelOutboundBuffer) does (see the class).
public class IdleStateHandlerTest
{

    [Fact]
    public void TestReaderIdle()
    {
        IdleStateHandler idleStateHandler = new IdleStateHandler(
                false, TimeSpan.FromSeconds(1L), TimeSpan.Zero, TimeSpan.Zero);

        // We start with one FIRST_READER_IDLE_STATE_EVENT, followed by an infinite number of READER_IDLE_STATE_EVENTs
        AnyIdle(idleStateHandler, IdleStateEvent.FIRST_READER_IDLE_STATE_EVENT,
                IdleStateEvent.READER_IDLE_STATE_EVENT, IdleStateEvent.READER_IDLE_STATE_EVENT);
    }

    [Fact]
    public void TestWriterIdle()
    {
        IdleStateHandler idleStateHandler = new IdleStateHandler(
                false, TimeSpan.Zero, TimeSpan.FromSeconds(1L), TimeSpan.Zero);

        AnyIdle(idleStateHandler, IdleStateEvent.FIRST_WRITER_IDLE_STATE_EVENT,
                IdleStateEvent.WRITER_IDLE_STATE_EVENT, IdleStateEvent.WRITER_IDLE_STATE_EVENT);
    }

    [Fact]
    public void TestAllIdle()
    {
        IdleStateHandler idleStateHandler = new IdleStateHandler(
                false, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1L));

        AnyIdle(idleStateHandler, IdleStateEvent.FIRST_ALL_IDLE_STATE_EVENT,
                IdleStateEvent.ALL_IDLE_STATE_EVENT, IdleStateEvent.ALL_IDLE_STATE_EVENT);
    }

    private static void AnyIdle(IdleStateHandler idleStateHandler, params object[] expected)
    {
        Assert.True(expected.Length >= 1);

        List<object> events = new List<object>();
        ChannelInboundHandlerAdapter handler = new EventCollector(events);

        EmbeddedChannel channel = new EmbeddedChannel(idleStateHandler, handler);
        channel.FreezeTime();

        try
        {
            // For each expected event advance the ticker and run() the task. Each
            // step should yield in an IdleStateEvent because we haven't written
            // or read anything from the channel.
            for (int i = 0; i < expected.Length; i++)
            {
                channel.AdvanceTimeBy(TimeSpan.FromSeconds(1));
                channel.RunPendingTasks();
            }

            Assert.Equal(expected.Length, events.Count);

            // Compare the expected with the actual IdleStateEvents
            for (int i = 0; i < expected.Length; i++)
            {
                object evt = events[i];
                Assert.Same(expected[i], evt, "Element " + i + " is not matching");
            }
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestResetReader()
    {
        IdleStateHandler idleStateHandler = new IdleStateHandler(
                false, TimeSpan.FromSeconds(1L), TimeSpan.Zero, TimeSpan.Zero);

        Action action = channel => idleStateHandler.ResetReadTimeout();

        AnyNotIdle(idleStateHandler, action, IdleStateEvent.FIRST_READER_IDLE_STATE_EVENT);
    }

    [Fact]
    public void TestResetWriter()
    {
        IdleStateHandler idleStateHandler = new IdleStateHandler(
                false, TimeSpan.Zero, TimeSpan.FromSeconds(1L), TimeSpan.Zero);

        Action action = channel => idleStateHandler.ResetWriteTimeout();

        AnyNotIdle(idleStateHandler, action, IdleStateEvent.FIRST_WRITER_IDLE_STATE_EVENT);
    }

    [Fact]
    public void TestReaderNotIdle()
    {
        IdleStateHandler idleStateHandler = new IdleStateHandler(
                false, TimeSpan.FromSeconds(1L), TimeSpan.Zero, TimeSpan.Zero);

        Action action = channel => channel.WriteInbound("Hello, World!");

        AnyNotIdle(idleStateHandler, action, IdleStateEvent.FIRST_READER_IDLE_STATE_EVENT);
    }

    [Fact]
    public void TestWriterNotIdle()
    {
        IdleStateHandler idleStateHandler = new IdleStateHandler(
                false, TimeSpan.Zero, TimeSpan.FromSeconds(1L), TimeSpan.Zero);

        Action action = channel => _ = channel.WriteAndFlushAsync("Hello, World!");

        AnyNotIdle(idleStateHandler, action, IdleStateEvent.FIRST_WRITER_IDLE_STATE_EVENT);
    }

    [Fact]
    public void TestAllNotIdle()
    {
        // Reader...
        IdleStateHandler idleStateHandler = new IdleStateHandler(
                false, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1L));

        Action reader = channel => channel.WriteInbound("Hello, World!");

        AnyNotIdle(idleStateHandler, reader, IdleStateEvent.FIRST_ALL_IDLE_STATE_EVENT);

        // Writer...
        idleStateHandler = new IdleStateHandler(
                false, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1L));

        Action writer = channel => _ = channel.WriteAndFlushAsync("Hello, World!");

        AnyNotIdle(idleStateHandler, writer, IdleStateEvent.FIRST_ALL_IDLE_STATE_EVENT);
    }

    private static void AnyNotIdle(IdleStateHandler idleStateHandler,
                                   Action action, object expected)
    {

        List<object> events = new List<object>();
        ChannelInboundHandlerAdapter handler = new EventCollector(events);

        EmbeddedChannel channel = new EmbeddedChannel(idleStateHandler, handler);
        channel.FreezeTime();

        try
        {
            long delayInNanos = TimeSpan.FromSeconds(1).Ticks * 100;

            channel.AdvanceTimeBy(NanosToTimeSpan(delayInNanos / 2L + 1L));
            action(channel);

            // Advance the ticker by some fraction.
            // There shouldn't be an IdleStateEvent getting fired because
            // we've just performed an action on the channel that is meant
            // to reset the idle task.
            channel.AdvanceTimeBy(NanosToTimeSpan(delayInNanos / 2L));
            channel.RunPendingTasks();
            Assert.Equal(0, events.Count);

            // Advance the ticker by the full amount and it should yield
            // in an IdleStateEvent.
            channel.AdvanceTimeBy(NanosToTimeSpan(delayInNanos));
            channel.RunPendingTasks();
            Assert.Equal(1, events.Count);
            Assert.Same(expected, events[0]);
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    // CLR: advanceTimeBy(n, NANOSECONDS) as a TimeSpan, rounded up to the next 100 ns tick (see the class comment).
    private static TimeSpan NanosToTimeSpan(long nanos)
    {
        return TimeSpan.FromTicks(nanos / 100 + (nanos % 100 == 0 ? 0 : 1));
    }

    [Fact]
    public void TestObserveWriterIdle()
    {
        ObserveOutputIdle(true);
    }

    [Fact]
    public void TestObserveAllIdle()
    {
        ObserveOutputIdle(false);
    }

    private static void ObserveOutputIdle(bool writer)
    {

        long writerIdleTime = 0L;
        long allIdleTime = 0L;
        IdleStateEvent expected;

        if (writer)
        {
            writerIdleTime = 5L;
            expected = IdleStateEvent.FIRST_WRITER_IDLE_STATE_EVENT;
        }
        else
        {
            allIdleTime = 5L;
            expected = IdleStateEvent.FIRST_ALL_IDLE_STATE_EVENT;
        }

        IdleStateHandler idleStateHandler = new IdleStateHandler(
                true, TimeSpan.Zero, TimeSpan.FromSeconds(writerIdleTime), TimeSpan.FromSeconds(allIdleTime));

        List<object> events = new List<object>();
        ChannelInboundHandlerAdapter handler = new EventCollector(events);

        ObservableChannel channel = new ObservableChannel(idleStateHandler, handler);
        channel.FreezeTime();

        try
        {
            // We're writing 3 messages that will be consumed at different rates!
            _ = channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 1 }));
            _ = channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 2 }));
            _ = channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 3 }));
            _ = channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[5 * 1024]));

            // Establish a baseline. We're not consuming anything and let it idle once.
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(5));
            channel.RunPendingTasks();
            Assert.Equal(1, events.Count);
            Assert.Same(expected, events[0]);
            events.Clear();

            // Consume one message in 4 seconds, then be idle for 2 seconds,
            // then run the task and we shouldn't get an IdleStateEvent because
            // we haven't been idle for long enough!
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(4));
            channel.RunPendingTasks();
            AssertNotNullAndRelease(channel.Consume());

            channel.AdvanceTimeBy(TimeSpan.FromSeconds(2));
            channel.RunPendingTasks();
            Assert.Equal(0, events.Count);

            // Consume one message in 3 seconds, then be idle for 4 seconds,
            // then run the task and we shouldn't get an IdleStateEvent because
            // we haven't been idle for long enough!
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(2));
            channel.RunPendingTasks();
            AssertNotNullAndRelease(channel.Consume());

            channel.AdvanceTimeBy(TimeSpan.FromSeconds(4));
            channel.RunPendingTasks();
            Assert.Equal(0, events.Count);

            // Don't consume a message and be idle for 5 seconds.
            // We should get an IdleStateEvent!
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(5));
            channel.RunPendingTasks();
            Assert.Equal(1, events.Count);
            events.Clear();

            // Consume one message in 2 seconds, then be idle for 1 seconds,
            // then run the task and we shouldn't get an IdleStateEvent because
            // we haven't been idle for long enough!
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(2));
            channel.RunPendingTasks();
            AssertNotNullAndRelease(channel.Consume());

            channel.AdvanceTimeBy(TimeSpan.FromSeconds(1));
            channel.RunPendingTasks();
            Assert.Equal(0, events.Count);

            // Consume part of the message every 2 seconds, then be idle for 1 seconds,
            // then run the task and we should get an IdleStateEvent because the first trigger
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(2));
            channel.RunPendingTasks();
            AssertNotNullAndRelease(channel.ConsumePart(1024));
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(2));
            channel.RunPendingTasks();
            AssertNotNullAndRelease(channel.ConsumePart(1024));
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(1));
            channel.RunPendingTasks();
            Assert.Equal(1, events.Count);
            events.Clear();

            // Consume part of the message every 2 seconds, then be idle for 1 seconds,
            // then consume all the rest of the message, then run the task and we shouldn't
            // get an IdleStateEvent because the data is flowing and we haven't been idle for long enough!
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(2));
            channel.RunPendingTasks();
            AssertNotNullAndRelease(channel.ConsumePart(1024));
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(2));
            channel.RunPendingTasks();
            AssertNotNullAndRelease(channel.ConsumePart(1024));
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(1));
            channel.RunPendingTasks();
            Assert.Equal(0, events.Count);
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(2));
            channel.RunPendingTasks();
            AssertNotNullAndRelease(channel.ConsumePart(1024));

            // There are no messages left! Advance the ticker by 3 seconds,
            // attempt a consume() but it will be null, then advance the
            // ticker by an another 2 seconds and we should get an IdleStateEvent
            // because we've been idle for 5 seconds.
            channel.AdvanceTimeBy(TimeSpan.FromSeconds(3));
            channel.RunPendingTasks();
            Assert.Null(channel.Consume());

            channel.AdvanceTimeBy(TimeSpan.FromSeconds(2));
            channel.RunPendingTasks();
            Assert.Equal(1, events.Count);

            // q.e.d.
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    private static void AssertNotNullAndRelease(object msg)
    {
        Assert.NotNull(msg);
        ReferenceCountUtil.Release(msg);
    }

    private delegate void Action(EmbeddedChannel channel);

    private sealed class EventCollector : ChannelInboundHandlerAdapter
    {
        private readonly List<object> events;

        internal EventCollector(List<object> events)
        {
            this.events = events;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            events.Add(evt);
        }
    }

    // CLR: EmbeddedChannel writes through AbstractChannel's per-message drain, which removes a message from the
    // ChannelOutboundBuffer once doWrite(msg)'s Task completes (src/Netty.NET.Transport/Channels/AbstractChannel.cs,
    // DrainWritesAsync). A Task that stays pending keeps the drain at the first flushed message, so every flushed message
    // stays in the buffer, as with the empty Java doWrite(ChannelOutboundBuffer). The drain retains the message while it
    // waits; doClose() completes the Task so that the drain releases it once the channel closes. unsafe().outboundBuffer()
    // is the OutboundBuffer property (Channel.Unsafe is folded into AbstractChannel).
    private sealed class ObservableChannel : EmbeddedChannel
    {
        private readonly TaskCompletionSource pendingWrite = new TaskCompletionSource();

        internal ObservableChannel(params IChannelHandler[] handlers) : base(handlers)
        {
        }

        protected override Task DoWrite(object msg)
        {
            // Overridden to change EmbeddedChannel's default behavior. We went to keep
            // the messages in the ChannelOutboundBuffer.
            return pendingWrite.Task;
        }

        protected override void DoClose()
        {
            base.DoClose();
            pendingWrite.TrySetResult();
        }

        internal object Consume()
        {
            ChannelOutboundBuffer buf = OutboundBuffer;
            if (buf != null)
            {
                object msg = buf.Current;
                if (msg != null)
                {
                    ReferenceCountUtil.Retain(msg);
                    buf.Remove();
                    return msg;
                }
            }

            return null;
        }

        /**
         * Consume the part of a message.
         *
         * @param byteCount count of byte to be consumed
         * @return the message currently being consumed
         */
        internal object ConsumePart(int byteCount)
        {
            ChannelOutboundBuffer buf = OutboundBuffer;
            if (buf != null)
            {
                object msg = buf.Current;
                if (msg != null)
                {
                    ReferenceCountUtil.Retain(msg);
                    buf.RemoveBytes(byteCount);
                    return msg;
                }
            }

            return null;
        }
    }
}
