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
    public void testInvalidGroup()
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
            group.shutdownGracefullyAsync();
        }
    }

    public static IEnumerable<object[]> data()
    {
        yield return new object[] { 64 };
        yield return new object[] { 256 };
        yield return new object[] { 1024 };
        yield return new object[] { int.MaxValue };
    }

    [Theory(Timeout = 10000)]
    [MemberData(nameof(data))]
    public void testOrdering(int maxTaskExecutePerRun)
    {
        int threads = NettyRuntime.availableProcessors() * 2;
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
                        execute(nonStickyGroup, startLatch);
                    }
                    catch (Exception cause)
                    {
                        error.compareAndSet(null, cause);
                    }
                });
                threadList.Add(thread);
                thread.Start();
            }

            startLatch.Signal();
            foreach (Thread t in threadList)
            {
                t.Join();
            }

            Exception cause = error.get();
            if (cause != null)
            {
                throw cause;
            }
        }
        finally
        {
            nonStickyGroup.shutdownGracefullyAsync();
        }
    }

    [Theory]
    [MemberData(nameof(data))]
    public void testRaceCondition(int maxTaskExecutePerRun)
    {
        IEventExecutorGroup group = new UnorderedThreadPoolEventExecutor(1);
        NonStickyEventExecutorGroup nonStickyGroup = new NonStickyEventExecutorGroup(group, maxTaskExecutePerRun);

        try
        {
            IEventExecutor executor = nonStickyGroup.next();

            for (int j = 0; j < 5000; j++)
            {
                CountdownEvent firstCompleted = new CountdownEvent(1);
                CountdownEvent latch = new CountdownEvent(2);
                for (int i = 0; i < 2; i++)
                {
                    executor.execute(Runnables.Create(() =>
                    {
                        firstCompleted.Signal();
                        latch.Signal();
                    }));

                    Assert.True(firstCompleted.Wait(TimeSpan.FromSeconds(1)));
                }

                Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            nonStickyGroup.shutdownGracefullyAsync();
        }
    }

    private static void execute(IEventExecutorGroup group, CountdownEvent startLatch)
    {
        IEventExecutor executor = group.next();
        Assert.True(executor is IOrderedEventExecutor);
        AtomicReference<Exception> cause = new AtomicReference<Exception>();
        AtomicInteger last = new AtomicInteger();
        int tasks = 10000;
        List<Task> futures = new List<Task>(tasks);
        CountdownEvent latch = new CountdownEvent(tasks);
        startLatch.Wait();

        for (int i = 1; i <= tasks; i++)
        {
            int id = i;
            Assert.False(executor.inEventLoop());
            Assert.False(executor.inEventLoop(Thread.CurrentThread));
            futures.Add(executor.submit(Runnables.Create(() =>
            {
                try
                {
                    Assert.True(executor.inEventLoop(Thread.CurrentThread));
                    Assert.True(executor.inEventLoop());

                    if (cause.get() == null)
                    {
                        int lastId = last.get();
                        if (lastId >= id)
                        {
                            cause.compareAndSet(null, new InvalidOperationException(
                                "Out of order execution id(" + id + ") >= lastId(" + lastId + ')'));
                        }

                        if (!last.compareAndSet(lastId, id))
                        {
                            cause.compareAndSet(null, new InvalidOperationException("Concurrent execution of tasks"));
                        }
                    }
                }
                finally
                {
                    latch.Signal();
                }
            })));
        }

        latch.Wait();
        foreach (Task future in futures)
        {
            future.Wait();
        }

        Exception error = cause.get();
        if (error != null)
        {
            throw error;
        }
    }
}