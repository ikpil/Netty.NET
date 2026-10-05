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
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Concurrent;

[CollectionDefinition("Global executor", DisableParallelization = true)]
public class GlobalExecutorCollection { }

[Collection("Global executor")]
public class GlobalEventExecutorTest
{
    private static readonly GlobalEventExecutor e = GlobalEventExecutor.INSTANCE;

    public GlobalEventExecutorTest() => SetUp();

    public void SetUp()
    {
        // Wait until the global executor is stopped (just in case there is a task running due to previous test cases)
        var wait = Stopwatch.StartNew();
        for (;;)
        {
            if (e._thread == null || !e._thread.IsAlive) break;
            Assert.True(wait.Elapsed < TimeSpan.FromSeconds(5), "Global executor did not become inactive.");
            Thread.Sleep(50);
        }
    }

    [Fact]
    public void TestAutomaticStartStop()
    {
        var task = new TestRunnable(500);
        e.Execute(task.Run);

        // Ensure the new thread has started.
        Thread thread = e._thread;
        Assert.NotNull(thread);
        Assert.True(thread.IsAlive);

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.True(task.ran);

        // Ensure another new thread starts again.
        task.ran = false;
        e.Execute(task.Run);
        Assert.NotSame(e._thread, thread);
        thread = e._thread;
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.True(task.ran);
    }

    [Fact]
    public void TestScheduledTasks()
    {
        var task = new TestRunnable(0);
        var f = e.ScheduleAsync(task.Run, TimeSpan.FromMilliseconds(1500));
        Sync(f);
        Assert.True(task.ran);

        // Ensure the thread is still running.
        Thread thread = e._thread;
        Assert.NotNull(thread);
        Assert.True(thread.IsAlive);
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    // ensure that when a task submission causes a new thread to be created, the thread inherits the thread group of the
    // submitting thread
    [Fact]
    public void TestThreadGroup()
    {
        var group = new ThreadGroup("group");
        ThreadGroup capturedGroup = null;
        // CLR groups preserve Netty thread identity through weak metadata.
        var thread = group.NewThread(() =>
        {
            Thread t = e._threadFactory.NewThread(() => { });
            capturedGroup = ThreadGroup.GetThreadGroup(t);
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        Assert.Same(group, capturedGroup);
    }

    [Fact]
    public void TestTakeTask()
    {
        //add task
        var beforeTask = new TestRunnable(0);
        e.Execute(beforeTask.Run);

        //add scheduled task
        var scheduledTask = new TestRunnable(0);
        var f = e.ScheduleAsync(scheduledTask.Run, TimeSpan.FromMilliseconds(1500));

        //add task
        var afterTask = new TestRunnable(0);
        e.Execute(afterTask.Run);
        Sync(f);

        Assert.True(beforeTask.ran);
        Assert.True(scheduledTask.ran);
        Assert.True(afterTask.ran);
    }

    [Fact]
    public void TestTakeTaskAlwaysHasTask()
    {
        //for https://github.com/netty/netty/issues/1614
        //add scheduled task
        var t = new TestRunnable(0);
        var f = e.ScheduleAsync(t.Run, TimeSpan.FromMilliseconds(1500));

        //ensure always has at least one task in taskQueue
        //check if scheduled tasks are triggered
        Action repeat = null;
        repeat = () =>
        {
            if (!f.IsCompleted) e.Execute(repeat);
        };
        e.Execute(repeat);
        Sync(f);
        Assert.True(t.ran);
    }

    [Fact]
    public void TestTerminationFutureFailureDoesNotFillInStackTrace()
    {
        // The GlobalEventExecutor.INSTANCE is a singleton that lives for the lifetime of the Classloader that
        // loaded it. It holds on to the failure of its terminationFuture forever, so that failure must not
        // populate a (native) backtrace: doing so would pin the Classloader of whatever thread happened to
        // trigger the lazy initialization of INSTANCE (see https://github.com/netty/netty/issues/17128).
        Exception cause = e.Termination.Exception.InnerException;
        Assert.NotNull(cause);
        Assert.IsAssignableFrom<NotSupportedException>(cause);

        string before = cause.StackTrace;
        Assert.Single(before.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(nameof(GlobalEventExecutor), before);
        Assert.Contains("terminationFuture", before);

        // fillInStackTrace() must be a no-op; otherwise it would repopulate the backtrace with native frames.
        // CLR has no fillInStackTrace. Throwing the same failure tests its synthetic trace override.
        Assert.Same(cause, Assert.ThrowsAny<NotSupportedException>(() => throw cause));
        Assert.Equal(before, cause.StackTrace);
    }

    private static void Sync(Task future)
    {
        // CLR: bound original unbounded waits to expose a stalled executor.
        future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    private sealed class TestRunnable
    {
        internal volatile bool ran;
        private readonly int delay;
        internal TestRunnable(int delay) => this.delay = delay;
        public void Run()
        {
            try
            {
                Thread.Sleep(delay);
                ran = true;
            }
            catch (ThreadInterruptedException)
            {
                // Ignore
            }
        }
    }
}
