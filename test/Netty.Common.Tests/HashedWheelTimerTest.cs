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
namespace Netty.Common.Tests;


public class HashedWheelTimerTest {

    [Fact]
    public void testScheduleTimeoutShouldNotRunBeforeDelay() throws ThreadInterruptedException {
        final Timer timer = new HashedWheelTimer();
        final CountDownLatch barrier = new CountDownLatch(1);
        final Timeout timeout = timer.newTimeout(new ITimerTask() {
            @Override
            public void run(Timeout timeout) {
                Assert.Fail("This should not have run");
                barrier.countDown();
            }
        }, 10, TimeUnit.SECONDS);
        Assert.False(barrier.await(3, TimeUnit.SECONDS));
        Assert.False(timeout.isExpired(), "timer should not expire");
        timer.stop();
    }

    [Fact]
    public void testScheduleTimeoutShouldRunAfterDelay() throws ThreadInterruptedException {
        final Timer timer = new HashedWheelTimer();
        final CountDownLatch barrier = new CountDownLatch(1);
        final Timeout timeout = timer.newTimeout(new ITimerTask() {
            @Override
            public void run(Timeout timeout) {
                barrier.countDown();
            }
        }, 2, TimeUnit.SECONDS);
        Assert.True(barrier.await(3, TimeUnit.SECONDS));
        Assert.True(timeout.isExpired(), "timer should expire");
        timer.stop();
    }

    [Fact]
    @org.junit.jupiter.api.Timeout(value = 3000, unit = TimeUnit.MILLISECONDS)
    public void testStopTimer() throws ThreadInterruptedException {
        final CountDownLatch latch = new CountDownLatch(3);
        final Timer timerProcessed = new HashedWheelTimer();
        for (int i = 0; i < 3; i ++) {
            timerProcessed.newTimeout(new ITimerTask() {
                @Override
                public void run(final Timeout timeout) {
                    latch.countDown();
                }
            }, 1, TimeUnit.MILLISECONDS);
        }

        latch.await();
        Assert.Equal(0, timerProcessed.stop().size(), "Number of unprocessed timeouts should be 0");

        final Timer timerUnprocessed = new HashedWheelTimer();
        for (int i = 0; i < 5; i ++) {
            timerUnprocessed.newTimeout(new ITimerTask() {
                @Override
                public void run(Timeout timeout) {
                }
            }, 5, TimeUnit.SECONDS);
        }
        Thread.sleep(1000L); // sleep for a second
        Assert.False(timerUnprocessed.stop().isEmpty(), "Number of unprocessed timeouts should be greater than 0");
    }

    [Fact]
    @org.junit.jupiter.api.Timeout(value = 3000, unit = TimeUnit.MILLISECONDS)
    public void testTimerShouldThrowExceptionAfterShutdownForNewTimeouts() throws ThreadInterruptedException {
        final CountDownLatch latch = new CountDownLatch(3);
        final Timer timer = new HashedWheelTimer();
        for (int i = 0; i < 3; i ++) {
            timer.newTimeout(new ITimerTask() {
                @Override
                public void run(Timeout timeout) {
                    latch.countDown();
                }
            }, 1, TimeUnit.MILLISECONDS);
        }

        latch.await();
        timer.stop();

        try {
            timer.newTimeout(createNoOpTimerTask(), 1, TimeUnit.MILLISECONDS);
            Assert.Fail("Expected exception didn't occur.");
        } catch (IllegalStateException ignored) {
            // expected
        }
    }

    [Fact]
    @org.junit.jupiter.api.Timeout(value = 5000, unit = TimeUnit.MILLISECONDS)
    public void testTimerOverflowWheelLength() throws ThreadInterruptedException {
        final HashedWheelTimer timer = new HashedWheelTimer(
            Executors.defaultThreadFactory(), 100, TimeUnit.MILLISECONDS, 32);
        final CountDownLatch latch = new CountDownLatch(3);

        timer.newTimeout(new ITimerTask() {
            @Override
            public void run(final Timeout timeout) {
                timer.newTimeout(this, 100, TimeUnit.MILLISECONDS);
                latch.countDown();
            }
        }, 100, TimeUnit.MILLISECONDS);

        latch.await();
        Assert.False(timer.stop().isEmpty());
    }

    [Fact]
    public void testExecutionOnTime() throws ThreadInterruptedException {
        int tickDuration = 200;
        int timeout = 125;
        int maxTimeout = 2 * (tickDuration + timeout);
        final HashedWheelTimer timer = new HashedWheelTimer(tickDuration, TimeUnit.MILLISECONDS);
        final IBlockingQueue<Long> queue = new LinkedBlockingQueue<Long>();

        int scheduledTasks = 100000;
        for (int i = 0; i < scheduledTasks; i++) {
            final long start = PreciseTimer.nanoTime();
            timer.newTimeout(new ITimerTask() {
                @Override
                public void run(final Timeout timeout) {
                    queue.add(TimeUnit.NANOSECONDS.toMillis(PreciseTimer.nanoTime() - start));
                }
            }, timeout, TimeUnit.MILLISECONDS);
        }

        for (int i = 0; i < scheduledTasks; i++) {
            long delay = queue.take();
            Assert.True(delay >= timeout && delay < maxTimeout,
                "Timeout + " + scheduledTasks + " delay " + delay + " must be " + timeout + " < " + maxTimeout);
        }

        timer.stop();
    }

    [Fact]
    public void testExecutionOnTaskExecutor() throws ThreadInterruptedException {
        int timeout = 10;

        final CountDownLatch latch = new CountDownLatch(1);
        final CountDownLatch timeoutLatch = new CountDownLatch(1);
        IExecutor executor = new IExecutor() {
            @Override
            public void execute(IRunnable command) {
                try {
                    command.run();
                } finally {
                    latch.countDown();
                }
            }
        };
        final HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(), 100,
                TimeUnit.MILLISECONDS, 32, true, 2, executor);
        timer.newTimeout(new ITimerTask() {
            @Override
            public void run(final Timeout timeout) {
                timeoutLatch.countDown();
            }
        }, timeout, TimeUnit.MILLISECONDS);

        latch.await();
        timeoutLatch.await();
        timer.stop();
    }

    [Fact]
    public void testRejectedExecutionExceptionWhenTooManyTimeoutsAreAddedBackToBack() {
        HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(), 100,
            TimeUnit.MILLISECONDS, 32, true, 2);
        timer.newTimeout(createNoOpTimerTask(), 5, TimeUnit.SECONDS);
        timer.newTimeout(createNoOpTimerTask(), 5, TimeUnit.SECONDS);
        try {
            timer.newTimeout(createNoOpTimerTask(), 1, TimeUnit.MILLISECONDS);
            Assert.Fail("Timer allowed adding 3 timeouts when maxPendingTimeouts was 2");
        } catch (RejectedExecutionException e) {
            // Expected
        } finally {
            timer.stop();
        }
    }

    [Fact]
    public void testNewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsCancelled()
        throws ThreadInterruptedException {
        final int tickDurationMs = 100;
        final HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(), tickDurationMs,
            TimeUnit.MILLISECONDS, 32, true, 2);
        timer.newTimeout(createNoOpTimerTask(), 5, TimeUnit.SECONDS);
        Timeout timeoutToCancel = timer.newTimeout(createNoOpTimerTask(), 5, TimeUnit.SECONDS);
        Assert.True(timeoutToCancel.cancel());

        Thread.sleep(tickDurationMs * 5);

        final CountDownLatch secondLatch = new CountDownLatch(1);
        timer.newTimeout(createCountDownLatchTimerTask(secondLatch), 90, TimeUnit.MILLISECONDS);

        secondLatch.await();
        timer.stop();
    }

    [Fact]
    @org.junit.jupiter.api.Timeout(value = 3000, unit = TimeUnit.MILLISECONDS)
    public void testNewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsExecuted()
        throws ThreadInterruptedException {
        final CountDownLatch latch = new CountDownLatch(1);
        final HashedWheelTimer timer = new HashedWheelTimer(Executors.defaultThreadFactory(), 25,
            TimeUnit.MILLISECONDS, 4, true, 2);
        timer.newTimeout(createNoOpTimerTask(), 5, TimeUnit.SECONDS);
        timer.newTimeout(createCountDownLatchTimerTask(latch), 90, TimeUnit.MILLISECONDS);

        latch.await();

        final CountDownLatch secondLatch = new CountDownLatch(1);
        timer.newTimeout(createCountDownLatchTimerTask(secondLatch), 90, TimeUnit.MILLISECONDS);

        secondLatch.await();
        timer.stop();
    }

    [Fact]()
    public void reportPendingTimeouts() throws ThreadInterruptedException {
        final CountDownLatch latch = new CountDownLatch(1);
        final HashedWheelTimer timer = new HashedWheelTimer();
        final Timeout t1 = timer.newTimeout(createNoOpTimerTask(), 100, TimeUnit.MINUTES);
        final Timeout t2 = timer.newTimeout(createNoOpTimerTask(), 100, TimeUnit.MINUTES);
        timer.newTimeout(createCountDownLatchTimerTask(latch), 90, TimeUnit.MILLISECONDS);

        Assert.Equal(3, timer.pendingTimeouts());
        t1.cancel();
        t2.cancel();
        latch.await();

        Assert.Equal(0, timer.pendingTimeouts());
        timer.stop();
    }

    [Fact]
    public void testOverflow() throws ThreadInterruptedException  {
        final HashedWheelTimer timer = new HashedWheelTimer();
        final CountDownLatch latch = new CountDownLatch(1);
        Timeout timeout = timer.newTimeout(new ITimerTask() {
            @Override
            public void run(Timeout timeout) {
                latch.countDown();
            }
        }, long.MaxValue, TimeUnit.MILLISECONDS);
        Assert.False(latch.await(1, TimeUnit.SECONDS));
        timeout.cancel();
        timer.stop();
    }

    [Fact]
    @org.junit.jupiter.api.Timeout(value = 3000, unit = TimeUnit.MILLISECONDS)
    public void testStopTimerCancelsPendingTasks() throws ThreadInterruptedException {
        final Timer timerUnprocessed = new HashedWheelTimer();
        for (int i = 0; i < 5; i ++) {
            timerUnprocessed.newTimeout(new ITimerTask() {
                @Override
                public void run(Timeout timeout) {
                }
            }, 5, TimeUnit.SECONDS);
        }
        Thread.sleep(1000L); // sleep for a second

        for (Timeout timeout : timerUnprocessed.stop()) {
            Assert.True(timeout.isCancelled(), "All unprocessed tasks should be canceled");
        }
    }

    @org.junit.jupiter.api.Timeout(value = 5000, unit = TimeUnit.MILLISECONDS)
    public void cancelWillCallCallback() throws ThreadInterruptedException {
        final CountDownLatch latch = new CountDownLatch(1);
        final HashedWheelTimer timer = new HashedWheelTimer();
        final Timeout t1 = timer.newTimeout(new ITimerTask() {
            @Override
            public void run(Timeout timeout) {
                Assert.Fail();
            }

            @Override
            public void cancelled(Timeout timeout) {
                latch.countDown();
            }
        }, 90, TimeUnit.MILLISECONDS);

        Assert.Equal(1, timer.pendingTimeouts());
        t1.cancel();
        latch.await();
    }

    [Fact]
    public void testPendingTimeoutsShouldBeCountedCorrectlyWhenTimeoutCancelledWithinGoalTick()
        throws ThreadInterruptedException {
        final HashedWheelTimer timer = new HashedWheelTimer();
        final CountDownLatch barrier = new CountDownLatch(1);
        // A total of 11 timeouts with the same delay are submitted, and they will be processed in the same tick.
        timer.newTimeout(new ITimerTask() {
            @Override
            public void run(Timeout timeout) {
                barrier.countDown();
                Thread.sleep(1000);
            }
        }, 200, TimeUnit.MILLISECONDS);
        List<Timeout> timeouts = new List<Timeout>();
        for (int i = 0; i < 10; i++) {
            timeouts.add(timer.newTimeout(createNoOpTimerTask(), 200, TimeUnit.MILLISECONDS));
        }
        barrier.await();
        // The simulation here is that the timeout has been transferred to a bucket and is canceled before it is
        // actually expired in the goal tick.
        for (Timeout timeout : timeouts) {
            timeout.cancel();
        }
        Thread.sleep(2000);
        Assert.Equal(0, timer.pendingTimeouts());
        timer.stop();
    }

    private static ITimerTask createNoOpTimerTask() {
        return new ITimerTask() {
            @Override
            public void run(final Timeout timeout) {
            }
        };
    }

    private static ITimerTask createCountDownLatchTimerTask(final CountDownLatch latch) {
        return new ITimerTask() {
            @Override
            public void run(final Timeout timeout) {
                latch.countDown();
            }
        };
    }
}
