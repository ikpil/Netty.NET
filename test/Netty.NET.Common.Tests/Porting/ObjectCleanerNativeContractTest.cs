using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

[Collection("GC cleanup registrations")]
public class ObjectCleanerNativeContractTest
{
    [Fact]
    public void InvalidInputsDoNotPublishARegistration()
    {
        int before = ObjectCleaner.PendingCount;
        Assert.Throws<ArgumentNullException>(() => ObjectCleaner.Register(null, () => { }));
        Assert.Throws<ArgumentNullException>(() => ObjectCleaner.Register(new object(), null));
        Assert.Equal(before, ObjectCleaner.PendingCount);
    }

    [Fact(Timeout = 10000)]
    public async Task RegistrationDoesNotRootItsTargetAndRunsExactlyOnce()
    {
        var counter = new Counter();
        int before = ObjectCleaner.PendingCount;
        WeakReference target = RegisterTemporary(() => Interlocked.Increment(ref counter.Value));
        await CollectUntil(() => Volatile.Read(ref counter.Value) == 1 && ObjectCleaner.PendingCount == before);
        Assert.False(target.IsAlive);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.Equal(1, Volatile.Read(ref counter.Value));
    }

    [Fact(Timeout = 10000)]
    public async Task ConditionalCallbackMayReferenceItsKeyWithoutGloballyRootingIt()
    {
        var counter = new Counter();
        int before = ObjectCleaner.PendingCount;
        WeakReference target = RegisterCapturingTarget(counter);
        await CollectUntil(() => Volatile.Read(ref counter.Value) == 1 && ObjectCleaner.PendingCount == before);
        Assert.False(target.IsAlive);
    }

    [Fact(Timeout = 10000)]
    public async Task LiveTargetRetainsItsCleanupAndCleanupRetainsItsPayloadUntilInvocation()
    {
        var counter = new Counter();
        int before = ObjectCleaner.PendingCount;
        var holder = new Holder();
        WeakReference payload = RegisterLive(holder, counter);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.Equal(0, Volatile.Read(ref counter.Value));
        Assert.True(payload.IsAlive);
        holder.Target = null;
        await CollectUntil(() => Volatile.Read(ref counter.Value) == 1 && ObjectCleaner.PendingCount == before);
        await CollectUntil(() => !payload.IsAlive);
        GC.KeepAlive(holder);
    }

    [Fact(Timeout = 10000)]
    public async Task EqualTargetsHaveIndependentReferenceIdentityLifetimes()
    {
        var counter = new Counter();
        int before = ObjectCleaner.PendingCount;
        var holder = new Holder();
        RegisterEqualTargets(holder, counter);
        await CollectUntil(() => Volatile.Read(ref counter.Value) == 1 && ObjectCleaner.PendingCount == before + 1);
        Assert.Equal(before + 1, ObjectCleaner.PendingCount);
        holder.Target = null;
        await CollectUntil(() => Volatile.Read(ref counter.Value) == 2 && ObjectCleaner.PendingCount == before);
        GC.KeepAlive(holder);
    }

    [Fact(Timeout = 10000)]
    public async Task ConcurrentRegistrationsForOneTargetAreNotLostOrDuplicated()
    {
        const int count = 64;
        var counter = new Counter();
        int before = ObjectCleaner.PendingCount;
        RegisterMany(counter, count);
        await CollectUntil(() => Volatile.Read(ref counter.Value) == count && ObjectCleaner.PendingCount == before);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.Equal(count, Volatile.Read(ref counter.Value));
    }

    [Fact(Timeout = 10000)]
    public async Task CleanupRunsWithoutRegistrarExecutionContext()
    {
        var ambient = new AsyncLocal<string>();
        var observed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int before = ObjectCleaner.PendingCount;
        ambient.Value = "registrar";
        try
        {
            RegisterTemporary(() => observed.TrySetResult(ambient.Value));
            await CollectUntil(() => observed.Task.IsCompleted && ObjectCleaner.PendingCount == before);
            Assert.Null(await observed.Task);
        }
        finally { ambient.Value = null; }
    }

    [Fact(Timeout = 10000)]
    public async Task BlockingCleanupDoesNotBlockFinalizersOrOtherCleanup()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int before = ObjectCleaner.PendingCount;
        RegisterTemporary(() =>
        {
            entered.Set();
            release.Wait();
        });
        try
        {
            await CollectUntil(() => entered.IsSet);
            RegisterTemporary(() => second.TrySetResult());
            await CollectUntil(() => second.Task.IsCompleted);
            Assert.False(release.IsSet);
        }
        finally { release.Set(); }
        await CollectUntil(() => ObjectCleaner.PendingCount == before);
    }

    internal static async Task CollectUntil(Func<bool> completed)
    {
        var clock = Stopwatch.StartNew();
        while (!completed() && clock.Elapsed < TimeSpan.FromSeconds(4))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(20);
        }
        Assert.True(completed(), "The expected GC notification/callback did not complete.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterTemporary(Action cleanup)
    {
        object target = new object();
        ObjectCleaner.Register(target, cleanup);
        return new WeakReference(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterCapturingTarget(Counter counter)
    {
        object target = new object();
        ObjectCleaner.Register(target, () =>
        {
            GC.KeepAlive(target);
            Interlocked.Increment(ref counter.Value);
        });
        return new WeakReference(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterLive(Holder holder, Counter counter)
    {
        holder.Target = new object();
        byte[] payload = new byte[4096];
        ObjectCleaner.Register(holder.Target, () =>
        {
            Assert.Equal(4096, payload.Length);
            Interlocked.Increment(ref counter.Value);
        });
        return new WeakReference(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterEqualTargets(Holder holder, Counter counter)
    {
        object first = new EqualTarget();
        holder.Target = new EqualTarget();
        ObjectCleaner.Register(first, () => Interlocked.Increment(ref counter.Value));
        ObjectCleaner.Register(holder.Target, () => Interlocked.Increment(ref counter.Value));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterMany(Counter counter, int count)
    {
        object target = new object();
        Parallel.For(0, count, _ => ObjectCleaner.Register(target, () => Interlocked.Increment(ref counter.Value)));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.Equal(0, Volatile.Read(ref counter.Value));
        GC.KeepAlive(target);
    }

    private sealed class Holder { internal object Target; }
    private sealed class Counter { internal int Value; }
    private sealed class EqualTarget
    {
        public override bool Equals(object obj) => obj is EqualTarget;
        public override int GetHashCode() => 1;
    }
}
