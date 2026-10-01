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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Concurrent;

[Collection("Thread-local globals")]
public class FastThreadLocalTest : IDisposable
{
    public FastThreadLocalTest()
    {
        FastThreadLocal.removeAll();
        Assert.Equal(0, FastThreadLocal.size());
    }
    public void Dispose() => FastThreadLocal.removeAll();

    private sealed class BooleanLocal : FastThreadLocal<object>
    {
        protected override object initialValue() => true;
    }

    [Fact]
    public void testGetAndSetReturnsOldValue()
    {
        var threadLocal = new BooleanLocal();
        Assert.Null(threadLocal.getAndSet(false));
        Assert.Equal(false, threadLocal.get());
        Assert.Equal(false, threadLocal.getAndSet(true));
        Assert.Equal(true, threadLocal.get());
        threadLocal.remove();
    }

    [Fact]
    public void testGetIfExists()
    {
        var threadLocal = new BooleanLocal();
        Assert.Null(threadLocal.getIfExists());
        Assert.Equal(true, threadLocal.get());
        Assert.Equal(true, threadLocal.getIfExists());
        FastThreadLocal.removeAll();
        Assert.Null(threadLocal.getIfExists());
    }

    private sealed class RemovalLocal : FastThreadLocal<object>
    {
        public bool Removed;
        protected override void onRemoval(object value) => Removed = true;
    }

    [Fact(Timeout = 10000)]
    public void testRemoveAll()
    {
        var variable = new RemovalLocal();
        // Initialize a thread-local variable.
        Assert.Null(variable.get());
        Assert.Equal(1, FastThreadLocal.size());
        // And then remove it.
        FastThreadLocal.removeAll();
        Assert.True(variable.Removed);
        Assert.Equal(0, FastThreadLocal.size());
    }

    [Fact(Timeout = 10000)]
    public void testRemoveAllFromFTLThread() => RunThread(testRemoveAll, true);

    [Fact]
    public void testWrappedProperties()
    {
        Assert.False(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
        Assert.False(FastThreadLocalThread.currentThreadHasFastThreadLocal());
        FastThreadLocalThread.runWithFastThreadLocal(() =>
        {
            Assert.True(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
            Assert.True(FastThreadLocalThread.currentThreadHasFastThreadLocal());
        });
    }

    private sealed class Worker
    {
        public readonly SemaphoreSlim Semaphore = new(0);
        public readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Run()
        {
            try
            {
                Assert.False(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
                Assert.False(FastThreadLocalThread.currentThreadHasFastThreadLocal());
                Assert.True(Semaphore.Wait(TimeSpan.FromSeconds(10)));
                FastThreadLocalThread.runWithFastThreadLocal(() =>
                {
                    Assert.True(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
                    Assert.True(FastThreadLocalThread.currentThreadHasFastThreadLocal());
                    Assert.True(Semaphore.Wait(TimeSpan.FromSeconds(10)));
                    Assert.True(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
                    Assert.True(FastThreadLocalThread.currentThreadHasFastThreadLocal());
                });
                Assert.False(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
                Assert.False(FastThreadLocalThread.currentThreadHasFastThreadLocal());
                Completion.SetResult();
            }
            catch (Exception cause) { Completion.TrySetException(cause); }
        }
    }

    [Fact]
    public async Task testWrapMany()
    {
        int n = 100;
        var workers = Enumerable.Range(0, n).Select(_ => new Worker()).OrderBy(_ => Random.Shared.Next()).ToList();
        var threads = workers.Select((worker, i) => new Thread(worker.Run) { IsBackground = true, Name = "worker-" + i }).ToList();
        try
        {
            foreach (Thread thread in threads) thread.Start();
            for (int i = 0; i < 2; i++)
                foreach (Worker worker in workers.OrderBy(_ => Random.Shared.Next())) worker.Semaphore.Release();
            await Task.WhenAll(workers.Select(worker => worker.Completion.Task)).WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            foreach (Thread thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
            foreach (Worker worker in workers) worker.Semaphore.Dispose();
        }
    }

    [Fact(Timeout = 4000)]
    public void testOnRemoveCalledForFastThreadLocalGet() => testOnRemoveCalled(true, false, true);
    [Fact(Skip = "onRemoval(...) not called with non FastThreadLocal")]
    public void testOnRemoveCalledForNonFastThreadLocalGet() => testOnRemoveCalled(false, false, true);
    [Fact(Timeout = 4000)]
    public void testOnRemoveCalledForFastThreadLocalSet() => testOnRemoveCalled(true, false, false);
    [Fact(Skip = "onRemoval(...) not called with non FastThreadLocal")]
    public void testOnRemoveCalledForNonFastThreadLocalSet() => testOnRemoveCalled(false, false, false);
    [Fact(Timeout = 4000)]
    public void testOnRemoveCalledForWrappedGet() => testOnRemoveCalled(false, true, true);
    [Fact(Timeout = 4000)]
    public void testOnRemoveCalledForWrappedSet() => testOnRemoveCalled(false, true, false);

    private static void testOnRemoveCalled(bool fastThreadLocal, bool wrap, bool callGet)
    {
        var threadLocal = new TestFastThreadLocal();
        var threadLocal2 = new TestFastThreadLocal();
        Action runnable = () =>
        {
            if (callGet)
            {
                Assert.Equal(Thread.CurrentThread.Name, threadLocal.get());
                Assert.Equal(Thread.CurrentThread.Name, threadLocal2.get());
            }
            else
            {
                threadLocal.set(Thread.CurrentThread.Name);
                threadLocal2.set(Thread.CurrentThread.Name);
            }
        };
        if (wrap)
        {
            Action original = runnable;
            runnable = () => FastThreadLocalThread.runWithFastThreadLocal(original);
        }
        Thread thread = RunThread(runnable, fastThreadLocal);
        string threadName = thread.Name;
        // Null this out so it can be collected
        thread = null;
        // Loop until onRemoval(...) was called. This will fail the test if this not works due a timeout.
        while (threadLocal.OnRemovalCalled == null || threadLocal2.OnRemovalCalled == null)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(50);
        }
        Assert.Equal(threadName, threadLocal.OnRemovalCalled);
        Assert.Equal(threadName, threadLocal2.OnRemovalCalled);
    }

    private sealed class TestFastThreadLocal : FastThreadLocal<string>
    {
        public volatile string OnRemovalCalled;
        protected override string initialValue() => Thread.CurrentThread.Name;
        protected override void onRemoval(string value) => OnRemovalCalled = value;
    }

    [Fact]
    public void testConstructionWithIndex()
    {
        int ARRAY_LIST_CAPACITY_MAX_SIZE = int.MaxValue - 8;
        var field = typeof(InternalThreadLocalMap).GetField("nextIndex", BindingFlags.Static | BindingFlags.NonPublic);
        var nextIndex = (AtomicInteger)field.GetValue(null);
        int previous = nextIndex.get();
        try
        {
            // CLR test adaptation: jump to the boundary rather than allocating over two billion objects.
            nextIndex.set(ARRAY_LIST_CAPACITY_MAX_SIZE - 2);
            while (nextIndex.get() < ARRAY_LIST_CAPACITY_MAX_SIZE) new FastThreadLocal<object>();
            Assert.Equal(ARRAY_LIST_CAPACITY_MAX_SIZE - 1, InternalThreadLocalMap.lastVariableIndex());
            // Assert the max index cannot greater than (ARRAY_LIST_CAPACITY_MAX_SIZE - 1).
            Assert.Throws<InvalidOperationException>(() => new FastThreadLocal<object>());
            // Assert the index was reset to ARRAY_LIST_CAPACITY_MAX_SIZE
            // after it reaches ARRAY_LIST_CAPACITY_MAX_SIZE.
            Assert.Equal(ARRAY_LIST_CAPACITY_MAX_SIZE - 1, InternalThreadLocalMap.lastVariableIndex());
        }
        finally
        {
            // Restore the index.
            nextIndex.set(previous);
        }
    }

    [CiOnlyFact]
    public void testInternalThreadLocalMapExpand()
    {
        Exception throwable = null;
        RunThread(() =>
        {
            int expand_threshold = 1 << 30;
            try { InternalThreadLocalMap.get().setIndexedVariable(expand_threshold, null); }
            catch (Exception cause) { throwable = cause; }
        }, true);
        // assert the expanded size is not overflowed to negative value
        Assert.False(throwable is OverflowException);
    }

    [Fact]
    public void testFastThreadLocalSize()
    {
        int originSize = FastThreadLocal.size();
        Assert.True(originSize >= 0);
        InternalThreadLocalMap.get();
        Assert.Equal(originSize, FastThreadLocal.size());
        new FastThreadLocal<object>();
        Assert.Equal(originSize, FastThreadLocal.size());
        var fst2 = new FastThreadLocal<object>();
        fst2.get();
        Assert.Equal(1 + originSize, FastThreadLocal.size());
        var fst3 = new FastThreadLocal<object>();
        fst3.set(null);
        Assert.Equal(2 + originSize, FastThreadLocal.size());
        var fst4 = new FastThreadLocal<object>();
        fst4.set(true);
        Assert.Equal(3 + originSize, FastThreadLocal.size());
        fst4.set(true);
        Assert.Equal(3 + originSize, FastThreadLocal.size());
        fst4.remove();
        Assert.Equal(2 + originSize, FastThreadLocal.size());
        FastThreadLocal.removeAll();
        Assert.Equal(0, FastThreadLocal.size());
    }

    private sealed class UnsetLocal : FastThreadLocal<object>
    {
        protected override object initialValue() => InternalThreadLocalMap.UNSET;
    }
    [Fact]
    public void testFastThreadLocalInitialValueWithUnset()
    {
        Exception throwable = null;
        var fst = new UnsetLocal();
        RunThread(() =>
        {
            try { fst.get(); }
            catch (Exception cause) { throwable = cause; }
        }, true);
        Assert.IsType<ArgumentException>(throwable);
    }

    private static Thread RunThread(Action runnable, bool fast)
    {
        Exception failure = null;
        IRunnable target = Runnables.Create(() =>
        {
            try { runnable(); }
            catch (Exception cause) { failure = cause; }
        });
        Thread thread = fast ? new FastThreadLocalThread(target).Thread : new Thread(target.run);
        thread.IsBackground = true;
        thread.Name = "test-local-" + Guid.NewGuid();
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Thread did not terminate");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return thread;
    }
}

[CollectionDefinition("Thread-local globals", DisableParallelization = true)]
public sealed class ThreadLocalGlobalsCollection;

public sealed class CiOnlyFactAttribute : FactAttribute
{
    public CiOnlyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CI") != "true")
            Skip = "Upstream CI-only test: deliberately allocates an oversized thread-local table.";
    }
}
