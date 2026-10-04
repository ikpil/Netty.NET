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
    public void TestScheduleTimeoutShouldNotRunBeforeDelay()
    {
        using HashedWheelTimer timer = new HashedWheelTimer();
        CountdownEvent barrier = new CountdownEvent(1);
        ITimeout timeout = timer.NewTimeout(TimerTask.Create(timeout =>
        {
            Assert.Fail("This should not have run");
            barrier.Signal();
        }), TimeSpan.FromSeconds(10));
        Assert.False(barrier.Wait(TimeSpan.FromSeconds(3)));
        Assert.False(timeout.IsExpired(), "timer should not expire");
        timer.Stop();
    }

    [Fact]
    public void TestScheduleTimeoutShouldRunAfterDelay()
    {
        using HashedWheelTimer timer = new HashedWheelTimer();
        CountdownEvent barrier = new CountdownEvent(1);
        ITimeout timeout = timer.NewTimeout(TimerTask.Create(timeout =>
        {
            barrier.Signal();
        }), TimeSpan.FromSeconds(2));
        Assert.True(barrier.Wait(TimeSpan.FromSeconds(3)));
        Assert.True(timeout.IsExpired(), "timer should expire");
        timer.Stop();
    }

    [Fact(Timeout = 3000)]
    public void TestStopTimer()
    {
        CountdownEvent latch = new CountdownEvent(3);
        using HashedWheelTimer timerProcessed = new HashedWheelTimer();
        for (int i = 0; i < 3; i++)
        {
            timerProcessed.NewTimeout(TimerTask.Create(timeout =>
            {
                latch.Signal();
            }), TimeSpan.FromMilliseconds(1));
        }

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, timerProcessed.Stop().Count, "Number of unprocessed timeouts should be 0");

        using HashedWheelTimer timerUnprocessed = new HashedWheelTimer();
        for (int i = 0; i < 5; i++)
        {
            timerUnprocessed.NewTimeout(TimerTask.Create(timeout =>
            {
            }), TimeSpan.FromSeconds(5));
        }

        Thread.Sleep(1000); // sleep for a second
        Assert.NotEmpty(timerUnprocessed.Stop());
    }

    [Fact(Timeout = 3000)]
    public void TestTimerShouldThrowExceptionAfterShutdownForNewTimeouts()
    {
        CountdownEvent latch = new CountdownEvent(3);
        using HashedWheelTimer timer = new HashedWheelTimer();
        for (int i = 0; i < 3; i++)
        {
            timer.NewTimeout(TimerTask.Create(timeout =>
            {
                latch.Signal();
            }), TimeSpan.FromMilliseconds(1));
        }

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
        timer.Stop();

        Assert.Throws<InvalidOperationException>(() => timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromMilliseconds(1)));
        // All three timeouts expired and the rejected one must not be counted either.
        Assert.Equal(0, timer.PendingTimeouts());
    }

    private sealed class StoppingOnStartTimer : HashedWheelTimer
    {
        internal ISet<ITimeout> Unprocessed;
        public override void Start()
        {
            base.Start();
            // Stop the timer after newTimeout() started it but before it added the timeout to the queue. This
            // lets the worker terminate before the timeout is added.
            if (Unprocessed == null) Unprocessed = Stop();
        }
    }

    [Fact(Timeout = 5000)]
    public void TestNewTimeoutRacingWithStop()
    {
        using var timer = new StoppingOnStartTimer();
        Assert.Throws<InvalidOperationException>(() => timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromMilliseconds(1)));
        Assert.Empty(timer.Unprocessed);
        Assert.Equal(0, timer.PendingTimeouts());
    }

    [Fact(Timeout = 5000)]
    public void TestTimerOverflowWheelLength()
    {
        using HashedWheelTimer timer = new HashedWheelTimer(
            Executors.DefaultThreadFactory(), TimeSpan.FromMilliseconds(100), 32);
        CountdownEvent latch = new CountdownEvent(3);

        ITimerTask repeated = null;
        repeated = TimerTask.Create(timeout =>
        {
            timer.NewTimeout(repeated, TimeSpan.FromMilliseconds(100));
            if (!latch.IsSet) latch.Signal();
        });
        timer.NewTimeout(repeated, TimeSpan.FromMilliseconds(100));

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEmpty(timer.Stop());
    }

    [Fact]
    public void TestExecutionOnTime()
    {
        int tickDuration = 200;
        int timeout = 125;
        int maxTimeout = 2 * (tickDuration + timeout);
        using HashedWheelTimer timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(tickDuration));
        using var queue = new BlockingCollection<long>();

        int scheduledTasks = 100000;
        for (int i = 0; i < scheduledTasks; i++)
        {
            long start = SystemTimer.NanoTime();
            timer.NewTimeout(TimerTask.Create(timeout =>
            {
                queue.Add((SystemTimer.NanoTime() - start) / 1_000_000);
            }), TimeSpan.FromMilliseconds(timeout));
        }

        for (int i = 0; i < scheduledTasks; i++)
        {
            Assert.True(queue.TryTake(out long delay, TimeSpan.FromSeconds(10)), "Timed out waiting for timer results.");
            Assert.True(delay >= timeout && delay < maxTimeout,
                "Timeout + " + scheduledTasks + " delay " + delay + " must be " + timeout + " < " + maxTimeout);
        }

        timer.Stop();
    }

    [Fact]
    public void TestExecutionOnTaskExecutor()
    {
        int timeout = 10;

        CountdownEvent latch = new CountdownEvent(1);
        CountdownEvent timeoutLatch = new CountdownEvent(1);
        Action<Action> executor = command =>
        {
            try
            {
                command();
            }
            finally
            {
                latch.Signal();
            }
        };
        using HashedWheelTimer timer = new HashedWheelTimer(Executors.DefaultThreadFactory(),
            TimeSpan.FromMilliseconds(100), 32, true, 2, executor);
        timer.NewTimeout(TimerTask.Create(timeout =>
        {
            timeoutLatch.Signal();
        }), TimeSpan.FromMilliseconds(timeout));

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(timeoutLatch.Wait(TimeSpan.FromSeconds(5)));
        timer.Stop();
    }

    [Fact]
    public void TestRejectedExecutionExceptionWhenTooManyTimeoutsAreAddedBackToBack()
    {
        using HashedWheelTimer timer = new HashedWheelTimer(Executors.DefaultThreadFactory(),
            TimeSpan.FromMilliseconds(100), 32, true, 2);
        timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(5));
        timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(5));
        try
        {
            timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromMilliseconds(1));
            Assert.Fail("Timer allowed adding 3 timeouts when maxPendingTimeouts was 2");
        }
        catch (RejectedExecutionException e)
        {
            // Expected
        }
        finally
        {
            timer.Stop();
        }
    }

    [Fact]
    public void TestNewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsCancelled()
    {
        int tickDurationMs = 100;
        using HashedWheelTimer timer = new HashedWheelTimer(Executors.DefaultThreadFactory(),
            TimeSpan.FromMilliseconds(tickDurationMs), 32, true, 2);
        timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(5));
        ITimeout timeoutToCancel = timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(5));
        Assert.True(timeoutToCancel.Cancel());

        Thread.Sleep(tickDurationMs * 5);

        CountdownEvent secondLatch = new CountdownEvent(1);
        timer.NewTimeout(CreateCountDownLatchTimerTask(secondLatch), TimeSpan.FromMilliseconds(90));

        Assert.True(secondLatch.Wait(TimeSpan.FromSeconds(5)));
        timer.Stop();
    }

    [Fact(Timeout = 3000)]
    public void TestNewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsExecuted()
    {
        CountdownEvent latch = new CountdownEvent(1);
        using HashedWheelTimer timer = new HashedWheelTimer(Executors.DefaultThreadFactory(),
            TimeSpan.FromMilliseconds(25), 4, true, 2);
        timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(5));
        timer.NewTimeout(CreateCountDownLatchTimerTask(latch), TimeSpan.FromMilliseconds(90));

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));

        CountdownEvent secondLatch = new CountdownEvent(1);
        timer.NewTimeout(CreateCountDownLatchTimerTask(secondLatch), TimeSpan.FromMilliseconds(90));

        Assert.True(secondLatch.Wait(TimeSpan.FromSeconds(5)));
        timer.Stop();
    }

    [Fact]
    public void ReportPendingTimeouts()
    {
        CountdownEvent latch = new CountdownEvent(1);
        using HashedWheelTimer timer = new HashedWheelTimer();
        ITimeout t1 = timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromMinutes(100));
        ITimeout t2 = timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromMinutes(100));
        timer.NewTimeout(CreateCountDownLatchTimerTask(latch), TimeSpan.FromMilliseconds(90));

        Assert.Equal(3, timer.PendingTimeouts());
        t1.Cancel();
        t2.Cancel();
        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));

        Assert.Equal(0, timer.PendingTimeouts());
        timer.Stop();
    }

    [Fact]
    public void TestOverflow()
    {
        using HashedWheelTimer timer = new HashedWheelTimer();
        CountdownEvent latch = new CountdownEvent(1);
        ITimeout timeout = timer.NewTimeout(TimerTask.Create(timeout =>
        {
            latch.Signal();
        }), TimeSpan.MaxValue);
        Assert.False(latch.Wait(TimeSpan.FromSeconds(1)));
        timeout.Cancel();
        timer.Stop();
    }

    [Fact(Timeout = 3000)]
    public void TestStopTimerCancelsPendingTasks()
    {
        using HashedWheelTimer timerUnprocessed = new HashedWheelTimer();
        for (int i = 0; i < 5; i++)
        {
            timerUnprocessed.NewTimeout(TimerTask.Create(timeout =>
            {
            }), TimeSpan.FromSeconds(5));
        }

        Thread.Sleep(1000); // sleep for a second

        foreach (ITimeout timeout in timerUnprocessed.Stop())
        {
            Assert.True(timeout.IsCancelled(), "All unprocessed tasks should be canceled");
        }
    }

    [Fact(Timeout = 5000)]
    public void CancelWillCallCallback()
    {
        CountdownEvent latch = new CountdownEvent(1);
        using HashedWheelTimer timer = new HashedWheelTimer();
        ITimeout t1 = timer.NewTimeout(TimerTask.Create(timeout =>
        {
            Assert.Fail();
        }, timeout =>
        {
            latch.Signal();
        }), TimeSpan.FromMilliseconds(90));

        Assert.Equal(1, timer.PendingTimeouts());
        t1.Cancel();
        Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TestPendingTimeoutsShouldBeCountedCorrectlyWhenTimeoutCancelledWithinGoalTick()
    {
        using HashedWheelTimer timer = new HashedWheelTimer();
        CountdownEvent barrier = new CountdownEvent(1);
        // A total of 11 timeouts with the same delay are submitted, and they will be processed in the same tick.
        timer.NewTimeout(TimerTask.Create(timeout =>
        {
            barrier.Signal();
            Thread.Sleep(1000);
        }), TimeSpan.FromMilliseconds(200));
        List<ITimeout> timeouts = new List<ITimeout>();
        for (int i = 0; i < 10; i++)
        {
            timeouts.Add(timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromMilliseconds(200)));
        }

        Assert.True(barrier.Wait(TimeSpan.FromSeconds(5)));
        // The simulation here is that the timeout has been transferred to a bucket and is canceled before it is
        // actually expired in the goal tick.
        foreach (ITimeout timeout in timeouts)
        {
            timeout.Cancel();
        }

        Thread.Sleep(2000);
        Assert.Equal(0, timer.PendingTimeouts());
        timer.Stop();
    }

    private static ITimerTask CreateNoOpTimerTask()
    {
        return TimerTask.Create(timeout => { });
    }

    private static ITimerTask CreateCountDownLatchTimerTask(CountdownEvent latch)
    {
        return TimerTask.Create(timeout => latch.Signal());
    }
}
