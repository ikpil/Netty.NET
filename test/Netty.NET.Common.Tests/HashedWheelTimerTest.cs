/*
 * Copyright 2013 The Netty Project
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
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests;

public class HashedWheelTimerTest
{
    [Fact]
    public void testScheduleTimeoutShouldNotRunBeforeDelay()
    {
        ITimer timer = new HashedWheelTimer();
        CountdownEvent barrier = new CountdownEvent(1);
        ITimeout timeout = timer.newTimeout(TimerTask.Create(timeout =>
        {
            Assert.Fail("This should not have run");
            barrier.Signal();
        }), TimeSpan.FromSeconds(10));
        Assert.False(barrier.Wait(TimeSpan.FromSeconds(3)));
        Assert.False(timeout.isExpired(), "timer should not expire");
        timer.stop();
    }

    [Fact]
    public void testScheduleTimeoutShouldRunAfterDelay()
    {
        ITimer timer = new HashedWheelTimer();
        CountdownEvent barrier = new CountdownEvent(1);
        ITimeout timeout = timer.newTimeout(TimerTask.Create(timeout =>
        {
            barrier.Signal();
        }), TimeSpan.FromSeconds(2));
        Assert.True(barrier.Wait(TimeSpan.FromSeconds(3)));
        Assert.True(timeout.isExpired(), "timer should expire");
        timer.stop();
    }

    [Fact(Timeout = 3000)]
    public void testStopTimer()
    {
        CountdownEvent latch = new CountdownEvent(3);
        ITimer timerProcessed = new HashedWheelTimer();
        for (int i = 0; i < 3; i++)
        {
            timerProcessed.newTimeout(TimerTask.Create(timeout =>
            {
                latch.Signal();
            }), TimeSpan.FromMilliseconds(1));
        }

        latch.Wait();
        Assert.Equal(0, timerProcessed.stop().Count, "Number of unprocessed timeouts should be 0");

        ITimer timerUnprocessed = new HashedWheelTimer();
        for (int i = 0; i < 5; i++)
        {
            timerUnprocessed.newTimeout(TimerTask.Create(timeout =>
            {
            }), TimeSpan.FromSeconds(5));
        }

        Thread.Sleep(1000); // sleep for a second
        Assert.False(timerUnprocessed.stop().IsEmpty(), "Number of unprocessed timeouts should be greater than 0");
    }

    [Fact(Timeout = 3000)]
    public void testTimerShouldThrowExceptionAfterShutdownForNewTimeouts()
    {
        CountdownEvent latch = new CountdownEvent(3);
        ITimer timer = new HashedWheelTimer();
        for (int i = 0; i < 3; i++)
        {
            timer.newTimeout(TimerTask.Create(timeout =>
            {
                latch.Signal();
            }), TimeSpan.FromMilliseconds(1));
        }

        latch.Wait();
        timer.stop();

        try
        {
            timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromMilliseconds(1));
            Assert.Fail("Expected exception didn't occur.");
        }
        catch (InvalidOperationException ignored)
        {
            // expected
        }
    }

    [Fact(Timeout = 5000)]
    public void testTimerOverflowWheelLength()
    {
        HashedWheelTimer timer = new HashedWheelTimer(
            Executors.defaultThreadFactory(), TimeSpan.FromMilliseconds(100), 32);
        CountdownEvent latch = new CountdownEvent(3);

        timer.newTimeout(TimerTask.Create(timeout =>
        {
            timer.newTimeout(this, TimeSpan.FromMilliseconds(100));
            latch.Signal();
        }), TimeSpan.FromMilliseconds(100));

        latch.Wait();
        Assert.False(timer.stop().IsEmpty());
    }

    [Fact]
    public void testExecutionOnTime()
    {
        int tickDuration = 200;
        int timeout = 125;
        int maxTimeout = 2 * (tickDuration + timeout);
        HashedWheelTimer timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(tickDuration));
        IBlockingQueue<long> queue = new LinkedBlockingQueue<long>(100000);

        int scheduledTasks = 100000;
        for (int i = 0; i < scheduledTasks; i++)
        {
            long start = SystemTimer.nanoTime();
            timer.newTimeout(TimerTask.Create(timeout =>
            {
                queue.add(TimeUnit.NANOSECONDS.toMillis(SystemTimer.nanoTime() - start));
            }), TimeSpan.FromMilliseconds(timeout));
        }

        for (int i = 0; i < scheduledTasks; i++)
        {
            long delay = queue.take();
            Assert.True(delay >= timeout && delay < maxTimeout,
                "Timeout + " + scheduledTasks + " delay " + delay + " must be " + timeout + " < " + maxTimeout);
        }

        timer.stop();
    }

    [Fact]
    public void testExecutionOnTaskExecutor()
    {
        int timeout = 10;

        CountdownEvent latch = new CountdownEvent(1);
        CountdownEvent timeoutLatch = new CountdownEvent(1);
        IExecutor executor = new AnonymousExecutor(command =>
        {
            try
            {
                command.run();
            }
            finally
            {
                latch.Signal();
            }
        });
        HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(),
            TimeSpan.FromMilliseconds(100), 32, true, 2, executor);
        timer.newTimeout(TimerTask.Create(timeout =>
        {
            timeoutLatch.Signal();
        }), TimeSpan.FromMilliseconds(timeout));

        latch.Wait();
        timeoutLatch.Wait();
        timer.stop();
    }

    [Fact]
    public void testRejectedExecutionExceptionWhenTooManyTimeoutsAreAddedBackToBack()
    {
        HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(),
            TimeSpan.FromMilliseconds(100), 32, true, 2);
        timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromSeconds(5));
        timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromSeconds(5));
        try
        {
            timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromMilliseconds(1));
            Assert.Fail("Timer allowed adding 3 timeouts when maxPendingTimeouts was 2");
        }
        catch (RejectedExecutionException e)
        {
            // Expected
        }
        finally
        {
            timer.stop();
        }
    }

    [Fact]
    public void testNewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsCancelled()
    {
        int tickDurationMs = 100;
        HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(),
            TimeSpan.FromMilliseconds(tickDurationMs), 32, true, 2);
        timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromSeconds(5));
        ITimeout timeoutToCancel = timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromSeconds(5));
        Assert.True(timeoutToCancel.cancel());

        Thread.Sleep(tickDurationMs * 5);

        CountdownEvent secondLatch = new CountdownEvent(1);
        timer.newTimeout(createCountDownLatchTimerTask(secondLatch), TimeSpan.FromMilliseconds(90));

        secondLatch.Wait();
        timer.stop();
    }

    [Fact(Timeout = 3000)]
    public void testNewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsExecuted()
    {
        CountdownEvent latch = new CountdownEvent(1);
        HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(),
            TimeSpan.FromMilliseconds(25), 4, true, 2);
        timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromSeconds(5));
        timer.newTimeout(createCountDownLatchTimerTask(latch), TimeSpan.FromMilliseconds(90));

        latch.Wait();

        CountdownEvent secondLatch = new CountdownEvent(1);
        timer.newTimeout(createCountDownLatchTimerTask(secondLatch), TimeSpan.FromMilliseconds(90));

        secondLatch.Wait();
        timer.stop();
    }

    [Fact]
    public void reportPendingTimeouts()
    {
        CountdownEvent latch = new CountdownEvent(1);
        HashedWheelTimer timer = new HashedWheelTimer();
        ITimeout t1 = timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromMinutes(100));
        ITimeout t2 = timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromMinutes(100));
        timer.newTimeout(createCountDownLatchTimerTask(latch), TimeSpan.FromMilliseconds(90));

        Assert.Equal(3, timer.pendingTimeouts());
        t1.cancel();
        t2.cancel();
        latch.Wait();

        Assert.Equal(0, timer.pendingTimeouts());
        timer.stop();
    }

    [Fact]
    public void testOverflow()
    {
        HashedWheelTimer timer = new HashedWheelTimer();
        CountdownEvent latch = new CountdownEvent(1);
        ITimeout timeout = timer.newTimeout(TimerTask.Create(timeout =>
        {
            latch.Signal();
        }), TimeSpan.FromMilliseconds(long.MaxValue));
        Assert.False(latch.Wait(TimeSpan.FromSeconds(1)));
        timeout.cancel();
        timer.stop();
    }

    [Fact(Timeout = 3000)]
    public void testStopTimerCancelsPendingTasks()
    {
        ITimer timerUnprocessed = new HashedWheelTimer();
        for (int i = 0; i < 5; i++)
        {
            timerUnprocessed.newTimeout(TimerTask.Create(timeout =>
            {
            }), TimeSpan.FromSeconds(5));
        }

        Thread.Sleep(1000); // sleep for a second

        foreach (ITimeout timeout in timerUnprocessed.stop())
        {
            Assert.True(timeout.isCancelled(), "All unprocessed tasks should be canceled");
        }
    }

    [Fact(Timeout = 5000)]
    public void cancelWillCallCallback()
    {
        CountdownEvent latch = new CountdownEvent(1);
        HashedWheelTimer timer = new HashedWheelTimer();
        ITimeout t1 = timer.newTimeout(TimerTask.Create(timeout =>
        {
            Assert.Fail();
        }, timeout =>
        {
            latch.Signal();
        }), TimeSpan.FromMilliseconds(90));

        Assert.Equal(1, timer.pendingTimeouts());
        t1.cancel();
        latch.Wait();
    }

    [Fact]
    public void testPendingTimeoutsShouldBeCountedCorrectlyWhenTimeoutCancelledWithinGoalTick()
    {
        HashedWheelTimer timer = new HashedWheelTimer();
        CountdownEvent barrier = new CountdownEvent(1);
        // A total of 11 timeouts with the same delay are submitted, and they will be processed in the same tick.
        timer.newTimeout(TimerTask.Create(timeout =>
        {
            barrier.Signal();
            Thread.Sleep(1000);
        }), TimeSpan.FromMilliseconds(200));
        List<ITimeout> timeouts = new List<ITimeout>();
        for (int i = 0; i < 10; i++)
        {
            timeouts.Add(timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromMilliseconds(200)));
        }

        barrier.Wait();
        // The simulation here is that the timeout has been transferred to a bucket and is canceled before it is
        // actually expired in the goal tick.
        foreach (ITimeout timeout in timeouts)
        {
            timeout.cancel();
        }

        Thread.Sleep(2000);
        Assert.Equal(0, timer.pendingTimeouts());
        timer.stop();
    }

    private static ITimerTask createNoOpTimerTask()
    {
        return TimerTask.Create(timeout => { });
    }

    private static ITimerTask createCountDownLatchTimerTask(CountdownEvent latch)
    {
        return TimerTask.Create(timeout => latch.Signal());
    }
}