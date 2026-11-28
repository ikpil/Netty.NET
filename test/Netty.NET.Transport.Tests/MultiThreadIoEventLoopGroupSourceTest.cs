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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Tests;

public class MultiThreadIoEventLoopGroupSourceTest
{
    private sealed class TestIoHandle : IIoHandle
    {
        public void Handle(IIoRegistration registration, IIoEvent readyOps)
        {
        }

        public void Close()
        {
        }
    }

    private sealed class TestIoHandlerFactory : IIoHandlerFactory
    {
        public IIoHandler NewHandler(IThreadAwareExecutor ioExecutor)
        {
            return new TestIoHandler();
        }

        public bool IsChangingThreadSupported()
        {
            return true;
        }
    }

    // CLR: the anonymous IoHandler and IoRegistration of TestIoHandlerFactory become nested classes.
    private sealed class TestIoHandler : IIoHandler
    {
        public int Run(IIoHandlerContext context)
        {
            return 0;
        }

        public void Wakeup()
        {
        }

        public IIoRegistration Register(IIoHandle handle)
        {
            return new TestIoRegistration(handle);
        }

        public bool IsCompatible(Type handleType)
        {
            return true;
        }
    }

    private sealed class TestIoRegistration(IIoHandle handle) : IIoRegistration
    {
        private readonly AtomicBoolean _valid = new AtomicBoolean(true);

        public T Attachment<T>()
        {
            return (T)handle;
        }

        public long Submit(IIoOps ops)
        {
            return 0;
        }

        public bool IsValid()
        {
            return _valid.Get();
        }

        public bool Cancel()
        {
            return _valid.CompareAndSet(true, false);
        }
    }

    private sealed class TestableIoEventLoop : SingleThreadIoEventLoop
    {
        private readonly AtomicBoolean _simulateWorkload = new AtomicBoolean(false);

        internal TestableIoEventLoop(IIoEventLoopGroup parent, Action<Action> executor, IIoHandlerFactory ioHandlerFactory)
            : base(parent, executor, ioHandlerFactory)
        {
        }

        public void SetSimulateWorkload(bool active)
        {
            _simulateWorkload.Set(active);

            if (active)
            {
                Execute(() =>
                { /* NO-OP */
                });
            }
        }

        protected override void Run()
        {
            do
            {
                if (_simulateWorkload.Get())
                {
                    Thread.Sleep(100);
                    ReportActiveIoTime(TimeSpan.FromMilliseconds(100).Ticks * 100);
                }
                else
                {
                    Action task = TakeTask();
                    if (task != null)
                    {
                        SafeExecute(task);
                    }
                }
            } while (!ConfirmShutdown() && !CanSuspend());
        }

        protected override IQueue<Action> NewTaskQueue(int maxPendingTasks)
        {
            return new LinkedBlockingQueue<Action>(maxPendingTasks);
        }
    }

    private sealed class TestMultiThreadIoEventLoopGroup : MultiThreadIoEventLoopGroup
    {
        internal TestMultiThreadIoEventLoopGroup(int minThreads, int maxThreads, TimeSpan checkPeriod)
            : base(maxThreads, (Action<Action>)null,
                  new AutoScalingEventExecutorChooserFactory(
                          minThreads, maxThreads, checkPeriod, 0.4, 0.8,
                          maxThreads, 1, 1),
                  new TestIoHandlerFactory())
        {
        }

        protected override IIoEventLoop NewChild(Action<Action> executor, IIoHandlerFactory ioHandlerFactory, params object[] args)
        {
            return new TestableIoEventLoop(this, executor, ioHandlerFactory);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestScalingWithIoRegistrationLifecycle()
    {
        TestMultiThreadIoEventLoopGroup group = new TestMultiThreadIoEventLoopGroup(0, 2, TimeSpan.FromMilliseconds(200));

        try
        {
            StartAllExecutors(group);
            Assert.Equal(2, CountActiveExecutors(group));

            WaitForSuspension(group, 1, 2000);
            Assert.Equal(1, CountActiveExecutors(group)); // One executor should have been suspended

            TestableIoEventLoop activeExecutor = (TestableIoEventLoop)FindActiveExecutor(group);
            Assert.NotNull(activeExecutor); // One executor should remain active

            IIoRegistration registration = await activeExecutor.RegisterAsync(new TestIoHandle());
            activeExecutor.SetSimulateWorkload(true);

            Thread.Sleep(450);

            // The executor will not be suspended because its workload generates utilization.
            Assert.False(activeExecutor.IsSuspended(), "Executor with active handle should not be suspended");

            activeExecutor.SetSimulateWorkload(false);
            Assert.True(registration.Cancel());

            WaitForSuspension(group, 2, 2000);
            Assert.True(activeExecutor.IsSuspended(),
                       "Executor should suspend after handle is cancelled and idle");
        }
        finally
        {
            await group.ShutdownGracefullyAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestShouldNotSuspendExecutorWithActiveRegistration()
    {
        TestMultiThreadIoEventLoopGroup group = new TestMultiThreadIoEventLoopGroup(0, 2, TimeSpan.FromMilliseconds(200));

        try
        {
            StartAllExecutors(group);
            WaitForSuspension(group, 1, 2000);
            Assert.Equal(1, CountActiveExecutors(group)); // One executor should have been suspended

            TestableIoEventLoop activeExecutor = (TestableIoEventLoop)FindActiveExecutor(group);
            Assert.NotNull(activeExecutor); // One executor should remain active

            // Register a handle with the active executor. This makes registeredChannels() return 1.
            IIoRegistration registration = await activeExecutor.RegisterAsync(new TestIoHandle());

            // The executor is now a candidate for suspension based on utilization,
            // but not based on registered channels.
            activeExecutor.SetSimulateWorkload(false);

            // Wait for a few monitor cycles. The monitor will run and see the executor is idle.
            Thread.Sleep(600);

            Assert.False(activeExecutor.IsSuspended(),
                        "Executor with an active registration should not be suspended, even if idle");
            Assert.Equal(1, CountActiveExecutors(group)); // Only one executor should still be active

            Assert.True(registration.Cancel());
            WaitForSuspension(group, 2, 2000);
            Assert.True(activeExecutor.IsSuspended(),
                       "Executor should suspend after registration is cancelled and it remains idle");
        }
        finally
        {
            await group.ShutdownGracefullyAsync();
        }
    }

    // CLR: CountdownEvent replaces CountDownLatch.
    internal static void StartAllExecutors(MultiThreadIoEventLoopGroup group)
    {
        using CountdownEvent latch = new CountdownEvent(group.ExecutorCount());

        foreach (IEventExecutor executor in group.Iterator())
        {
            executor.Execute(() => latch.Signal());
        }

        Assert.True(latch.Wait(TimeSpan.FromSeconds(2)), "Executors did not start in time");
    }

    internal static int CountActiveExecutors(MultiThreadIoEventLoopGroup group)
    {
        int activeCount = 0;

        foreach (IEventExecutor executor in group.Iterator())
        {
            if (!executor.IsSuspended())
            {
                activeCount++;
            }
        }

        return activeCount;
    }

    internal static IEventExecutor FindActiveExecutor(MultiThreadIoEventLoopGroup group)
    {
        foreach (IEventExecutor executor in group.Iterator())
        {
            if (!executor.IsSuspended())
            {
                return executor;
            }
        }

        return null;
    }

    internal static void WaitForSuspension(MultiThreadIoEventLoopGroup group, int expectedSuspendedCount,
                                          long timeoutMillis)
    {
        var watch = Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < timeoutMillis)
        {
            if (group.ExecutorCount() - CountActiveExecutors(group) >= expectedSuspendedCount)
            {
                return;
            }

            Thread.Sleep(50);
        }

        Assert.Fail("Timed out waiting for " + expectedSuspendedCount + " executor(s) to suspend.");
    }

    // CLR: shared with ScalableEventLoopGroupContractTest; a NIO-backed auto-scaling group of two workers.
    internal static MultiThreadIoEventLoopGroup NewGroup(int minimum = 0)
    {
        return new(2, (Action<Action>)null, new AutoScalingEventExecutorChooserFactory(
            minimum, 2, TimeSpan.FromMilliseconds(200), 0.4, 0.8, 2, 1, 1), NioIoHandler.NewFactory());
    }

    // CLR: shared with ScalableEventLoopGroupContractTest; keeps an executor busy until cancelled. runAllTasks
    // publishes its active time when its queue drains, so each piece of work is re-submitted from outside the loop.
    internal static void StartWorkload(IEventExecutor executor, CancellationToken cancellation)
    {
        executor.Execute(Work);

        void Work()
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            Thread.Sleep(20);

            if (!cancellation.IsCancellationRequested)
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        executor.Execute(Work);
                    }
                    catch (RejectedExecutionException)
                    {
                    }
                });
        }
    }
}
