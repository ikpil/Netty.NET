using System;
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Concurrent;
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
        var thread = factory.NewThread(() =>
        {
            try
            {
                Assert.True(FastThreadLocalThread.CurrentThreadHasFastThreadLocal());
                Assert.True(FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals());
                Assert.Throws<InvalidOperationException>(() => FastThreadLocalThread.RunWithFastThreadLocal(() => { }));
                local.Set(Thread.CurrentThread.Name);
            }
            catch (Exception cause) { failure = cause; }
        });
        Assert.True(FastThreadLocalThread.WillCleanupFastThreadLocals(thread));
        Assert.True(thread.IsBackground);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.Equal(thread.Name, local.Removed);
    }

    [Fact]
    public void CsvResultsRemainOwnedAcrossFollowingCalls()
    {
        var first = StringUtil.UnescapeCsvFields("\"first\",\"a\"\"b\"");
        var second = StringUtil.UnescapeCsvFields("second,");
        string large = new string('x', 10000);
        Assert.Equal(large, StringUtil.UnescapeCsv("\"" + large + "\""));
        Assert.Equal(new[] { "first", "a\"b" }, first);
        Assert.Equal(new[] { "second", "" }, second);
        Assert.NotSame(first, second);
        Assert.Null(InternalThreadLocalMap.GetIfSet());
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

    [Fact]
    public void MixedTypeBindingsRegisterOnceAndCanBeRemovedAndRebound()
    {
        var text = new RemovalLocal<string>();
        var bytes = new RemovalLocal<byte[]>();
        var value = new RemovalLocal<object>();
        var map = InternalThreadLocalMap.Get();
        Assert.Null(text.Get()); // A null initial value still owns a binding.
        text.Set(map, "first");
        text.Set("second");
        bytes.Set(new byte[] { 1 });
        value.Set(null);
        Assert.Equal(3, FastThreadLocal.Size());
        text.Remove(map);
        Assert.Equal("second", text.Removed);
        Assert.Equal(2, FastThreadLocal.Size());
        text.Set("rebound");
        Assert.Equal(3, FastThreadLocal.Size());
        FastThreadLocal.RemoveAll();
        Assert.Equal(2, text.RemovalCount);
        Assert.Equal("rebound", text.Removed);
        Assert.Equal(1, bytes.RemovalCount);
        Assert.Equal(new byte[] { 1 }, bytes.Removed);
        Assert.Equal(1, value.RemovalCount);
        Assert.Null(value.Removed);
        Assert.Null(InternalThreadLocalMap.GetIfSet());
        Assert.Equal(0, FastThreadLocal.Size());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupSnapshotsBindingsWhileRemovalCallbacksRemoveOtherBindings(bool scoped)
    {
        var locals = new RemovalLocal<string>[3];
        for (int index = 0; index < locals.Length; index++)
            locals[index] = new RemovalLocal<string>
            {
                Removal = _ =>
                {
                    // The first callback removes all peers; do not depend on set iteration order.
                    foreach (var local in locals) local.Remove();
                }
            };
        void Work()
        {
            for (int index = 0; index < locals.Length; index++) locals[index].Set(index.ToString());
            Assert.Equal(3, FastThreadLocal.Size());
            if (!scoped) FastThreadLocal.RemoveAll();
        }
        if (scoped) FastThreadLocalThread.RunWithFastThreadLocal(Work);
        else Work();
        for (int index = 0; index < locals.Length; index++)
        {
            Assert.Equal(1, locals[index].RemovalCount);
            Assert.Equal(index.ToString(), locals[index].Removed);
            Assert.False(locals[index].IsSet());
        }
        Assert.Null(InternalThreadLocalMap.GetIfSet());
        Assert.Equal(0, FastThreadLocal.Size());
    }

    [Fact]
    public void CleanupDetachesTheMapWhenARemovalCallbackThrows()
    {
        var cause = new InvalidOperationException("removal failed");
        var local = new RemovalLocal<string> { Removal = _ => throw cause };
        local.Set("old binding");
        var oldMap = InternalThreadLocalMap.Get();
        Assert.Same(cause, Assert.Throws<InvalidOperationException>(FastThreadLocal.RemoveAll));
        Assert.False(local.IsSet(oldMap));
        Assert.Equal(0, oldMap.Size());
        Assert.Null(InternalThreadLocalMap.GetIfSet());
        local.Removal = null;
        local.Set("new binding");
        Assert.NotSame(oldMap, InternalThreadLocalMap.Get());
        Assert.Equal(1, FastThreadLocal.Size());
        FastThreadLocal.RemoveAll();
        Assert.Equal(2, local.RemovalCount);
        Assert.Equal("new binding", local.Removed);
    }

    private sealed class RemovalLocal<T> : FastThreadLocal<T> where T : class
    {
        public Action<T> Removal;
        public int RemovalCount;
        public T Removed;
        protected override void OnRemoval(T value)
        {
            ++RemovalCount;
            Removed = value;
            Removal?.Invoke(value);
        }
    }
}
