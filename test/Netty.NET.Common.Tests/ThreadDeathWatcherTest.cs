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

using System;
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests;

public class ThreadDeathWatcherTest
{
    [Fact(Timeout = 10000)]
    public void testWatch()
    {
        CountdownEvent latch = new CountdownEvent(1);
        Thread t = new Thread(() =>
        {
            for (;;)
            {
                try
                {
                    Thread.Sleep(1000);
                }
                catch (ThreadInterruptedException ignore)
                {
                    break;
                }
            }
        });

        IRunnable task = AnonymousRunnable.Create(() =>
        {
            if (!t.IsAlive)
            {
                latch.Signal();
            }
        });

        try
        {
            ThreadDeathWatcher.watch(t, task);
            Assert.Fail("must reject to watch a non-alive thread.");
        }
        catch (ArgumentException e)
        {
            // expected
        }

        t.Start();
        ThreadDeathWatcher.watch(t, task);

        // As long as the thread is alive, the task should not run.
        Assert.False(latch.Wait(750));

        // Interrupt the thread to terminate it.
        t.Interrupt();

        // The task must be run on termination.
        latch.Wait();
    }

    [Fact(Timeout = 10000)]
    public void testUnwatch()
    {
        AtomicBoolean run = new AtomicBoolean();
        Thread t = new Thread(() =>
        {
            for (;;)
            {
                try
                {
                    Thread.Sleep(1000);
                }
                catch (ThreadInterruptedException ignore)
                {
                    break;
                }
            }
        });

        IRunnable task = AnonymousRunnable.Create(() =>
        {
            run.set(true);
        });

        t.Start();

        // Watch and then unwatch.
        ThreadDeathWatcher.watch(t, task);
        ThreadDeathWatcher.unwatch(t, task);

        // Interrupt the thread to terminate it.
        t.Interrupt();

        // Wait until the thread dies.
        t.Join();

        // Wait until the watcher thread terminates itself.
        Assert.True(ThreadDeathWatcher.awaitInactivity(long.MaxValue, TimeUnit.SECONDS));

        // And the task should not run.
        Assert.False(run.get());
    }

    [Fact(Timeout = 2000)]
    public void testThreadGroup()
    {
        List<Thread> group = new List<Thread>();
        AtomicReference<List<Thread>> capturedGroup = new AtomicReference<List<Thread>>();
        Thread thread = new Thread(() =>
        {
            Thread t = ThreadDeathWatcher.threadFactory.newThread(() => { });
            capturedGroup.set(t.getThreadGroup());
        });
        thread.Start();
        thread.Join();

        Assert.Equal(group, capturedGroup.get());
    }
}