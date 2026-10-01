using System;
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Thread-local globals")]
public class ThreadLocalContractTest : IDisposable
{
    public void Dispose() => FastThreadLocal.removeAll();

    [Fact]
    public void ScopeRejectsReentryAndCleansValuesWhenTheTaskThrows()
    {
        var local = new TrackingLocal();
        var cause = new InvalidOperationException("task failed");
        Assert.Same(cause, Assert.Throws<InvalidOperationException>(() =>
            FastThreadLocalThread.runWithFastThreadLocal(() =>
            {
                local.set("value");
                Assert.Throws<InvalidOperationException>(() => FastThreadLocalThread.runWithFastThreadLocal(() => { }));
                throw cause;
            })));
        Assert.False(FastThreadLocalThread.currentThreadHasFastThreadLocal());
        Assert.False(local.isSet());
        Assert.Equal("value", local.Removed);
        Assert.Null(InternalThreadLocalMap.getIfSet());
    }

    [Fact]
    public void IndexedStorageGrowsWithoutLosingExistingValues()
    {
        var locals = new List<FastThreadLocal<string>>();
        for (int i = 0; i < 100; i++)
        {
            var local = new FastThreadLocal<string>();
            local.set(i.ToString());
            locals.Add(local);
        }
        Assert.Equal(100, FastThreadLocal.size());
        for (int i = 0; i < locals.Count; i++) Assert.Equal(i.ToString(), locals[i].get());
        FastThreadLocal.removeAll();
        Assert.Equal(0, FastThreadLocal.size());
    }

    [Fact]
    public void FactoryThreadsAdvertiseAndPerformCleanup()
    {
        var local = new TrackingLocal();
        Exception failure = null;
        var factory = new DefaultThreadFactory(typeof(ThreadLocalContractTest), true);
        var thread = factory.newThread(Runnables.Create(() =>
        {
            try
            {
                Assert.True(FastThreadLocalThread.currentThreadHasFastThreadLocal());
                Assert.True(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
                Assert.Throws<InvalidOperationException>(() => FastThreadLocalThread.runWithFastThreadLocal(() => { }));
                local.set(Thread.CurrentThread.Name);
            }
            catch (Exception cause) { failure = cause; }
        }));
        Assert.True(FastThreadLocalThread.willCleanupFastThreadLocals(thread));
        Assert.True(thread.IsBackground);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.Equal(thread.Name, local.Removed);
    }

    [Fact]
    public void CachedCollectionsHandleDifferentClrElementTypesAndReleaseOldEntries()
    {
        var map = InternalThreadLocalMap.get();
        var strings = map.arrayList<string>();
        strings.Add("entry");
        var objects = map.arrayList<object>();
        Assert.Empty(objects);
        Assert.Empty(strings);
        Assert.Same(objects, map.arrayList<object>());
        var builder = map.stringBuilder();
        builder.Append('x', 10000);
        Assert.Same(builder, map.stringBuilder());
        Assert.Empty(builder.ToString());
        Assert.Equal(1024, builder.Capacity);
    }

    private sealed class TrackingLocal : FastThreadLocal<string>
    {
        public string Removed;
        protected override void onRemoval(string value) => Removed = value;
    }

    [Fact]
    public void RemovalRegistryUsesIdentityEvenWhenVariablesCompareEqual()
    {
        var first = new EqualLocal();
        var second = new EqualLocal();
        first.set("first");
        second.set("second");
        Assert.Equal(2, FastThreadLocal.size());
        FastThreadLocal.removeAll();
        Assert.Equal("first", first.Removed);
        Assert.Equal("second", second.Removed);
    }

    private sealed class EqualLocal : FastThreadLocal<string>
    {
        public string Removed;
        public override bool Equals(object other) => other is EqualLocal;
        public override int GetHashCode() => 1;
        protected override void onRemoval(string value) => Removed = value;
    }
}
