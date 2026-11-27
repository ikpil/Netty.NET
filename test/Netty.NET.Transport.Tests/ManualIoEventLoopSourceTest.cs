/*
 * Copyright 2025 The Netty Project
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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1031 // The source drives the loop synchronously on one physical thread.
public class ManualIoEventLoopSourceTest
{
    [Fact]
    public void TestRunNow()
    {
        Thread currentThread = Thread.CurrentThread;
        using SemaphoreSlim semaphore = new SemaphoreSlim(0);
        ManualIoEventLoop eventLoop = new ManualIoEventLoop(currentThread, new TestIoHandlerFactory(semaphore));
        Assert.Equal(0, eventLoop.RunNow());

        TestRunnable runnable = new TestRunnable();
        eventLoop.Execute(runnable.Run);
        Assert.False(runnable.IsDone());

        Assert.Equal(1, eventLoop.RunNow());
        Assert.True(runnable.IsDone());
        eventLoop.StopAsync();
        while (!eventLoop.IsTerminated())
        {
            eventLoop.RunNow();
        }

        eventLoop.TerminationFuture.GetAwaiter().GetResult();
    }

    [Fact]
    public void TestRun()
    {
        Thread currentThread = Thread.CurrentThread;
        using SemaphoreSlim semaphore = new SemaphoreSlim(0);
        ManualIoEventLoop eventLoop = new ManualIoEventLoop(currentThread, new TestIoHandlerFactory(semaphore));

        long waitTime = TimeSpan.FromMilliseconds(200).Ticks * 100;
        long current = SystemTimer.NanoTime();
        Assert.Equal(0, eventLoop.Run(waitTime));
        long actualNanos = SystemTimer.NanoTime() - current;
        Assert.True(actualNanos >= waitTime);

        TestRunnable runnable = new TestRunnable();
        eventLoop.Execute(runnable.Run);
        Assert.False(runnable.IsDone());

        waitTime = TimeSpan.FromSeconds(1).Ticks * 100;
        current = SystemTimer.NanoTime();
        Assert.Equal(1, eventLoop.Run(waitTime));
        Assert.True(waitTime > SystemTimer.NanoTime() - current);

        Assert.True(runnable.IsDone());
        eventLoop.StopAsync();

        while (!eventLoop.IsTerminated())
        {
            eventLoop.RunNow();
        }
        eventLoop.TerminationFuture.GetAwaiter().GetResult();
    }

    [Fact]
    public void TestShutdownOutSideOfOwningThread()
    {
        using SemaphoreSlim semaphore = new SemaphoreSlim(0);
        Thread ownerThread = new Thread(() =>
        {
        });
        ManualIoEventLoop eventLoop = new ManualIoEventLoop(ownerThread, new TestIoHandlerFactory(semaphore));
        eventLoop.StopAsync();
        Assert.True(eventLoop.IsShuttingDown());
        // we expect wakeup to be called!
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public void TestCallFromWrongThread()
    {
        Thread thread = new Thread(() =>
        {
        });
        using SemaphoreSlim semaphore = new SemaphoreSlim(0);
        ManualIoEventLoop eventLoop = new ManualIoEventLoop(thread, new TestIoHandlerFactory(semaphore));

        // CLR: InvalidOperationException replaces IllegalStateException.
        Assert.Throws<InvalidOperationException>(() => eventLoop.RunNow());
        Assert.Throws<InvalidOperationException>(() => eventLoop.Run(10));
    }

    [Fact]
    public void TestThreadEventExecutorMap()
    {
        BlockingCollection<IEventExecutor> queue = new BlockingCollection<IEventExecutor>();
        using SemaphoreSlim semaphore = new SemaphoreSlim(0);
        ManualIoEventLoop eventLoop = new ManualIoEventLoop(Thread.CurrentThread, new TestIoHandlerFactory(semaphore));
        Assert.Null(ThreadExecutorMap.CurrentExecutor());
        eventLoop.Execute(() => queue.Add(ThreadExecutorMap.CurrentExecutor()));
        Assert.Equal(1, eventLoop.RunNow());
        Assert.Same(eventLoop, queue.Take());
        eventLoop.StopAsync();

        while (!eventLoop.IsTerminated())
        {
            eventLoop.RunNow();
        }
        eventLoop.TerminationFuture.GetAwaiter().GetResult();
    }

    [Fact(Timeout = 10000)]
    public async Task TestDelayOwningThread()
    {
        using SemaphoreSlim semaphore = new SemaphoreSlim(0);
        ManualIoEventLoop eventLoop = new ManualIoEventLoop((Thread)null, new TestIoHandlerFactory(semaphore));
        Exception failure = null;
        Thread thread = new Thread(() =>
        {
            try
            {
                eventLoop.SetOwningThread(Thread.CurrentThread);
                Assert.True(eventLoop.InEventLoop());
                while (!eventLoop.IsTerminated())
                {
                    eventLoop.RunNow();
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        Assert.False(eventLoop.InEventLoop());
        TaskCompletionSource cf = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        eventLoop.Execute(() =>
        {
            Assert.True(eventLoop.InEventLoop());
            cf.SetResult();
        });
        thread.Start();
        await cf.Task;
        _ = eventLoop.ShutdownGracefullyAsync();
        thread.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void TestRunWithoutOwner()
    {
        ManualIoEventLoop eventLoop = new ManualIoEventLoop((Thread)null, new TestIoHandlerFactory(new SemaphoreSlim(0)));
        // prior to setOwningThread, runNow is forbidden
        Assert.Throws<InvalidOperationException>(() => eventLoop.RunNow());
        eventLoop.SetOwningThread(Thread.CurrentThread);
        eventLoop.RunNow(); // runs fine
        _ = eventLoop.ShutdownGracefullyAsync();
    }

    [Fact]
    public void TestRunNonIoTasksSetupEventExecutor()
    {
        MockTicker ticker = Ticker.NewMockTicker();
        ManualIoEventLoop eventLoop = new ManualIoEventLoop(
                null, Thread.CurrentThread, LocalIoHandler.NewFactory(), ticker);
        Assert.Null(ThreadExecutorMap.CurrentExecutor());
        AtomicBoolean executed = new AtomicBoolean(false);
        eventLoop.Execute(() =>
        {
            Assert.True(executed.CompareAndSet(false, true));
            Assert.Same(eventLoop, ThreadExecutorMap.CurrentExecutor());
        });
        Assert.Equal(1, eventLoop.RunNonBlockingTasks(0));
        Assert.True(executed.Get());
        _ = eventLoop.ShutdownGracefullyAsync();
    }

    public enum RunMode
    {
        Now,
        Wait,
        NonIoNow,
    }

    // CLR: the enum method runWith becomes a static helper.
    private static int RunWith(RunMode mode, ManualIoEventLoop el, long timeoutNs)
    {
        switch (mode)
        {
            case RunMode.Now:
                return el.RunNow(timeoutNs);
            case RunMode.Wait:
                return el.Run(TimeSpan.FromHours(1).Ticks * 100, timeoutNs);
            case RunMode.NonIoNow:
                return el.RunNonBlockingTasks(timeoutNs);
            default:
                throw new InvalidOperationException("Unknown run mode: " + mode);
        }
    }

    [Theory]
    [InlineData(RunMode.Now)]
    [InlineData(RunMode.Wait)]
    [InlineData(RunMode.NonIoNow)]
    public void TestTasksWhileExpiringTimeout(RunMode mode)
    {
        MockTicker ticker = Ticker.NewMockTicker();
        ManualIoEventLoop eventLoop = new ManualIoEventLoop(
                null, Thread.CurrentThread, LocalIoHandler.NewFactory(), ticker);
        AtomicBoolean executedFirst = new AtomicBoolean(false);
        eventLoop.Execute(() =>
        {
            Assert.True(executedFirst.CompareAndSet(false, true));
            ticker.Advance(1);
        });
        AtomicBoolean canExecute = new AtomicBoolean(false);
        AtomicBoolean executedSecond = new AtomicBoolean(false);
        eventLoop.Execute(() =>
        {
            if (!canExecute.Get())
            {
                Assert.Fail("Should not be executed");
            }
            else
            {
                Assert.True(executedSecond.CompareAndSet(false, true));
            }
        });
        Assert.Equal(1, RunWith(mode, eventLoop, 1));
        Assert.True(executedFirst.Get());
        Assert.False(executedSecond.Get());
        canExecute.Set(true);
        Assert.Equal(1, RunWith(mode, eventLoop, 1));
        _ = eventLoop.ShutdownGracefullyAsync();
    }

    [Fact]
    public void TestSetOwnerMultipleTimes()
    {
        ManualIoEventLoop eventLoop = new ManualIoEventLoop((Thread)null, new TestIoHandlerFactory(new SemaphoreSlim(0)));
        eventLoop.SetOwningThread(Thread.CurrentThread);
        Assert.Throws<InvalidOperationException>(() => eventLoop.SetOwningThread(Thread.CurrentThread));
        _ = eventLoop.ShutdownGracefullyAsync();
    }

    [Fact]
    public void TestTicker()
    {
        MockTicker ticker = Ticker.NewMockTicker();
        ManualIoEventLoop eventLoop = new ManualIoEventLoop(
                null, Thread.CurrentThread, NioIoHandler.NewFactory(), ticker);
        AtomicInteger counter = new AtomicInteger();
        _ = eventLoop.ScheduleAsync(() => counter.IncrementAndGet(), TimeSpan.FromSeconds(60));
        eventLoop.RunNow();
        Assert.Equal(0, counter.Get());
        ticker.Advance(TimeSpan.FromSeconds(50).Ticks * 100);
        eventLoop.RunNow();
        Assert.Equal(0, counter.Get());
        ticker.Advance(TimeSpan.FromSeconds(20).Ticks * 100);
        eventLoop.RunNow();
        Assert.Equal(1, counter.Get());
        _ = eventLoop.ShutdownGracefullyAsync();
    }

    // CLR: drives a caller-owned loop to termination for the supplemental tests that share it.
    internal static void Terminate(ManualIoEventLoop loop)
    {
        var termination = loop.StopAsync();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!loop.IsTerminated())
        {
            Assert.True(DateTime.UtcNow < deadline, "The manual loop did not terminate.");
            loop.RunNow();
        }
        termination.GetAwaiter().GetResult();
    }

    private sealed class TestRunnable
    {
        private bool _done;

        public void Run()
        {
            _done = true;
        }

        internal bool IsDone()
        {
            return _done;
        }
    }

    // CLR: the executor -> new TestIoHandler(semaphore) lambdas become a factory class.
    private sealed class TestIoHandlerFactory(SemaphoreSlim semaphore) : IIoHandlerFactory
    {
        public IIoHandler NewHandler(IThreadAwareExecutor ioExecutor)
        {
            return new TestIoHandler(semaphore);
        }
    }

    private class TestIoHandler : IIoHandler
    {
        private readonly SemaphoreSlim _semaphore;

        internal TestIoHandler(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void PrepareToDestroy()
        {
            // NOOP
        }

        public void Destroy()
        {
            // NOOP
        }

        public IIoRegistration Register(IIoHandle handle)
        {
            return new TestIoRegistration();
        }

        // CLR: the anonymous IoRegistration of register(IoHandle) becomes a nested class.
        private sealed class TestIoRegistration : IIoRegistration
        {
            private readonly AtomicBoolean _canceled = new AtomicBoolean();

            public T Attachment<T>()
            {
                return default;
            }

            public long Submit(IIoOps ops)
            {
                return 0;
            }

            public bool Cancel()
            {
                return _canceled.CompareAndSet(false, true);
            }

            public bool IsValid()
            {
                return !_canceled.Get();
            }
        }

        public void Wakeup()
        {
            _semaphore.Release();
        }

        public int Run(IIoHandlerContext context)
        {
            if (context.CanBlock())
            {
                if (context.DeadlineNanos() != -1)
                {
                    long delay = context.DelayNanos(SystemTimer.NanoTime());
                    // CLR: Semaphore.tryAcquire(delay, NANOSECONDS) returns false only after the whole timeout elapsed.
                    // SemaphoreSlim.Wait has millisecond resolution and a Windows timed wait can end before the
                    // high-resolution clock reaches the deadline (testRun failed once at its >= waitTime assertion),
                    // so wait again for the remainder until a permit arrives or the deadline passed.
                    long deadline = SystemTimer.NanoTime() + delay;
                    for (long remaining = delay; remaining > 0; remaining = deadline - SystemTimer.NanoTime())
                    {
                        if (_semaphore.Wait(TimeSpan.FromMilliseconds(Math.Ceiling(Math.Min(remaining, TimeSpan.FromDays(1).Ticks * 100L) / 1_000_000d))))
                        {
                            break;
                        }
                    }
                }
            }
            return 0;
        }

        public bool IsCompatible(Type handleType)
        {
            return false;
        }
    }
}
