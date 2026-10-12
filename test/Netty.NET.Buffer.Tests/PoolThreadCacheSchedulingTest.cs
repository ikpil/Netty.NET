/*
 * Copyright 2012 The Netty Project
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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using static Netty.NET.Buffer.Tests.AllocatorLeakDetectionTest;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

public class PoolThreadCacheSchedulingTest
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(10);
    private static PooledByteBufAllocator NewAllocator(TimeSpan? period = null, NativeMemoryAllocator native = null)
        => new(maxOrder: 4, nativeAllocator: native, smallCacheSize: 4, normalCacheSize: 4,
            cacheTrimPeriod: period ?? Period);
    private static ByteBuf Allocate(PooledByteBufAllocator a, bool direct, int size)
        => direct ? a.DirectBuffer(size) : a.HeapBuffer(size);
    private static IPoolArenaMetric Arena(PooledByteBufAllocator a, bool direct)
        => (direct ? a.Metric.DirectArenas : a.Metric.HeapArenas)[0];
    private static void Mapped(IEventExecutor executor, Action action) => ThreadExecutorMap.Apply(action, executor)();

    [Theory]
    [InlineData(false, 48)] [InlineData(true, 48)]
    [InlineData(false, 32768)] [InlineData(true, 32768)]
    public void FirstSweepWaitsOnePeriodAndUsesOriginalSuccessfulHitBudget(bool direct, int size)
    {
        var executor = new ManualExecutor(); using var a = NewAllocator(); var m = Arena(a, direct);
        Mapped(executor, () =>
        {
            var buffers = Enumerable.Range(0, 4).Select(_ => Allocate(a, direct, size)).ToArray();
            foreach (var b in buffers) b.Release();
            for (int i = 0; i < 4; ++i) Allocate(a, direct, size).Release();
        });
        Assert.Equal(1, executor.ScheduledCount); Assert.Equal(Period.Ticks * 100, executor.Head.DeadlineNanos());
        executor.Advance(Period - TimeSpan.FromTicks(1)); Assert.Equal(0, executor.RunDue());
        executor.Advance(TimeSpan.FromTicks(1)); Assert.Equal(1, executor.RunDue());
        Assert.Equal(0, m.NumDeallocations); // Four hits preserve the four cached handles this interval.
        executor.Advance(Period); Assert.Equal(1, executor.RunDue());
        Assert.Equal(4, m.NumDeallocations); Assert.Equal(0, m.NumActiveAllocations);
        a.Dispose(); Assert.Equal(0, executor.ScheduledCount);
    }

    [Fact]
    public void FixedRateCatchesUpMissedDeadlinesAndAllocationsDoNotAddSchedules()
    {
        var executor = new ManualExecutor(); using var a = NewAllocator();
        Mapped(executor, () => { for (int i = 0; i < 12; ++i) Allocate(a, false, 48).Release(); });
        long id = executor.Head.GetId();
        executor.Advance(Period * 3); Assert.Equal(3, executor.RunDue());
        Assert.Equal(id, executor.Head.GetId()); Assert.Equal(Period.Ticks * 400, executor.Head.DeadlineNanos());
        Assert.Equal(1, executor.ScheduledCount); Assert.Equal(0, a.Metric.HeapArenas[0].NumActiveAllocations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnmappedThreadAndDisabledPeriodKeepAllocationDrivenCachePolicy(bool disablePeriod)
    {
        var executor = new ManualExecutor(); using var a = NewAllocator(disablePeriod ? TimeSpan.Zero : Period);
        if (disablePeriod) Mapped(executor, () => Allocate(a, false, 48).Release());
        else Allocate(a, false, 48).Release();
        Assert.Equal(0, executor.ScheduledCount); executor.Advance(Period * 100); Assert.Equal(0, executor.RunDue());
        Assert.Equal(1, a.Metric.HeapArenas[0].NumActiveAllocations);
        Assert.True(a.TrimCurrentThreadCache()); Assert.Equal(0, a.Metric.HeapArenas[0].NumActiveAllocations);
    }

    [Fact]
    public void ZeroSizedCachesStillHaveOneExecutorScheduleForTheirOriginalBinding()
    {
        var executor = new ManualExecutor(); using var a = new PooledByteBufAllocator(cacheTrimPeriod: Period);
        Mapped(executor, () => a.HeapBuffer(48).Release());
        Assert.Equal(1, executor.ScheduledCount); Assert.Equal(0, a.Metric.HeapArenas[0].NumActiveAllocations);
        executor.Advance(Period); Assert.Equal(1, executor.RunDue());
        a.ReleaseCurrentThreadBinding(); Assert.Equal(0, executor.ScheduledCount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LaterExecutorMappingDoesNotRescheduleAnExistingPhysicalThreadBinding(bool initiallyMapped)
    {
        var first = new ManualExecutor(); var second = new ManualExecutor(); using var a = NewAllocator();
        if (initiallyMapped) Mapped(first, () => Allocate(a, false, 48).Release());
        else Allocate(a, false, 48).Release();
        Mapped(second, () => Allocate(a, false, 48).Release());
        Assert.Equal(initiallyMapped ? 1 : 0, first.ScheduledCount); Assert.Equal(0, second.ScheduledCount);
        Assert.Equal(1, a.Metric.NumThreadLocalCaches);
        a.ReleaseCurrentThreadBinding();
        Mapped(second, () => Allocate(a, false, 48).Release());
        Assert.Equal(0, first.ScheduledCount); Assert.Equal(1, second.ScheduledCount);
    }

    [Fact]
    public void EmptyAndFullyUnpooledFactoriesDoNotCreateBindingsOrPeriodicWork()
    {
        var executor = new ManualExecutor();
        using var pooled = NewAllocator();
        using var fallback = new PooledByteBufAllocator(false, 0, 0, cacheTrimPeriod: Period);
        Mapped(executor, () =>
        {
            pooled.HeapBuffer(0, 0).Release(); fallback.HeapBuffer(48).Release(); fallback.DirectBuffer(48).Release();
        });
        Assert.Equal(0, pooled.Metric.NumThreadLocalCaches); Assert.Equal(0, fallback.Metric.NumThreadLocalCaches);
        Assert.Equal(0, executor.ScheduledCount);
    }

    [Fact]
    public void ReleasingBindingCancelsTransferredCallbackAndRebindingSchedulesExactlyOnce()
    {
        var executor = new ManualExecutor(); using var a = NewAllocator();
        Mapped(executor, () => Allocate(a, false, 48).Release());
        IScheduledWork oldWork = executor.Head; executor.Advance(Period); Action oldDue = executor.Poll();
        Assert.NotNull(oldDue); Assert.True(a.ReleaseCurrentThreadBinding()); Assert.True(oldWork.IsCanceled);
        Mapped(executor, () => Allocate(a, false, 48).Release());
        Assert.Equal(1, executor.ScheduledCount); Assert.Equal(1, a.Metric.HeapArenas[0].NumActiveAllocations);
        oldDue();
        Assert.Equal(1, a.Metric.HeapArenas[0].NumActiveAllocations); Assert.Equal(1, executor.ScheduledCount);
        executor.Advance(Period); executor.RunDue(); Assert.Equal(0, a.Metric.HeapArenas[0].NumActiveAllocations);
        a.Dispose(); Assert.Equal(0, executor.ScheduledCount); Assert.False(a.TrimCurrentThreadCache());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ClosingAllocatorCancelsScheduleWhileLiveBufferOrLeaseRemainsUsable(bool useLease)
    {
        var native = new NativeMemoryAllocator(); var executor = new ManualExecutor();
        using var a = NewAllocator(native: native); ByteBuf live = null;
        Mapped(executor, () => { live = Allocate(a, true, 48); live.WriteInt(42); Allocate(a, true, 64).Release(); });
        var lease = useLease ? live.AcquireReadLease() : default;
        if (useLease) live.Release();
        IScheduledWork work = executor.Head;
        a.Dispose(); Assert.True(work.IsCanceled); Assert.Equal(0, executor.ScheduledCount);
        Assert.Equal(1, a.Metric.DirectArenas[0].NumActiveAllocations); Assert.True(native.ReservedBytes > 0);
        executor.Advance(Period * 10); Assert.Equal(0, executor.RunDue());
        if (useLease) lease.Dispose();
        else { Assert.Equal(42, live.ReadInt()); live.Release(); }
        Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, a.Metric.DirectArenas[0].NumActiveAllocations);
    }

    [Fact]
    public void ExecutorShutdownStopsPeriodicWorkButKeepsManualAndReturnCleanupValid()
    {
        var executor = new ManualExecutor(); using var a = NewAllocator();
        Mapped(executor, () => Allocate(a, false, 48).Release()); var work = executor.Head;
        executor.Close(); Assert.True(work.IsCanceled); Assert.Equal(0, executor.ScheduledCount);
        Assert.Equal(1, a.Metric.HeapArenas[0].NumActiveAllocations);
        Assert.True(a.TrimCurrentThreadCache()); Assert.Equal(0, a.Metric.HeapArenas[0].NumActiveAllocations);
        a.ReleaseCurrentThreadBinding(); Assert.Equal(0, a.Metric.NumThreadLocalCaches);
    }

    [Fact]
    public void CallbackOnAnotherPhysicalThreadTrimsOriginalBindingWithoutCreatingAnother()
    {
        var executor = new ManualExecutor(); using var a = NewAllocator();
        Mapped(executor, () => Allocate(a, false, 48).Release());
        executor.Advance(Period); Action due = executor.Poll(); Exception error = null;
        var thread = new Thread(() => { try { due(); } catch (Exception failure) { error = failure; } });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10))); Assert.Null(error);
        Assert.Equal(0, a.Metric.HeapArenas[0].NumActiveAllocations); Assert.Equal(1, a.Metric.NumThreadLocalCaches);
        Assert.Equal(1, executor.ScheduledCount);
    }

    [Fact]
    public void TerminatedThreadCleanupCancelsItsPeriodicWorkAndDrainsItsCache()
    {
        var executor = new ManualExecutor(); using var a = NewAllocator(); Exception error = null;
        var thread = new Thread(() =>
        {
            try { Mapped(executor, () => Allocate(a, false, 48).Release()); }
            catch (Exception failure) { error = failure; }
        });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10))); Assert.Null(error);
        Assert.Equal(1, executor.ScheduledCount); Assert.Equal(1, a.ReleaseTerminatedThreadBindings());
        Assert.Equal(0, executor.ScheduledCount); Assert.Equal(0, a.Metric.HeapArenas[0].NumActiveAllocations);
    }

    [Fact]
    public void FailedScheduleRollsBackBindingAndRetryCanUseSameArena()
    {
        var native = new NativeMemoryAllocator(); var executor = new ManualExecutor { RejectSchedule = true };
        using var a = NewAllocator(native: native);
        Mapped(executor, () => Assert.Throws<RejectedExecutionException>(() => a.DirectBuffer(48)));
        Assert.Equal(0, a.Metric.NumThreadLocalCaches); Assert.Equal(0, native.ReservedBytes);
        Assert.All(a.Metric.HeapArenas.Concat(a.Metric.DirectArenas), m => Assert.Equal(0, m.NumThreadCaches));
        executor.RejectSchedule = false;
        Mapped(executor, () => Allocate(a, true, 48).Release());
        Assert.Equal(1, a.Metric.NumThreadLocalCaches); Assert.Equal(1, executor.ScheduledCount);
        a.Dispose(); Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, executor.ScheduledCount);
    }

    [Fact]
    public void UnsupportedMappedExecutorFailsBeforeBackingReservation()
    {
        using var a = NewAllocator(); var executor = new UnsupportedExecutor();
        Mapped(executor, () => Assert.Throws<NotSupportedException>(() => a.HeapBuffer(48)));
        Assert.Equal(0, a.Metric.NumThreadLocalCaches); Assert.Equal(0, a.Metric.UsedHeapMemory);
        Assert.Equal(0, a.Metric.HeapArenas[0].NumThreadCaches);
    }

    [Fact]
    public void FailedBackingAllocationKeepsOneRegisteredScheduleForRetryAndCleanup()
    {
        var native = new NativeMemoryAllocator(0); var executor = new ManualExecutor(); using var a = NewAllocator(native: native);
        Mapped(executor, () =>
        {
            Assert.Throws<OutOfMemoryException>(() => a.DirectBuffer(48));
            Assert.Throws<OutOfMemoryException>(() => a.DirectBuffer(48));
        });
        Assert.Equal(1, executor.ScheduledCount); Assert.Equal(1, a.Metric.NumThreadLocalCaches);
        a.ReleaseCurrentThreadBinding(); Assert.Equal(0, executor.ScheduledCount); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HousekeepingDoesNotRetainAmbientExecutionContextOrChangeFlowSuppression(bool alreadySuppressed)
    {
        var executor = new ManualExecutor(); using var a = NewAllocator(TimeSpan.FromDays(1));
        WeakReference payload;
        if (alreadySuppressed)
        {
            using (ExecutionContext.SuppressFlow())
            {
                payload = AllocateWithAmbientPayload(a, executor); Assert.True(ExecutionContext.IsFlowSuppressed());
            }
        }
        else { payload = AllocateWithAmbientPayload(a, executor); Assert.False(ExecutionContext.IsFlowSuppressed()); }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(payload.IsAlive); Assert.Equal(1, executor.ScheduledCount);
        GC.KeepAlive(a);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AllocateWithAmbientPayload(PooledByteBufAllocator a, ManualExecutor executor)
    {
        var ambient = new AsyncLocal<object> { Value = new object() }; var weak = new WeakReference(ambient.Value);
        Mapped(executor, () => Allocate(a, false, 48).Release()); ambient.Value = null; return weak;
    }

    [Fact]
    public void CanceledReadyCallbackDoesNotRetainDisposedAllocator()
    {
        var executor = new ManualExecutor(); var captured = CreateDisposedAllocator(executor);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(captured.Allocator.IsAlive); Assert.Equal(0, executor.ScheduledCount);
        captured.Callback(); Assert.Equal(0, executor.ScheduledCount); GC.KeepAlive(executor);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Allocator, Action Callback) CreateDisposedAllocator(ManualExecutor executor)
    {
        var a = NewAllocator(); Mapped(executor, () => Allocate(a, false, 48).Release());
        executor.Advance(Period); Action due = executor.Poll(); var weak = new WeakReference(a); a.Dispose();
        return (weak, due);
    }

    [Fact]
    public async Task RealExecutorUsesItsNativeClockAndConcurrentCloseReleasesAllRegions()
    {
        var clock = new ManualTimeProvider(); var executor = new DefaultEventExecutor(clock);
        var native = new NativeMemoryAllocator(); using var a = NewAllocator(native: native);
        try
        {
            Task barrier = null;
            await executor.SubmitAsync(() =>
            {
                Assert.Same(executor, ThreadExecutorMap.CurrentExecutor()); Allocate(a, true, 48).Release();
                // Register inside the worker before advancing time: a zero-delay
                // submission may run from the ready queue ahead of due timers.
                barrier = executor.ScheduleAsync(() => Assert.Equal(0, a.Metric.DirectArenas[0].NumActiveAllocations),
                    Period, TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            clock.Advance(Period);
            executor.Execute(static () => { });
            await barrier.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var buffers = await executor.SubmitAsync(() => Enumerable.Range(0, 64).Select(_ => Allocate(a, true, 48)).ToArray(),
                    TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task returns = Task.Run(() => { foreach (var b in buffers) b.Release(); }, TestContext.Current.CancellationToken);
            clock.Advance(Period); a.Dispose();
            await returns.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, a.Metric.DirectArenas[0].NumActiveAllocations);
        }
        finally { await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task IndependentExecutorsTrimOnlyTheirOwnPhysicalThreadCaches()
    {
        var firstClock = new ManualTimeProvider(); var secondClock = new ManualTimeProvider();
        var first = new DefaultEventExecutor(firstClock); var second = new DefaultEventExecutor(secondClock);
        var native = new NativeMemoryAllocator();
        using var a = new PooledByteBufAllocator(false, 2, 2, maxOrder: 4, nativeAllocator: native,
            smallCacheSize: 4, normalCacheSize: 4, cacheTrimPeriod: Period);
        try
        {
            Task barrier = null; PoolArena firstArena = null, secondArena = null;
            await first.SubmitAsync(() =>
            {
                var b = Allocate(a, true, 48); firstArena = PooledStorage(b).Allocation.Arena; b.Release();
                barrier = first.ScheduleAsync(static () => { }, Period, TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await second.SubmitAsync(() =>
            {
                var b = Allocate(a, true, 48); secondArena = PooledStorage(b).Allocation.Arena; b.Release();
            }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.NotSame(firstArena, secondArena); Assert.Equal(1, firstArena.NumActiveAllocations);
            Assert.Equal(1, secondArena.NumActiveAllocations); Assert.Equal(2, a.Metric.NumThreadLocalCaches);
            firstClock.Advance(Period); first.Execute(static () => { });
            await barrier.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(0, firstArena.NumActiveAllocations); Assert.Equal(1, secondArena.NumActiveAllocations);
            a.Dispose(); Assert.Equal(0, native.ReservedBytes);
        }
        finally
        {
            await Task.WhenAll(first.StopAsync(), second.StopAsync())
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void PeriodValidationAndExtremeDeadlineUseNativeTimeSpanSemantics()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewAllocator(TimeSpan.FromTicks(-1)));
        var executor = new ManualExecutor(); using var a = new PooledByteBufAllocator(false, 1, 0,
            smallCacheSize: 1, cacheTrimPeriod: TimeSpan.MaxValue);
        Mapped(executor, () => Allocate(a, false, 48).Release());
        Assert.Equal(long.MaxValue, executor.Head.DeadlineNanos()); Assert.Equal(0, executor.RunDue());
    }

    private sealed class ManualExecutor : AbstractScheduledEventExecutor
    {
        private readonly MockTicker _clock = global::Netty.NET.Common.Concurrent.Ticker.NewMockTicker();
        private bool _shutdown;
        internal bool RejectSchedule;
        internal IScheduledWork Head => PeekScheduledTask();
        internal int ScheduledCount => _scheduledTaskQueue?.Count ?? 0;
        internal void Advance(TimeSpan amount) => _clock.Advance(amount);
        internal Action Poll() => PollScheduledTask();
        internal int RunDue()
        {
            int count = 0;
            while (Poll() is Action due) { due(); Assert.True(++count < 1024); }
            return count;
        }
        public override Ticker Ticker() => _clock;
        public override bool InEventLoop(Thread thread) => true;
        public override void Execute(Action task) => task();
        [Obsolete]
        protected override void ValidateScheduled(TimeSpan amount)
        { if (RejectSchedule) throw new RejectedExecutionException("Test scheduling rejection"); }
        public override bool IsShutdown() => _shutdown;
        public override bool IsShuttingDown() => _shutdown;
        public override bool IsTerminated() => _shutdown;
        public override bool AwaitTermination(TimeSpan timeout) => _shutdown;
        public override Task Termination => Task.CompletedTask;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) { Close(); return Termination; }
        internal void Close() { _shutdown = true; CancelScheduledTasks(); }
        [Obsolete]
        public override void Shutdown() => Close();
    }

    private sealed class UnsupportedExecutor : AbstractEventExecutor
    {
        public override bool InEventLoop(Thread thread) => true;
        public override void Execute(Action task) => task();
        public override bool IsShutdown() => false;
        public override bool IsShuttingDown() => false;
        public override bool IsTerminated() => false;
        public override bool AwaitTermination(TimeSpan timeout) => false;
        public override Task Termination => Task.CompletedTask;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => Task.CompletedTask;
        [Obsolete]
        public override void Shutdown() { }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        internal void Advance(TimeSpan amount) => Interlocked.Add(ref _timestamp, amount.Ticks);
    }
}
