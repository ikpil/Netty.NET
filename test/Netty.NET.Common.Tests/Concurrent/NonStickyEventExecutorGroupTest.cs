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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Concurrent;

public class NonStickyEventExecutorGroupTest
{
    private static readonly string PARAMETERIZED_NAME = "{index}: maxTaskExecutePerRun = {0}";

    [Fact]
    public void TestInvalidGroup()
    {
        IEventExecutorGroup group = new DefaultEventExecutorGroup(1);
        try
        {
            Assert.Throws<ArgumentException>(() =>
            {
                new NonStickyEventExecutorGroup(group);
            });
        }
        finally
        {
            Stop(group);
        }
    }

    public static IEnumerable<object[]> Data()
    {
        yield return new object[] { 64 };
        yield return new object[] { 256 };
        yield return new object[] { 1024 };
        yield return new object[] { int.MaxValue };
    }

    [Theory(Timeout = 10000)]
    [MemberData(nameof(Data))]
    public void TestOrdering(int maxTaskExecutePerRun)
    {
        int threads = NettyRuntime.AvailableProcessors() * 2;
        IEventExecutorGroup group = new UnorderedThreadPoolEventExecutor(threads);
        NonStickyEventExecutorGroup nonStickyGroup = new NonStickyEventExecutorGroup(group, maxTaskExecutePerRun);
        try
        {
            CountdownEvent startLatch = new CountdownEvent(1);
            AtomicReference<Exception> error = new AtomicReference<Exception>();
            List<Thread> threadList = new List<Thread>(threads);
            for (int i = 0; i < threads; i++)
            {
                Thread thread = new Thread(() =>
                {
                    try
                    {
                        Execute(nonStickyGroup, startLatch);
                    }
                    catch (Exception cause)
                    {
                        error.CompareAndSet(null, cause);
                    }
                });
                threadList.Add(thread);
                thread.Start();
            }

            startLatch.Signal();
            foreach (Thread t in threadList)
            {
                Assert.True(t.Join(TimeSpan.FromSeconds(30)));
            }

            Exception cause = error.Get();
            if (cause != null)
            {
                throw cause;
            }
        }
        finally
        {
            Stop(nonStickyGroup);
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestRaceCondition(int maxTaskExecutePerRun)
    {
        IEventExecutorGroup group = new UnorderedThreadPoolEventExecutor(1);
        NonStickyEventExecutorGroup nonStickyGroup = new NonStickyEventExecutorGroup(group, maxTaskExecutePerRun);

        try
        {
            IEventExecutor executor = nonStickyGroup.Next();

            for (int j = 0; j < 5000; j++)
            {
                CountdownEvent firstCompleted = new CountdownEvent(1);
                CountdownEvent latch = new CountdownEvent(2);
                for (int i = 0; i < 2; i++)
                {
                    executor.Execute(Runnables.Create(() =>
                    {
                        if (!firstCompleted.IsSet) firstCompleted.Signal();
                        latch.Signal();
                    }));

                    Assert.True(firstCompleted.Wait(TimeSpan.FromSeconds(1)));
                }

                Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            Stop(nonStickyGroup);
        }
    }

    private static void Stop(IEventExecutorGroup group)
    {
        group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        if (!group.AwaitTermination(TimeSpan.FromSeconds(5))) group.ShutdownNow();
        Assert.True(group.AwaitTermination(TimeSpan.FromSeconds(5)));
    }

    private sealed class RejectSecondGroup : AbstractEventExecutorGroup
    {
        private readonly UnorderedThreadPoolEventExecutor underlying;
        private readonly IEventExecutor executor;
        internal RejectSecondGroup(UnorderedThreadPoolEventExecutor underlying)
        {
            this.underlying = underlying;
            executor = new RejectSecondExecutor(this, underlying);
        }
        public override IEventExecutor Next() => executor;
        public override IEnumerable<IEventExecutor> Iterator() => new[] { executor };
        public override void Shutdown() => underlying.ShutdownGracefullyAsync();
        public override bool IsShuttingDown() => underlying.IsShuttingDown();
        public override bool IsShutdown() => underlying.IsShutdown();
        public override bool IsTerminated() => underlying.IsTerminated();
        public override bool AwaitTermination(TimeSpan timeout) => underlying.AwaitTermination(timeout);
        public override Task Termination => underlying.Termination;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) =>
            underlying.ShutdownGracefullyAsync(quietPeriod, timeout);
    }

    private sealed class RejectSecondExecutor : AbstractEventExecutor
    {
        private readonly UnorderedThreadPoolEventExecutor underlying;
        private int executeCount;
        internal RejectSecondExecutor(IEventExecutorGroup parent, UnorderedThreadPoolEventExecutor underlying) : base(parent) =>
            this.underlying = underlying;
        public override bool InEventLoop(Thread thread) => underlying.InEventLoop(thread);
        public override void Shutdown() => underlying.ShutdownGracefullyAsync();
        public override bool IsShuttingDown() => underlying.IsShuttingDown();
        public override bool IsShutdown() => underlying.IsShutdown();
        public override bool IsTerminated() => underlying.IsTerminated();
        public override bool AwaitTermination(TimeSpan timeout) => underlying.AwaitTermination(timeout);
        public override Task Termination => underlying.Termination;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) =>
            underlying.ShutdownGracefullyAsync(quietPeriod, timeout);
        public override void Execute(Action command)
        {
            IRunnable queuedTask = ExecutorWork.Unwrap(command, nameof(command));
            // Reject the 2nd execute() call (the reschedule attempt)
            // 1st call: initial task submission
            // 2nd call: reschedule after maxTaskExecutePerRun
            if (Interlocked.Increment(ref executeCount) == 2)
                throw new RejectedExecutionException("Simulated queue full");
            underlying.Execute(queuedTask);
        }
    }

    [Fact]
    public void TestInEventLoopAfterReschedulingFailure()
    {
        var underlying = new UnorderedThreadPoolEventExecutor(1);
        var wrapper = new RejectSecondGroup(underlying);

        // Use maxTaskExecutePerRun=1 so reschedule happens after first task
        var nonStickyGroup = new NonStickyEventExecutorGroup(wrapper, 1);
        try
        {
            var executor = nonStickyGroup.Next();
            using var latch = new CountdownEvent(1);
            // CLR nullable bool is a value type; the latch publishes the single writer's result.
            bool? inEventLoopResult = null;

            // Submit 2 tasks:
            // Task 1: completes, triggers reschedule which will be rejected
            // Task 2: verifies inEventLoop() still works after failed reschedule
            executor.Execute(Runnables.Create(() =>
            {
                // First task - will trigger reschedule attempt that fails
            }));
            executor.Execute(Runnables.Create(() =>
            {
                // This runs AFTER the failed rescheduling
                // WITHOUT line 262 fix: executingThread is null, inEventLoop() returns false
                // WITH line 262 fix: executingThread restored, inEventLoop() returns true
                inEventLoopResult = executor.InEventLoop();
                latch.Signal();
            }));
            Assert.True(latch.Wait(TimeSpan.FromSeconds(5)), "Tasks should complete");
            bool? result = inEventLoopResult;
            Assert.NotNull(result, "inEventLoop() should have been called");
            Assert.True(result.Value,
                "inEventLoop() should return true even after failed reschedule attempt. " +
                "This indicates executingThread was properly restored in the exception handler.");
        }
        finally { Stop(nonStickyGroup); Stop(underlying); }
    }
    private static void Execute(IEventExecutorGroup group, CountdownEvent startLatch)
    {
        IEventExecutor executor = group.Next();
        Assert.True(executor is IOrderedEventExecutor);
        AtomicReference<Exception> cause = new AtomicReference<Exception>();
        AtomicInteger last = new AtomicInteger();
        int tasks = 10000;
        List<Task> completions = new List<Task>(tasks);
        CountdownEvent latch = new CountdownEvent(tasks);
        Assert.True(startLatch.Wait(TimeSpan.FromSeconds(5)));

        for (int i = 1; i <= tasks; i++)
        {
            int id = i;
            Assert.False(executor.InEventLoop());
            Assert.False(executor.InEventLoop(Thread.CurrentThread));
            completions.Add(executor.SubmitAsync(() =>
            {
                try
                {
                    Assert.True(executor.InEventLoop(Thread.CurrentThread));
                    Assert.True(executor.InEventLoop());

                    if (cause.Get() == null)
                    {
                        int lastId = last.Get();
                        if (lastId >= id)
                        {
                            cause.CompareAndSet(null, new InvalidOperationException(
                                "Out of order execution id(" + id + ") >= lastId(" + lastId + ')'));
                        }

                        if (!last.CompareAndSet(lastId, id))
                        {
                            cause.CompareAndSet(null, new InvalidOperationException("Concurrent execution of tasks"));
                        }
                    }
                }
                finally
                {
                    latch.Signal();
                }
            }));
        }

        Assert.True(latch.Wait(TimeSpan.FromSeconds(30)));
        // Native Tasks preserve completion/failure observation after every original
        // ordering/affinity check. This scenario does not exercise Java interruption.
        Task.WhenAll(completions).GetAwaiter().GetResult();

        Exception error = cause.Get();
        if (error != null)
        {
            throw error;
        }
    }
}
