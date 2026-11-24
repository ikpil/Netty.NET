/*
 * Copyright 2026 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in
 * compliance with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
 * implied. See the License for the specific language governing
 * permissions and limitations under the License.
 */

using System;
using System.Collections.Generic;
using Netty.NET.Handler.Timeout;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Tests.Timeout;

/**
 * Tests that {@link IdleStateHandler#resetWriteTimeout()} and
 * {@link IdleStateHandler#resetReadTimeout()} correctly restore the
 * "first idle" semantics after a non-first event has already fired.
 *
 * The reset methods update the timestamp but don't touch the firstWriter/ReaderIdleEvent
 * flags, so the handler keeps reporting non-first events even after a reset.
 * The writeListener path doesn't have this problem because it resets both.
 */
// CLR: the anonymous ChannelInboundHandlerAdapter is the named nested class IdleEventCollector;
// advanceTimeBy(1100L, MILLISECONDS) is AdvanceTimeBy(TimeSpan.FromMilliseconds(1100)) (the EmbeddedChannel frozen
// clock, as in Java) and new IdleStateHandler(false, 0L, 1L, 0L, SECONDS) takes TimeSpan arguments.
public class IdleStateHandlerResetFlagTest
{

    /**
     * If a WRITER_IDLE event has already fired as non-first and then
     * resetWriteTimeout() is called, the next event should be first again —
     * same as if an actual write had happened.
     */
    [Fact]
    public void TestResetWriteTimeoutResetsFirstEventFlag()
    {
        IdleStateHandler idleStateHandler = new IdleStateHandler(
                false, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.Zero);

        List<IdleStateEvent> events = new List<IdleStateEvent>();
        EmbeddedChannel channel = new EmbeddedChannel(idleStateHandler,
                new IdleEventCollector(events));
        channel.FreezeTime();

        try
        {
            // first idle — expected
            channel.AdvanceTimeBy(TimeSpan.FromMilliseconds(1100L));
            channel.RunPendingTasks();
            Assert.Equal(1, events.Count);
            Assert.Same(IdleStateEvent.FIRST_WRITER_IDLE_STATE_EVENT, events[0],
                    "First idle after connect should be FIRST_WRITER_IDLE_STATE_EVENT");

            // second idle, no activity in between — non-first
            channel.AdvanceTimeBy(TimeSpan.FromMilliseconds(1100L));
            channel.RunPendingTasks();
            Assert.Equal(2, events.Count);
            Assert.Same(IdleStateEvent.WRITER_IDLE_STATE_EVENT, events[1],
                    "Second idle without reset should be WRITER_IDLE_STATE_EVENT (first=false)");

            // reset: tells the handler to treat this moment as a fresh start
            idleStateHandler.ResetWriteTimeout();

            // should fire as first again, but currently doesn't because
            // resetWriteTimeout() only updates lastWriteTime, not firstWriterIdleEvent
            channel.AdvanceTimeBy(TimeSpan.FromMilliseconds(1100L));
            channel.RunPendingTasks();
            Assert.Equal(3, events.Count);
            Assert.Same(IdleStateEvent.FIRST_WRITER_IDLE_STATE_EVENT, events[2],
                    "After resetWriteTimeout(), next idle MUST be FIRST_WRITER_IDLE_STATE_EVENT. " +
                    "Bug: firstWriterIdleEvent is not reset by resetWriteTimeout().");
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    /**
     * Same issue on the read side: resetReadTimeout() resets the clock but
     * leaves firstReaderIdleEvent as-is, so the next event stays non-first.
     */
    [Fact]
    public void TestResetReadTimeoutResetsFirstEventFlag()
    {
        IdleStateHandler idleStateHandler = new IdleStateHandler(
                false, TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero);

        List<IdleStateEvent> events = new List<IdleStateEvent>();
        EmbeddedChannel channel = new EmbeddedChannel(idleStateHandler,
                new IdleEventCollector(events));
        channel.FreezeTime();

        try
        {
            // first idle
            channel.AdvanceTimeBy(TimeSpan.FromMilliseconds(1100L));
            channel.RunPendingTasks();
            Assert.Equal(1, events.Count);
            Assert.Same(IdleStateEvent.FIRST_READER_IDLE_STATE_EVENT, events[0],
                    "First idle after connect should be FIRST_READER_IDLE_STATE_EVENT");

            // non-first idle
            channel.AdvanceTimeBy(TimeSpan.FromMilliseconds(1100L));
            channel.RunPendingTasks();
            Assert.Equal(2, events.Count);
            Assert.Same(IdleStateEvent.READER_IDLE_STATE_EVENT, events[1],
                    "Second idle without reset should be READER_IDLE_STATE_EVENT (first=false)");

            idleStateHandler.ResetReadTimeout();

            // should be first again after the reset
            channel.AdvanceTimeBy(TimeSpan.FromMilliseconds(1100L));
            channel.RunPendingTasks();
            Assert.Equal(3, events.Count);
            Assert.Same(IdleStateEvent.FIRST_READER_IDLE_STATE_EVENT, events[2],
                    "After resetReadTimeout(), next idle MUST be FIRST_READER_IDLE_STATE_EVENT. " +
                    "Bug: firstReaderIdleEvent is not reset by resetReadTimeout().");
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    private sealed class IdleEventCollector : ChannelInboundHandlerAdapter
    {
        private readonly List<IdleStateEvent> events;

        internal IdleEventCollector(List<IdleStateEvent> events)
        {
            this.events = events;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is IdleStateEvent)
            {
                events.Add((IdleStateEvent)evt);
            }
        }
    }
}
