using System;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class ThreadConstructionContractTest
{
    [Fact]
    public void NullNativeEntryReportsThePublicTargetParameter()
    {
        Assert.Equal("target", Assert.Throws<ArgumentNullException>(() => new FastThreadLocalThread((Action)null)).ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(131072)]
    public void NativeNamedArgumentsPreserveGroupHintAndOwnedWorkerCleanup(int maxStackSize)
    {
        var group = new ThreadGroup("explicit native group");
        var local = new TrackingLocal();
        Exception failure = null;
        Thread executing = null;
        var owner = new FastThreadLocalThread(() =>
        {
            try
            {
                executing = Thread.CurrentThread;
                Assert.True(FastThreadLocalThread.CurrentThreadHasFastThreadLocal());
                Assert.True(FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals());
                local.Set(new object());
            }
            catch (Exception error) { failure = error; }
        }, group: group, maxStackSize: maxStackSize, name: "native entry");
        Assert.True(owner.CleansFastThreadLocals);
        Assert.Same(group, ThreadGroup.GetThreadGroup(owner.Thread));
        Assert.Equal("native entry", owner.Thread.Name);
        owner.Thread.IsBackground = true;
        owner.Thread.Start();
        Assert.True(owner.Thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.Same(owner.Thread, executing);
        Assert.Equal(1, local.Removed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NativeStackHintsRejectNegativeValuesForBothConstructionModes(int maxStackSize)
    {
        Assert.Equal("maxStackSize", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Thread(() => { }, maxStackSize)).ParamName);
        Assert.Equal("maxStackSize", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FastThreadLocalThread(maxStackSize: maxStackSize)).ParamName);
        Assert.Equal("maxStackSize", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FastThreadLocalThread(() => { }, maxStackSize: maxStackSize)).ParamName);
    }

    private sealed class TrackingLocal : FastThreadLocal<object>
    {
        internal int Removed;
        protected override void OnRemoval(object value) => Removed++;
    }

    private sealed class Item(IRecyclerHandle<Item> handle)
    {
        internal IRecyclerHandle<Item> Handle => handle;
    }
    private sealed class Pool() : Recycler<Item>(4, 0, 2, false)
    {
        protected override Item NewObject(IRecyclerHandle<Item> handle) => new(handle);
    }

    private sealed class ManualWorker(Action work, bool cleanup) : FastThreadLocalThread(name: "manual cleanup")
    {
        internal Exception Failure;
        public override bool CleansFastThreadLocals => cleanup;
        public override void Run()
        {
            try { work(); }
            catch (Exception error) { Failure = error; }
            finally { if (cleanup) FastThreadLocal.RemoveAll(); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubclassCleanupPolicyDrivesMetadataAndActualRecyclerEligibility(bool cleanup)
    {
        var local = new TrackingLocal();
        var worker = new ManualWorker(() =>
        {
            Assert.True(FastThreadLocalThread.CurrentThreadHasFastThreadLocal());
            Assert.Equal(cleanup, FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals());
            local.Set(new object());
            var pool = new Pool();
            Item first = pool.Get();
            first.Handle.Recycle(first);
            Item second = pool.Get();
            if (cleanup) Assert.Same(first, second);
            else Assert.NotSame(first, second);
            second.Handle.Recycle(second);
        }, cleanup);
        Assert.Equal(cleanup, FastThreadLocalThread.WillCleanupFastThreadLocals(worker.Thread));
        worker.Thread.IsBackground = true;
        worker.Thread.Start();
        Assert.True(worker.Thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(worker.Failure);
        Assert.Equal(cleanup ? 1 : 0, local.Removed);
        Assert.Equal(cleanup, FastThreadLocalThread.WillCleanupFastThreadLocals(worker.Thread));
    }

    private sealed class ConservativeWorker(Action target) : FastThreadLocalThread(target)
    {
        public override bool CleansFastThreadLocals => false;
    }

    [Fact]
    public void SubclassesCanDisablePoolingWithoutDisablingTheWrappedEntryCleanup()
    {
        var local = new TrackingLocal();
        Exception failure = null;
        var worker = new ConservativeWorker(() =>
        {
            try
            {
                Assert.False(FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals());
                local.Set(new object());
                var pool = new Pool();
                Item first = pool.Get();
                first.Handle.Recycle(first);
                Assert.NotSame(first, pool.Get());
            }
            catch (Exception error) { failure = error; }
        });
        Assert.False(FastThreadLocalThread.WillCleanupFastThreadLocals(worker.Thread));
        worker.Thread.IsBackground = true;
        worker.Thread.Start();
        Assert.True(worker.Thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.Equal(1, local.Removed);
    }

    [Fact]
    public void TargetlessNativeConstructionHasNoAutomaticCleanupPromise()
    {
        var group = new ThreadGroup("targetless");
        var owner = new FastThreadLocalThread(group: group, name: "native subclass");
        Assert.False(owner.CleansFastThreadLocals);
        Assert.False(FastThreadLocalThread.WillCleanupFastThreadLocals(owner.Thread));
        Assert.Same(group, ThreadGroup.GetThreadGroup(owner.Thread));
        owner.Thread.IsBackground = true;
        owner.Thread.Start();
        Assert.True(owner.Thread.Join(TimeSpan.FromSeconds(5)));
    }
}
