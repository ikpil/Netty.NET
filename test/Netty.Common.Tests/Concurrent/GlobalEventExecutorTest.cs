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

using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.Common.Tests.Concurrent;


public class GlobalEventExecutorTest {

    private static readonly GlobalEventExecutor e = GlobalEventExecutor.INSTANCE;

    public GlobalEventExecutorTest() {
        // Wait until the global executor is stopped (just in case there is a task running due to previous test cases)
        for (;;) {
            if (e._thread == null || !e._thread.isAlive()) {
                break;
            }

            Thread.Sleep(50);
        }
    }

    [Fact(Timeout = 5000)]
    public void testAutomaticStartStop() {
        final TestRunnable task = new TestRunnable(500);
        e.execute(task);

        // Ensure the new thread has started.
        Thread thread = e.thread;
        Assert.NotNull(thread);
        Assert.True(thread.isAlive());

        thread.join();
        Assert.True(task.ran.get());

        // Ensure another new thread starts again.
        task.ran.set(false);
        e.execute(task);
        Assert.NotSame(e.thread, thread);
        thread = e.thread;

        thread.join();

        Assert.True(task.ran.get());
    }

    [Fact(Timeout = 5000)]
    public void testScheduledTasks() {
        TestRunnable task = new TestRunnable(0);
        ScheduledFuture<?> f = e.schedule(task, 1500, TimeUnit.MILLISECONDS);
        f.sync();
        Assert.True(task.ran.get());

        // Ensure the thread is still running.
        Thread thread = e.thread;
        Assert.NotNull(thread);
        Assert.True(thread.isAlive());

        thread.join();
    }

    // ensure that when a task submission causes a new thread to be created, the thread inherits the thread group of the
    // submitting thread
    [Fact]
    @Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
    public void testThreadGroup() {
        final ThreadGroup group = new ThreadGroup("group");
        final AtomicReference<ThreadGroup> capturedGroup = new AtomicReference<ThreadGroup>();
        final Thread thread = new Thread(group, new IRunnable() {
            @Override
            public void run() {
                final Thread t = e.threadFactory.newThread(new IRunnable() {
                    @Override
                    public void run() {
                    }
                });
                capturedGroup.set(t.getThreadGroup());
            }
        });
        thread.start();
        thread.join();

        Assert.Equal(group, capturedGroup.get());
    }

    [Fact]
    @Timeout(value = 5000, unit = TimeUnit.MILLISECONDS)
    public void testTakeTask() {
        //add task
        TestRunnable beforeTask = new TestRunnable(0);
        e.execute(beforeTask);

        //add scheduled task
        TestRunnable scheduledTask = new TestRunnable(0);
        ScheduledFuture<?> f = e.schedule(scheduledTask , 1500, TimeUnit.MILLISECONDS);

        //add task
        TestRunnable afterTask = new TestRunnable(0);
        e.execute(afterTask);

        f.sync();

        Assert.True(beforeTask.ran.get());
        Assert.True(scheduledTask.ran.get());
        Assert.True(afterTask.ran.get());
    }

    [Fact]
    @Timeout(value = 5000, unit = TimeUnit.MILLISECONDS)
    public void testTakeTaskAlwaysHasTask() {
        //for https://github.com/netty/netty/issues/1614
        //add scheduled task
        TestRunnable t = new TestRunnable(0);
        final ScheduledFuture<?> f = e.schedule(t, 1500, TimeUnit.MILLISECONDS);

        //ensure always has at least one task in taskQueue
        //check if scheduled tasks are triggered
        e.execute(new IRunnable() {
            @Override
            public void run() {
                if (!f.isDone()) {
                    e.execute(this);
                }
            }
        });

        f.sync();

        Assert.True(t.ran.get());
    }

    private static final class TestRunnable implements IRunnable {
        final AtomicBoolean ran = new AtomicBoolean();
        final long delay;

        TestRunnable(long delay) {
            this.delay = delay;
        }

        @Override
        public void run() {
            try {
                Thread.sleep(delay);
                ran.set(true);
            } catch (ThreadInterruptedException ignored) {
                // Ignore
            }
        }
    }
}
