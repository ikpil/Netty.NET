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
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Concurrent;

[Collection("Global executor")]
public class AutoScalingEventExecutorChooserFactoryTest
{

    private static void BusyTask(TimeSpan duration)
    {
        long endTime = Ticker.SystemTicker().NanoTime() + AbstractScheduledEventExecutor.ToNanos(duration);
        while (Ticker.SystemTicker().NanoTime() < endTime)
        {
            // Spin-wait to simulate CPU usage
        }
    }

    private sealed class TestEventExecutor : SingleThreadEventExecutor
    {
        private int highLoad;

        internal TestEventExecutor(IEventExecutorGroup parent, IExecutor executor)
            : base(parent, executor, true, true, DEFAULT_MAX_PENDING_EXECUTOR_TASKS, RejectedExecutionHandlers.Reject())
        {
        }

        internal void SetHighLoad(bool highLoad)
        {
            Volatile.Write(ref this.highLoad, highLoad ? 1 : 0);
        }


        protected override void Run()
        {
            do
            {
                if (Volatile.Read(ref highLoad) != 0)
                {
                    RunAllTasks(20_000_000L);
                    long busyWorkStart = Ticker().NanoTime();
                    BusyTask(TimeSpan.FromMilliseconds(35));
                    long busyWorkEnd = Ticker().NanoTime();
                    ReportActiveIoTime(busyWorkEnd - busyWorkStart);
                    try
                    {
                        Thread.Sleep(10);
                    }
                    catch (ThreadInterruptedException e)
                    {
                        Thread.CurrentThread.Interrupt();
                        break;
                    }
                }
                else
                {
                    bool ranTask = RunAllTasks();
                    if (ranTask)
                    {
                        UpdateLastExecutionTime();
                        // If we ran tasks, immediately loop back to check highLoad state
                        continue;
                    }

                    // No immediate tasks available, sleep to avoid busy waiting
                    // This allows the thread to be responsive to state changes while staying idle
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException e)
                    {
                        Thread.CurrentThread.Interrupt();
                        break;
                    }
                }
            } while (!ConfirmShutdown() && !CanSuspend());
        }
    }

    private sealed class TestEventExecutorGroup : MultithreadEventExecutorGroup
    {
        private static readonly object[] ARGS = Array.Empty<object>();

        internal TestEventExecutorGroup(int minThreads, int maxThreads, TimeSpan checkPeriod)
            : base(maxThreads,
                  new ThreadPerTaskExecutor(new DefaultThreadFactory("auto-scaling-test", true)),
                  new AutoScalingEventExecutorChooserFactory(
                          minThreads, maxThreads, checkPeriod, 0.4, 0.6,
                          maxThreads, maxThreads, 2),
                  ARGS)
        {
        }


        protected override IEventExecutor NewChild(IExecutor executor, params object[] args)
        {
            return new TestEventExecutor(this, executor);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestScaleDown()
    {
        TestEventExecutorGroup group = new TestEventExecutorGroup(1, 3, TimeSpan.FromMilliseconds(50));
        try
        {
            StartAllExecutors(group);
            Assert.Equal(3, group.ActiveExecutorCount());
            Thread.Sleep(200);

            // The monitor should have suspended 2 executors, leaving 1 active.
            Assert.Equal(1, group.ActiveExecutorCount());
        }
        finally
        {
            await group.ShutdownGracefullyAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestScaleUp()
    {
        TestEventExecutorGroup group = new TestEventExecutorGroup(1, 3, TimeSpan.FromMilliseconds(50));
        try
        {
            StartAllExecutors(group);
            Thread.Sleep(200);
            Assert.Equal(1, group.ActiveExecutorCount());

            TestEventExecutor activeExecutor = null;
            foreach (IEventExecutor exec in group.Iterator())
            {
                if (!exec.IsSuspended())
                {
                    activeExecutor = (TestEventExecutor)exec;
                    break;
                }
            }
            if (activeExecutor == null)
            {
                Assert.Fail("Could not find an active executor to stress.");
            }

            activeExecutor.SetHighLoad(true);

            // The monitor will see high utilization on the active thread. After 2 cycles (100 ms),
            // it will decide to scale up.
            long deadline = Ticker.SystemTicker().NanoTime() + 5_000_000_000L;
            while (group.ActiveExecutorCount() < 2 && Ticker.SystemTicker().NanoTime() < deadline)
            {
                Thread.Sleep(50);
            }
            Assert.Equal(2, group.ActiveExecutorCount(),
                         "Should scale up to 2 after stressing one executor.");

            foreach (IEventExecutor exec in group.Iterator())
            {
                if (!exec.IsSuspended())
                {
                    ((TestEventExecutor)exec).SetHighLoad(true);
                }
            }

            while (group.ActiveExecutorCount() < 3 && Ticker.SystemTicker().NanoTime() < deadline)
            {
                Thread.Sleep(50);
            }
            Assert.Equal(3, group.ActiveExecutorCount(),
                         "Should scale up to 3 after stressing two executors.");
        }
        finally
        {
            await group.ShutdownGracefullyAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestScaleDownWhenExecutorIsNotStarted()
    {
        TestEventExecutorGroup group = new TestEventExecutorGroup(2, 4, TimeSpan.FromMilliseconds(50));
        try
        {
            // Do not start executors
            Thread.Sleep(200);
            Assert.Equal(2, group.ActiveExecutorCount(), "Should not scale below minThreads");
        }
        finally
        {
            await group.ShutdownGracefullyAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestScaleDownDoesNotGoBelowMinThreads()
    {
        TestEventExecutorGroup group = new TestEventExecutorGroup(2, 4, TimeSpan.FromMilliseconds(50));
        try
        {
            StartAllExecutors(group);
            Thread.Sleep(200);
            Assert.Equal(2, group.ActiveExecutorCount(), "Should not scale below minThreads");
        }
        finally
        {
            await group.ShutdownGracefullyAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestScaleUpDoesNotExceedMaxThreads()
    {
        TestEventExecutorGroup group = new TestEventExecutorGroup(1, 2, TimeSpan.FromMilliseconds(50));
        try
        {
            StartAllExecutors(group);
            Thread.Sleep(200); // Allow time for initial scale-down to minThreads
            Assert.Equal(1, group.ActiveExecutorCount());

            TestEventExecutor activeExecutor = null;
            foreach (IEventExecutor exec in group.Iterator())
            {
                if (!exec.IsSuspended())
                {
                    activeExecutor = (TestEventExecutor)exec;
                    break;
                }
            }
            if (activeExecutor == null)
            {
                Assert.Fail("Could not find an active executor to stress.");
            }
            activeExecutor.SetHighLoad(true);

            // Wait for the UtilizationMonitor to react and scale up.
            long deadline = Ticker.SystemTicker().NanoTime() + 5_000_000_000L;
            while (group.ActiveExecutorCount() < 2 && Ticker.SystemTicker().NanoTime() < deadline)
            {
                Thread.Sleep(50);
            }
            Assert.Equal(2, group.ActiveExecutorCount(), "Should scale up to maxThreads");

            // Now that we have scaled up, put all active executors under a high load
            // to prevent the new one from being scaled back down immediately.
            foreach (IEventExecutor exec in group.Iterator())
            {
                if (!exec.IsSuspended())
                {
                    ((TestEventExecutor)exec).SetHighLoad(true);
                }
            }

            // Further calls to next() should not increase the count, and the group should
            // remain at its max size because both threads are now busy.
            group.Next();
            Thread.Sleep(200); // Give the monitor time to check again.

            Assert.Equal(2, group.ActiveExecutorCount(),
                         "Should not scale back down while load is high");
        }
        finally
        {
            await group.ShutdownGracefullyAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestSmarterPickingConsolidatesWorkOnActiveExecutor()
    {
        TestEventExecutorGroup group = new TestEventExecutorGroup(1, 3, TimeSpan.FromMilliseconds(50));
        try
        {
            StartAllExecutors(group);

            long deadline = Ticker.SystemTicker().NanoTime() + 5_000_000_000L;
            while (group.ActiveExecutorCount() > 1 && Ticker.SystemTicker().NanoTime() < deadline)
            {
                Thread.Sleep(50);
            }
            Assert.Equal(1, group.ActiveExecutorCount(),
                         "Group should scale down to 1 active executor");

            // Simulate a slow trickle of new work (e.g., new connections) by calling next() a few times.
            for (int i = 0; i < 5; i++)
            {
                group.Next().Execute(Runnables.Empty);
                Thread.Sleep(20);
            }

            Assert.Equal(1, group.ActiveExecutorCount(),
                         "Should consolidate the trickle of work onto the single active executor, without" +
                         " waking up the suspended ones");
        }
        finally
        {
            await group.ShutdownGracefullyAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestMetricsProvideCorrectUtilizationAndActiveExecutorCount()
    {
        TestEventExecutorGroup group = new TestEventExecutorGroup(1, 3, TimeSpan.FromMilliseconds(50));
        try
        {
            StartAllExecutors(group);
            long deadline = Ticker.SystemTicker().NanoTime() + 5_000_000_000L;
            while (group.ActiveExecutorCount() > 1 && Ticker.SystemTicker().NanoTime() < deadline)
            {
                Thread.Sleep(50);
            }
            Assert.Equal(1, group.ActiveExecutorCount(), "Should have scaled down to 1 active executor.");

            TestEventExecutor activeExecutor = null;
            foreach (IEventExecutor exec in group.Iterator())
            {
                if (!exec.IsSuspended())
                {
                    activeExecutor = (TestEventExecutor)exec;
                    break;
                }
            }
            if (activeExecutor == null)
            {
                Assert.Fail("Could not find an active executor.");
            }

            activeExecutor.SetHighLoad(true);

            while (Ticker.SystemTicker().NanoTime() < deadline)
            {
                IReadOnlyList<AutoScalingUtilizationMetric> currentMetrics = group.ExecutorUtilizations();
                TestEventExecutor finalActiveExecutor = activeExecutor;
                double utilization = currentMetrics.FirstOrDefault(metric => ReferenceEquals(metric.Executor(), finalActiveExecutor))?.Utilization() ?? 0.0;
                if (utilization > 0.4)
                {
                    break;
                }
                Thread.Sleep(50);
            }

            Assert.Equal(1, group.ActiveExecutorCount(), "Active count should still be 1 before scaling up.");

            IReadOnlyList<AutoScalingUtilizationMetric> utilizationMetrics = group.ExecutorUtilizations();
            Assert.Equal(3, utilizationMetrics.Count, "Utilization list should report on all executors.");

            TestEventExecutor finalActiveExecutor2 = activeExecutor;
            double activeUtilization = utilizationMetrics.FirstOrDefault(metric => ReferenceEquals(metric.Executor(), finalActiveExecutor2))?.Utilization() ?? 0.0;
            Assert.True(activeUtilization > 0.4,
                       "Active executor should have utilization above the scale-down threshold. " +
                       "Was: " + activeUtilization);

            TestEventExecutor finalActiveExecutor1 = activeExecutor;
            foreach (var metric in utilizationMetrics.Where(metric => metric.Executor() != finalActiveExecutor1))
            {
                Assert.True(metric.Executor().IsSuspended(), "Other executors should be suspended.");
                Assert.Equal(0.0, metric.Utilization(),
                             "Suspended executor should have 0.0 utilization.");
            }
        }
        finally
        {
            await group.ShutdownGracefullyAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static void StartAllExecutors(MultithreadEventExecutorGroup group)
    {
        using var startLatch = new CountdownEvent(group.ExecutorCount());
        foreach (IEventExecutor executor in group.Iterator())
        {
            executor.Execute(Runnables.Create(() => startLatch.Signal()));
        }
        Assert.True(startLatch.Wait(TimeSpan.FromSeconds(5)));
    }
}
