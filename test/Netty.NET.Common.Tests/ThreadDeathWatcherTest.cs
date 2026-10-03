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
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests;

[CollectionDefinition("Thread death watcher globals", DisableParallelization = true)]
public class ThreadDeathWatcherGlobalsCollection { }

[Collection("Thread death watcher globals")]
public class ThreadDeathWatcherTest
{
    private static Thread CreateThread() => new(() =>
    {
        try { for (;;) Thread.Sleep(1000); }
        catch (ThreadInterruptedException) { }
    }) { IsBackground = true };

    [Fact(Timeout = 10000)]
    public void TestWatch()
    {
        using var latch = new CountdownEvent(1);
        Thread thread = CreateThread();
        IRunnable task = Runnables.Create(() => { if (!thread.IsAlive) latch.Signal(); });
        Assert.Throws<ArgumentException>(() => ThreadDeathWatcher.Watch(thread, task));
        thread.Start();
        try
        {
            ThreadDeathWatcher.Watch(thread, task);
            // As long as the thread is alive, the task should not run.
            Assert.False(latch.Wait(750));
            // Interrupt the thread to terminate it.
            thread.Interrupt();
            // The task must be run on termination.
            Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            if (thread.IsAlive) thread.Interrupt();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.True(ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromSeconds(5)));
        }
    }
    [Fact(Timeout = 10000)]
    public void TestUnwatch()
    {
        int run = 0;
        Thread thread = CreateThread();
        IRunnable task = Runnables.Create(() => Interlocked.Exchange(ref run, 1));
        thread.Start();
        try
        {
            // Watch and then unwatch.
            ThreadDeathWatcher.Watch(thread, task);
            ThreadDeathWatcher.Unwatch(thread, task);
            // Interrupt the thread to terminate it.
            thread.Interrupt();
            // Wait until the thread dies.
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            // Wait until the watcher thread terminates itself.
            Assert.True(ThreadDeathWatcher.AwaitInactivity(TimeSpan.MaxValue));
            // And the task should not run.
            Assert.Equal(0, Volatile.Read(ref run));
        }
        finally
        {
            if (thread.IsAlive) thread.Interrupt();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        }
    }
    [Fact(Timeout = 2000)]
    public void TestThreadGroup()
    {
        var group = new ThreadGroup("group");
        ThreadGroup capturedGroup = null;
        Thread thread = group.NewThread(Runnables.Create(() =>
        {
            Thread child = ThreadDeathWatcher.threadFactory.NewThread(Runnables.Empty);
            capturedGroup = ThreadGroup.GetThreadGroup(child);
        }));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(1)));
        Assert.Same(group, capturedGroup);
    }
}
