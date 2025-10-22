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
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Concurrent;

public class GlobalEventExecutorTest
{
    private static readonly GlobalEventExecutor e = GlobalEventExecutor.INSTANCE;

    public GlobalEventExecutorTest()
    {
        // Wait until the global executor is stopped (just in case there is a task running due to previous test cases)
        for (;;)
        {
            if (e._thread == null || !e._thread.IsAlive)
            {
                break;
            }

            Thread.Sleep(50);
        }
    }

    [Fact(Timeout = 5000)]
    public void testAutomaticStartStop()
    {
        TestRunnable task = new TestRunnable(500);
        e.execute(task);

        // Ensure the new thread has started.
        Thread thread = e._thread;
        Assert.NotNull(thread);
        Assert.True(thread.IsAlive);

        thread.Join();
        Assert.True(task.ran.get());

        // Ensure another new thread starts again.
        task.ran.set(false);
        e.execute(task);
        Assert.NotSame(e._thread, thread);
        thread = e._thread;

        thread.Join();

        Assert.True(task.ran.get());
    }

    [Fact(Timeout = 5000)]
    public void testScheduledTasks()
    {
        TestRunnable task = new TestRunnable(0);
        IScheduledTask f = e.schedule(task, TimeSpan.FromMilliseconds(1500));
        f.sync();
        Assert.True(task.ran.get());

        // Ensure the thread is still running.
        Thread thread = e._thread;
        Assert.NotNull(thread);
        Assert.True(thread.IsAlive);

        thread.Join();
    }

    // ensure that when a task submission causes a new thread to be created, the thread inherits the thread group of the
    // submitting thread
    [Fact(Timeout = 2000)]
    public void testThreadGroup()
    {
        ThreadGroup group = new ThreadGroup("group");
        AtomicReference<ThreadGroup> capturedGroup = new AtomicReference<ThreadGroup>();
        Thread thread = new Thread(group, Runnables.Create(() =>
        {
            Thread t = e._threadFactory.newThread(EmptyRunnable.Shared);
            capturedGroup.set(t.getThreadGroup());
        }));
        thread.Start();
        thread.Join();

        Assert.Equal(group, capturedGroup.get());
    }

    [Fact(Timeout = 5000)]
    public void testTakeTask()
    {
        //add task
        TestRunnable beforeTask = new TestRunnable(0);
        e.execute(beforeTask);

        //add scheduled task
        TestRunnable scheduledTask = new TestRunnable(0);
        IScheduledTask f = e.schedule(scheduledTask, TimeSpan.FromMilliseconds(1500));

        //add task
        TestRunnable afterTask = new TestRunnable(0);
        e.execute(afterTask);

        f.sync();

        Assert.True(beforeTask.ran.get());
        Assert.True(scheduledTask.ran.get());
        Assert.True(afterTask.ran.get());
    }

    [Fact(Timeout = 5000)]
    public void testTakeTaskAlwaysHasTask()
    {
        //for https://github.com/netty/netty/issues/1614
        //add scheduled task
        TestRunnable t = new TestRunnable(0);
        IScheduledTask f = e.schedule(t, TimeSpan.FromMilliseconds(1500);

        //ensure always has at least one task in taskQueue
        //check if scheduled tasks are triggered
        e.execute(Runnables.Create(() =>
        {
            if (!f.isDone())
            {
                e.execute(this);
            }
        }));

        f.sync();

        Assert.True(t.ran.get());
    }

    internal class TestRunnable : IRunnable
    {
        internal AtomicBoolean ran = new AtomicBoolean();
        internal int delay;

        public TestRunnable(int delay)
        {
            this.delay = delay;
        }

        public void run()
        {
            try
            {
                Thread.Sleep(delay);
                ran.set(true);
            }
            catch (ThreadInterruptedException ignored)
            {
                // Ignore
            }
        }
    }
}