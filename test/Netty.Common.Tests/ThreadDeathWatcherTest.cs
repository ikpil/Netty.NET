/*
 * Copyright 2014 The Netty Project
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


public class ThreadDeathWatcherTest {

    [Fact]
    @Timeout(value = 10000, unit = TimeUnit.MILLISECONDS)
    public void testWatch() {
        final CountdownEvent latch = new CountdownEvent(1);
        final Thread t = new Thread() {
            @Override
            public void run() {
                for (;;) {
                    try {
                        Thread.sleep(1000);
                    } catch (ThreadInterruptedException ignore) {
                        break;
                    }
                }
            }
        };

        final IRunnable task = new IRunnable() {
            @Override
            public void run() {
                if (!t.isAlive()) {
                    latch.countDown();
                }
            }
        };

        try {
            ThreadDeathWatcher.watch(t, task);
            Assert.Fail("must reject to watch a non-alive thread.");
        } catch (ArgumentException e) {
            // expected
        }

        t.start();
        ThreadDeathWatcher.watch(t, task);

        // As long as the thread is alive, the task should not run.
        Assert.False(latch.await(750, TimeUnit.MILLISECONDS));

        // Interrupt the thread to terminate it.
        t.interrupt();

        // The task must be run on termination.
        latch.await();
    }

    [Fact]
    @Timeout(value = 10000, unit = TimeUnit.MILLISECONDS)
    public void testUnwatch() {
        final AtomicBoolean run = new AtomicBoolean();
        final Thread t = new Thread() {
            @Override
            public void run() {
                for (;;) {
                    try {
                        Thread.sleep(1000);
                    } catch (ThreadInterruptedException ignore) {
                        break;
                    }
                }
            }
        };

        final IRunnable task = new IRunnable() {
            @Override
            public void run() {
                run.set(true);
            }
        };

        t.start();

        // Watch and then unwatch.
        ThreadDeathWatcher.watch(t, task);
        ThreadDeathWatcher.unwatch(t, task);

        // Interrupt the thread to terminate it.
        t.interrupt();

        // Wait until the thread dies.
        t.join();

        // Wait until the watcher thread terminates itself.
        Assert.True(ThreadDeathWatcher.awaitInactivity(long.MaxValue, TimeUnit.SECONDS));

        // And the task should not run.
        Assert.False(run.get());
    }

    [Fact]
    @Timeout(value = 2000, unit = TimeUnit.MILLISECONDS)
    public void testThreadGroup() {
        final ThreadGroup group = new ThreadGroup("group");
        final AtomicReference<ThreadGroup> capturedGroup = new AtomicReference<ThreadGroup>();
        final Thread thread = new Thread(group, new IRunnable() {
            @Override
            public void run() {
                final Thread t = ThreadDeathWatcher.threadFactory.newThread(new IRunnable() {
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
}
