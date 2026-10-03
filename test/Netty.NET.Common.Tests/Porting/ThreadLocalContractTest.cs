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
    public ThreadLocalContractTest()
    {
        // xUnit may reuse a worker that previously ran other Netty consumers.
        // These tests assert absolute counts on an explicitly empty map.
        FastThreadLocal.RemoveAll();
        Assert.Equal(0, FastThreadLocal.Size());
    }

    public void Dispose() => FastThreadLocal.RemoveAll();

    [Fact]
    public void ScopeRejectsReentryAndCleansValuesWhenTheTaskThrows()
    {
        var local = new TrackingLocal();
        var cause = new InvalidOperationException("task failed");
        Assert.Same(cause, Assert.Throws<InvalidOperationException>(() =>
            FastThreadLocalThread.RunWithFastThreadLocal(() =>
            {
                local.Set("value");
                Assert.Throws<InvalidOperationException>(() => FastThreadLocalThread.RunWithFastThreadLocal(() => { }));
                throw cause;
            })));
        Assert.False(FastThreadLocalThread.CurrentThreadHasFastThreadLocal());
        Assert.False(local.IsSet());
        Assert.Equal("value", local.Removed);
        Assert.Null(InternalThreadLocalMap.GetIfSet());
    }

    [Fact]
    public void IndexedStorageGrowsWithoutLosingExistingValues()
    {
        var locals = new List<FastThreadLocal<string>>();
        for (int i = 0; i < 100; i++)
        {
            var local = new FastThreadLocal<string>();
            local.Set(i.ToString());
            locals.Add(local);
        }
        Assert.Equal(100, FastThreadLocal.Size());
        for (int i = 0; i < locals.Count; i++) Assert.Equal(i.ToString(), locals[i].Get());
        FastThreadLocal.RemoveAll();
        Assert.Equal(0, FastThreadLocal.Size());
    }

    [Fact]
    public void FactoryThreadsAdvertiseAndPerformCleanup()
    {
        var local = new TrackingLocal();
        Exception failure = null;
        var factory = new DefaultThreadFactory(typeof(ThreadLocalContractTest), true);
        var thread = factory.NewThread(Runnables.Create(() =>
        {
            try
            {
                Assert.True(FastThreadLocalThread.CurrentThreadHasFastThreadLocal());
                Assert.True(FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals());
                Assert.Throws<InvalidOperationException>(() => FastThreadLocalThread.RunWithFastThreadLocal(() => { }));
                local.Set(Thread.CurrentThread.Name);
            }
            catch (Exception cause) { failure = cause; }
        }));
        Assert.True(FastThreadLocalThread.WillCleanupFastThreadLocals(thread));
        Assert.True(thread.IsBackground);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.Equal(thread.Name, local.Removed);
    }

    [Fact]
    public void CachedCollectionsHandleDifferentClrElementTypesAndReleaseOldEntries()
    {
        var map = InternalThreadLocalMap.Get();
        var strings = map.ArrayList<string>();
        strings.Add("entry");
        var objects = map.ArrayList<object>();
        Assert.Empty(objects);
        Assert.Empty(strings);
        Assert.Same(objects, map.ArrayList<object>());
        var builder = map.StringBuilder();
        builder.Append('x', 10000);
        Assert.Same(builder, map.StringBuilder());
        Assert.Empty(builder.ToString());
        Assert.Equal(1024, builder.Capacity);
    }

    private sealed class TrackingLocal : FastThreadLocal<string>
    {
        public string Removed;
        protected override void OnRemoval(string value) => Removed = value;
    }

    [Fact]
    public void RemovalRegistryUsesIdentityEvenWhenVariablesCompareEqual()
    {
        var first = new EqualLocal();
        var second = new EqualLocal();
        first.Set("first");
        second.Set("second");
        Assert.Equal(2, FastThreadLocal.Size());
        FastThreadLocal.RemoveAll();
        Assert.Equal("first", first.Removed);
        Assert.Equal("second", second.Removed);
    }

    private sealed class EqualLocal : FastThreadLocal<string>
    {
        public string Removed;
        public override bool Equals(object other) => other is EqualLocal;
        public override int GetHashCode() => 1;
        protected override void OnRemoval(string value) => Removed = value;
    }
}
