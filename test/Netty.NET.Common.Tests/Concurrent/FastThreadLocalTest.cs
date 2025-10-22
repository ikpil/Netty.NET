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

namespace Netty.NET.Common.Tests.Concurrent;

public class FastThreadLocalTest {
    @BeforeEach
    public void setUp() {
        FastThreadLocal.removeAll();
        Assert.Equal(0, FastThreadLocal.size());
    }

    [Fact]
    public void testGetAndSetReturnsOldValue() {
        FastThreadLocal<Boolean> threadLocal = new FastThreadLocal<Boolean>() {
            @Override
            protected Boolean initialValue() {
            return Boolean.TRUE;
        }
        };

        Assert.Null(threadLocal.getAndSet(Boolean.FALSE));
        Assert.Equal(Boolean.FALSE, threadLocal.get());
        Assert.Equal(Boolean.FALSE, threadLocal.getAndSet(Boolean.TRUE));
        Assert.Equal(Boolean.TRUE, threadLocal.get());
        threadLocal.remove();
    }

    [Fact]
    public void testGetIfExists() {
        FastThreadLocal<Boolean> threadLocal = new FastThreadLocal<Boolean>() {
            @Override
            protected Boolean initialValue() {
            return Boolean.TRUE;
        }
        };

        Assert.Null(threadLocal.getIfExists());
        Assert.True(threadLocal.get());
        Assert.True(threadLocal.getIfExists());

        FastThreadLocal.removeAll();
        Assert.Null(threadLocal.getIfExists());
    }

    [Fact]
        @Timeout(value = 10000, unit = TimeUnit.MILLISECONDS)
    public void testRemoveAll() {
        final AtomicBoolean removed = new AtomicBoolean();
        final FastThreadLocal<Boolean> var = new FastThreadLocal<Boolean>() {
        @Override
        protected void onRemoval(Boolean value) {
        removed.set(true);
    }
};

// Initialize a thread-local variable.
Assert.Null(var.get());
Assert.Equal(1, FastThreadLocal.size());

// And then remove it.
FastThreadLocal.removeAll();
Assert.True(removed.get());
Assert.Equal(0, FastThreadLocal.size());

}

[Fact]
@Timeout(value = 10000, unit = TimeUnit.MILLISECONDS)
public void testRemoveAllFromFTLThread() throws Exception {
    final AtomicReference<Exception> throwable = new AtomicReference<Exception>();
    final Thread thread = new FastThreadLocalThread() {
        @Override
        public void run() {
        try {
        testRemoveAll();
    } catch (Exception t) {
        throwable.set(t);
    }
    }
    };

    thread.start();
    thread.join();

    Exception t = throwable.get();
    if (t != null) {
        throw t;
    }
}

[Fact]
public void testMultipleSetRemove() {
    final FastThreadLocal<string> threadLocal = new FastThreadLocal<string>();
    final IRunnable runnable = new IRunnable() {
        @Override
        public void run() {
        threadLocal.set("1");
        threadLocal.remove();
        threadLocal.set("2");
        threadLocal.remove();
    }
    };

    final int sizeWhenStart = ObjectCleaner.getLiveSetCount();
    Thread thread = new Thread(runnable);
    thread.start();
    thread.join();

    Assert.Equal(0, ObjectCleaner.getLiveSetCount() - sizeWhenStart);

    Thread thread2 = new Thread(runnable);
    thread2.start();
    thread2.join();

    Assert.Equal(0, ObjectCleaner.getLiveSetCount() - sizeWhenStart);
}

[Fact]
public void testMultipleSetRemove_multipleThreadLocal() {
    final FastThreadLocal<string> threadLocal = new FastThreadLocal<string>();
    final FastThreadLocal<string> threadLocal2 = new FastThreadLocal<string>();
    final IRunnable runnable = new IRunnable() {
        @Override
        public void run() {
        threadLocal.set("1");
        threadLocal.remove();
        threadLocal.set("2");
        threadLocal.remove();
        threadLocal2.set("1");
        threadLocal2.remove();
        threadLocal2.set("2");
        threadLocal2.remove();
    }
    };

    final int sizeWhenStart = ObjectCleaner.getLiveSetCount();
    Thread thread = new Thread(runnable);
    thread.start();
    thread.join();

    Assert.Equal(0, ObjectCleaner.getLiveSetCount() - sizeWhenStart);

    Thread thread2 = new Thread(runnable);
    thread2.start();
    thread2.join();

    Assert.Equal(0, ObjectCleaner.getLiveSetCount() - sizeWhenStart);
}

[Fact]
public void testWrappedProperties() {
    Assert.False(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
    Assert.False(FastThreadLocalThread.currentThreadHasFastThreadLocal());
    FastThreadLocalThread.runWithFastThreadLocal(()() => {
        Assert.True(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
        Assert.True(FastThreadLocalThread.currentThreadHasFastThreadLocal());
    });
}

[Fact]
public void testWrapMany() throws AggregateException, ThreadInterruptedException {
class Worker implements IRunnable {
    final Semaphore semaphore = new Semaphore(0);
    final FutureTask<?> task = new FutureTask<>(this, null);

    @Override
    public void run() {
    Assert.False(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
    Assert.False(FastThreadLocalThread.currentThreadHasFastThreadLocal());
    semaphore.acquireUninterruptibly();
    FastThreadLocalThread.runWithFastThreadLocal(()() => {
        Assert.True(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
        Assert.True(FastThreadLocalThread.currentThreadHasFastThreadLocal());
        semaphore.acquireUninterruptibly();
        Assert.True(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
        Assert.True(FastThreadLocalThread.currentThreadHasFastThreadLocal());
    });
    Assert.False(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
    Assert.False(FastThreadLocalThread.currentThreadHasFastThreadLocal());
}
}

int n = 100;
List<Worker> workers = new List<>();
for (int i = 0; i < n; i++) {
    Worker worker = new Worker();
    workers.add(worker);
}
Collections.shuffle(workers);
for (int i = 0; i < workers.size(); i++) {
    new Thread(workers.get(i).task, "worker-" + i).start();
}
for (int i = 0; i < 2; i++) {
    Collections.shuffle(workers);
    for (Worker worker : workers) {
        worker.semaphore.release();
    }
}
for (Worker worker : workers) {
    worker.task.get();
}
}

[Fact]
@Timeout(value = 4000, unit = TimeUnit.MILLISECONDS)
public void testOnRemoveCalledForFastThreadLocalGet() {
    testOnRemoveCalled(true, false, true);
}

@Disabled("onRemoval(...) not called with non FastThreadLocal")
[Fact]
@Timeout(value = 4000, unit = TimeUnit.MILLISECONDS)
public void testOnRemoveCalledForNonFastThreadLocalGet() {
    testOnRemoveCalled(false, false, true);
}

[Fact]
@Timeout(value = 4000, unit = TimeUnit.MILLISECONDS)
public void testOnRemoveCalledForFastThreadLocalSet() {
    testOnRemoveCalled(true, false, false);
}

@Disabled("onRemoval(...) not called with non FastThreadLocal")
[Fact]
@Timeout(value = 4000, unit = TimeUnit.MILLISECONDS)
public void testOnRemoveCalledForNonFastThreadLocalSet() {
    testOnRemoveCalled(false, false, false);
}

[Fact]
@Timeout(value = 4000, unit = TimeUnit.MILLISECONDS)
public void testOnRemoveCalledForWrappedGet() {
    testOnRemoveCalled(false, true, true);
}

[Fact]
@Timeout(value = 4000, unit = TimeUnit.MILLISECONDS)
public void testOnRemoveCalledForWrappedSet() {
    testOnRemoveCalled(false, true, false);
}

private static void testOnRemoveCalled(bool fastThreadLocal, bool wrap, final bool callGet)
throws Exception {

    final TestFastThreadLocal threadLocal = new TestFastThreadLocal();
    final TestFastThreadLocal threadLocal2 = new TestFastThreadLocal();

    IRunnable runnable = new IRunnable() {
        @Override
        public void run() {
        if (callGet) {
        Assert.Equal(Thread.CurrentThread.getName(), threadLocal.get());
        Assert.Equal(Thread.CurrentThread.getName(), threadLocal2.get());
    } else {
        threadLocal.set(Thread.CurrentThread.getName());
        threadLocal2.set(Thread.CurrentThread.getName());
    }
    }
    };
    if (wrap) {
        IRunnable r = runnable;
        runnable = () => FastThreadLocalThread.runWithFastThreadLocal(r);
    }
    Thread thread = fastThreadLocal ? new FastThreadLocalThread(runnable) : new Thread(runnable);
    thread.start();
    thread.join();

    string threadName = thread.getName();

    // Null this out so it can be collected
    thread = null;

    // Loop until onRemoval(...) was called. This will fail the test if this not works due a timeout.
    while (threadLocal.onRemovalCalled.get() == null || threadLocal2.onRemovalCalled.get() == null) {
        System.gc();
        System.runFinalization();
        Thread.sleep(50);
    }

    Assert.Equal(threadName, threadLocal.onRemovalCalled.get());
    Assert.Equal(threadName, threadLocal2.onRemovalCalled.get());
}

private static final class TestFastThreadLocal extends FastThreadLocal<string> {

    final AtomicReference<string> onRemovalCalled = new AtomicReference<string>();

    @Override
    protected string initialValue() {
        return Thread.CurrentThread.getName();
    }

    @Override
    protected void onRemoval(string value) {
        onRemovalCalled.set(value);
    }
}

[Fact]
public void testConstructionWithIndex() {
    int ARRAY_LIST_CAPACITY_MAX_SIZE = int.MaxValue - 8;
    Field nextIndexField =
        InternalThreadLocalMap.class.getDeclaredField("nextIndex");
    nextIndexField.setAccessible(true);
    AtomicInteger nextIndex = (AtomicInteger) nextIndexField.get(AtomicInteger.class);
    int nextIndex_before = nextIndex.get();
    final AtomicReference<Exception> throwable = new AtomicReference<Exception>();
    try {
        while (nextIndex.get() < ARRAY_LIST_CAPACITY_MAX_SIZE) {
            new FastThreadLocal<Boolean>();
        }
        Assert.Equal(ARRAY_LIST_CAPACITY_MAX_SIZE - 1, InternalThreadLocalMap.lastVariableIndex());
        try {
            new FastThreadLocal<Boolean>();
        } catch (Exception t) {
            throwable.set(t);
        } finally {
            // Assert the max index cannot greater than (ARRAY_LIST_CAPACITY_MAX_SIZE - 1).
            assertInstanceOf(IllegalStateException.class, throwable.get());
            // Assert the index was reset to ARRAY_LIST_CAPACITY_MAX_SIZE
            // after it reaches ARRAY_LIST_CAPACITY_MAX_SIZE.
            Assert.Equal(ARRAY_LIST_CAPACITY_MAX_SIZE - 1, InternalThreadLocalMap.lastVariableIndex());
        }
    } finally {
        // Restore the index.
        nextIndex.set(nextIndex_before);
    }
}

@EnabledIfEnvironmentVariable(named = "CI", matches = "true", disabledReason = "" +
                                                                               "This deliberately causes OutOfMemoryErrors, for which heap dumps are automatically generated. " +
                                                                               "To avoid confusion, wasted time investigating heap dumps, and to avoid heap dumps accidentally " +
                                                                               "getting committed to the Git repository, we should only enable this test when running in a CI " +
                                                                               "environment. We make this check by assuming a 'CI' environment variable. " +
                                                                               "This matches what Github Actions is doing for us currently.")
[Fact]
public void testInternalThreadLocalMapExpand() {
    final AtomicReference<Exception> throwable = new AtomicReference<Exception>();
    IRunnable runnable = new IRunnable() {
        @Override
        public void run() {
        int expand_threshold = 1 << 30;
        try {
        InternalThreadLocalMap.get().setIndexedVariable(expand_threshold, null);
    } catch (Exception t) {
        throwable.set(t);
    }
    }
    };
    FastThreadLocalThread fastThreadLocalThread = new FastThreadLocalThread(runnable);
    fastThreadLocalThread.start();
    fastThreadLocalThread.join();
    // assert the expanded size is not overflowed to negative value
    assertThat(throwable.get()).isNotInstanceOf(NegativeArraySizeException.class);
}

[Fact]
public void testFastThreadLocalSize() {
    int originSize = FastThreadLocal.size();
    Assert.True(originSize >= 0);

    InternalThreadLocalMap.get();
    Assert.Equal(originSize, FastThreadLocal.size());

    new FastThreadLocal<Boolean>();
    Assert.Equal(originSize, FastThreadLocal.size());

    FastThreadLocal<Boolean> fst2 = new FastThreadLocal<Boolean>();
    fst2.get();
    Assert.Equal(1 + originSize, FastThreadLocal.size());

    FastThreadLocal<Boolean> fst3 = new FastThreadLocal<Boolean>();
    fst3.set(null);
    Assert.Equal(2 + originSize, FastThreadLocal.size());

    FastThreadLocal<Boolean> fst4 = new FastThreadLocal<Boolean>();
    fst4.set(Boolean.TRUE);
    Assert.Equal(3 + originSize, FastThreadLocal.size());

    fst4.set(Boolean.TRUE);
    Assert.Equal(3 + originSize, FastThreadLocal.size());

    fst4.remove();
    Assert.Equal(2 + originSize, FastThreadLocal.size());

    FastThreadLocal.removeAll();
    Assert.Equal(0, FastThreadLocal.size());
}

[Fact]
public void testFastThreadLocalInitialValueWithUnset() {
    final AtomicReference<Exception> throwable = new AtomicReference<Exception>();
    final FastThreadLocal fst = new FastThreadLocal() {
        @Override
        protected Object initialValue() {
        return InternalThreadLocalMap.UNSET;
    }
    };
    IRunnable runnable = new IRunnable() {
        @Override
        public void run() {
        try {
        fst.get();
    } catch (Exception t) {
        throwable.set(t);
    }
    }
    };
    FastThreadLocalThread fastThreadLocalThread = new FastThreadLocalThread(runnable);
    fastThreadLocalThread.start();
    fastThreadLocalThread.join();
    assertInstanceOf(ArgumentException.class, throwable.get());
}
}