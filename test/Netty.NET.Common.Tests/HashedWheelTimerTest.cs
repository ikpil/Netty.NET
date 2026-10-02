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
using System.Collections.Concurrent;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests;

[CollectionDefinition("Timer globals", DisableParallelization = true)]
public class TimerGlobalsCollection;

[Collection("Timer globals")]
public class HashedWheelTimerTest
{
    [Fact]
    public void testScheduleTimeoutShouldNotRunBeforeDelay()
    {
        using HashedWheelTimer timer = new HashedWheelTimer();
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
        using HashedWheelTimer timer = new HashedWheelTimer();
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
        using HashedWheelTimer timerProcessed = new HashedWheelTimer();
        for (int i = 0; i < 3; i++)
        {
            timerProcessed.newTimeout(TimerTask.Create(timeout =>
            {
                latch.Signal();
            }), TimeSpan.FromMilliseconds(1));
        }

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, timerProcessed.stop().Count, "Number of unprocessed timeouts should be 0");

        using HashedWheelTimer timerUnprocessed = new HashedWheelTimer();
        for (int i = 0; i < 5; i++)
        {
            timerUnprocessed.newTimeout(TimerTask.Create(timeout =>
            {
            }), TimeSpan.FromSeconds(5));
        }

        Thread.Sleep(1000); // sleep for a second
        Assert.NotEmpty(timerUnprocessed.stop());
    }

    [Fact(Timeout = 3000)]
    public void testTimerShouldThrowExceptionAfterShutdownForNewTimeouts()
    {
        CountdownEvent latch = new CountdownEvent(3);
        using HashedWheelTimer timer = new HashedWheelTimer();
        for (int i = 0; i < 3; i++)
        {
            timer.newTimeout(TimerTask.Create(timeout =>
            {
                latch.Signal();
            }), TimeSpan.FromMilliseconds(1));
        }

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
        timer.stop();

        Assert.Throws<InvalidOperationException>(() => timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromMilliseconds(1)));
        // All three timeouts expired and the rejected one must not be counted either.
        Assert.Equal(0, timer.pendingTimeouts());
    }

    private sealed class StoppingOnStartTimer : HashedWheelTimer
    {
        internal ISet<ITimeout> Unprocessed;
        public override void start()
        {
            base.start();
            // Stop the timer after newTimeout() started it but before it added the timeout to the queue. This
            // lets the worker terminate before the timeout is added.
            if (Unprocessed == null) Unprocessed = stop();
        }
    }

    [Fact(Timeout = 5000)]
    public void testNewTimeoutRacingWithStop()
    {
        using var timer = new StoppingOnStartTimer();
        Assert.Throws<InvalidOperationException>(() => timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromMilliseconds(1)));
        Assert.Empty(timer.Unprocessed);
        Assert.Equal(0, timer.pendingTimeouts());
    }

    [Fact(Timeout = 5000)]
    public void testTimerOverflowWheelLength()
    {
        using HashedWheelTimer timer = new HashedWheelTimer(
            Executors.defaultThreadFactory(), TimeSpan.FromMilliseconds(100), 32);
        CountdownEvent latch = new CountdownEvent(3);

        ITimerTask repeated = null;
        repeated = TimerTask.Create(timeout =>
        {
            timer.newTimeout(repeated, TimeSpan.FromMilliseconds(100));
            if (!latch.IsSet) latch.Signal();
        });
        timer.newTimeout(repeated, TimeSpan.FromMilliseconds(100));

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEmpty(timer.stop());
    }

    [Fact]
    public void testExecutionOnTime()
    {
        int tickDuration = 200;
        int timeout = 125;
        int maxTimeout = 2 * (tickDuration + timeout);
        using HashedWheelTimer timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(tickDuration));
        using var queue = new BlockingCollection<long>();

        int scheduledTasks = 100000;
        for (int i = 0; i < scheduledTasks; i++)
        {
            long start = SystemTimer.nanoTime();
            timer.newTimeout(TimerTask.Create(timeout =>
            {
                queue.Add((SystemTimer.nanoTime() - start) / 1_000_000);
            }), TimeSpan.FromMilliseconds(timeout));
        }

        for (int i = 0; i < scheduledTasks; i++)
        {
            Assert.True(queue.TryTake(out long delay, TimeSpan.FromSeconds(10)), "Timed out waiting for timer results.");
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
        using HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(),
            TimeSpan.FromMilliseconds(100), 32, true, 2, executor);
        timer.newTimeout(TimerTask.Create(timeout =>
        {
            timeoutLatch.Signal();
        }), TimeSpan.FromMilliseconds(timeout));

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(timeoutLatch.Wait(TimeSpan.FromSeconds(5)));
        timer.stop();
    }

    [Fact]
    public void testRejectedExecutionExceptionWhenTooManyTimeoutsAreAddedBackToBack()
    {
        using HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(),
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
        using HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(),
            TimeSpan.FromMilliseconds(tickDurationMs), 32, true, 2);
        timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromSeconds(5));
        ITimeout timeoutToCancel = timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromSeconds(5));
        Assert.True(timeoutToCancel.cancel());

        Thread.Sleep(tickDurationMs * 5);

        CountdownEvent secondLatch = new CountdownEvent(1);
        timer.newTimeout(createCountDownLatchTimerTask(secondLatch), TimeSpan.FromMilliseconds(90));

        Assert.True(secondLatch.Wait(TimeSpan.FromSeconds(5)));
        timer.stop();
    }

    [Fact(Timeout = 3000)]
    public void testNewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsExecuted()
    {
        CountdownEvent latch = new CountdownEvent(1);
        using HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(),
            TimeSpan.FromMilliseconds(25), 4, true, 2);
        timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromSeconds(5));
        timer.newTimeout(createCountDownLatchTimerTask(latch), TimeSpan.FromMilliseconds(90));

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));

        CountdownEvent secondLatch = new CountdownEvent(1);
        timer.newTimeout(createCountDownLatchTimerTask(secondLatch), TimeSpan.FromMilliseconds(90));

        Assert.True(secondLatch.Wait(TimeSpan.FromSeconds(5)));
        timer.stop();
    }

    [Fact]
    public void reportPendingTimeouts()
    {
        CountdownEvent latch = new CountdownEvent(1);
        using HashedWheelTimer timer = new HashedWheelTimer();
        ITimeout t1 = timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromMinutes(100));
        ITimeout t2 = timer.newTimeout(createNoOpTimerTask(), TimeSpan.FromMinutes(100));
        timer.newTimeout(createCountDownLatchTimerTask(latch), TimeSpan.FromMilliseconds(90));

        Assert.Equal(3, timer.pendingTimeouts());
        t1.cancel();
        t2.cancel();
        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));

        Assert.Equal(0, timer.pendingTimeouts());
        timer.stop();
    }

    [Fact]
    public void testOverflow()
    {
        using HashedWheelTimer timer = new HashedWheelTimer();
        CountdownEvent latch = new CountdownEvent(1);
        ITimeout timeout = timer.newTimeout(TimerTask.Create(timeout =>
        {
            latch.Signal();
        }), TimeSpan.MaxValue);
        Assert.False(latch.Wait(TimeSpan.FromSeconds(1)));
        timeout.cancel();
        timer.stop();
    }

    [Fact(Timeout = 3000)]
    public void testStopTimerCancelsPendingTasks()
    {
        using HashedWheelTimer timerUnprocessed = new HashedWheelTimer();
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
        using HashedWheelTimer timer = new HashedWheelTimer();
        ITimeout t1 = timer.newTimeout(TimerTask.Create(timeout =>
        {
            Assert.Fail();
        }, timeout =>
        {
            latch.Signal();
        }), TimeSpan.FromMilliseconds(90));

        Assert.Equal(1, timer.pendingTimeouts());
        t1.cancel();
        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void testPendingTimeoutsShouldBeCountedCorrectlyWhenTimeoutCancelledWithinGoalTick()
    {
        using HashedWheelTimer timer = new HashedWheelTimer();
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

        Assert.True(barrier.Wait(TimeSpan.FromSeconds(5)));
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
