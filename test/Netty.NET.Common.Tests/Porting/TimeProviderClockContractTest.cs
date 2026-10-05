using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class TimeProviderClockContractTest
{
    private sealed class TimestampProvider(long frequency, long timestamp = 0) : TimeProvider
    {
        private long timestamp = timestamp;
        public override long TimestampFrequency => frequency;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        internal void Advance(long ticks) => Interlocked.Add(ref timestamp, ticks);
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Wall time is unrelated to deadlines");
        public override System.Threading.ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
            => throw new InvalidOperationException("The executor owns scheduling");
    }

    private sealed class ManualExecutor(TimeProvider provider) : AbstractScheduledEventExecutor(null, provider), IDisposable
    {
        private readonly Thread owner = Thread.CurrentThread;
        private readonly ConcurrentQueue<Action> tasks = new();
        private bool stopped;
        internal void Pump()
        {
            Assert.True(InEventLoop());
            while (tasks.TryDequeue(out var normal)) normal();
            Action due;
            while ((due = PollScheduledTask()) != null) due();
        }
        public override bool InEventLoop(Thread thread) => ReferenceEquals(thread, owner);
        public override void Execute(Action task)
        {
            tasks.Enqueue(task);
        }
        public override bool IsShutdown() => stopped;
        public override bool IsShuttingDown() => stopped;
        public override bool IsTerminated() => stopped;
        public override bool AwaitTermination(TimeSpan timeout) => stopped;
        public override Task Termination => Task.CompletedTask;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
        {
            Shutdown();
            return Termination;
        }
        public override void Shutdown() { stopped = true; CancelScheduledTasks(); }
        public void Dispose() => Shutdown();
    }

    [Theory]
    [InlineData(1_000_000_000L, 5L)]
    [InlineData(10_000_000L, 500L)]
    [InlineData(3L, 1_666_666_666L)]
    [InlineData(1_000_000_001L, 4L)]
    public void ProviderFrequencyAndNonzeroOriginProduceExactRelativeNanoseconds(long frequency, long expected)
    {
        const long origin = 987_654_322;
        var provider = new TimestampProvider(frequency, origin);
        var clock = Ticker.FromTimeProvider(provider);
        Assert.Equal((long)((Int128)origin * 1_000_000_000 / frequency), clock.InitialNanoTime());
        Assert.Equal(0, clock.NanoTime());
        provider.Advance(5);
        Assert.Equal(expected, clock.NanoTime());
    }

    [Fact]
    public void NativeTickWrapIsSubtractedBeforeFractionalFrequencyConversion()
    {
        var provider = new TimestampProvider(3, long.MaxValue - 4);
        var clock = Ticker.FromTimeProvider(provider);
        provider.Advance(12);
        Assert.Equal(4_000_000_000L, clock.NanoTime());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void InvalidProviderFrequencyFailsBeforeExecutorAdmission(long frequency)
    {
        var provider = new TimestampProvider(frequency);
        Assert.Throws<ArgumentOutOfRangeException>(() => Ticker.FromTimeProvider(provider));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultEventExecutor(provider));
    }

    [Fact]
    public void SystemSelectionKeepsTheSharedEpochAndNullSelectionFails()
    {
        Assert.Same(Ticker.SystemTicker(), Ticker.FromTimeProvider(TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => Ticker.FromTimeProvider(null));
        Assert.Throws<ArgumentNullException>(() => new DefaultEventExecutor((TimeProvider)null));
    }

    [Fact]
    public void TimestampOnlyProviderDoesNotPretendToSupplySynchronousSleep()
    {
        var clock = Ticker.FromTimeProvider(new TimestampProvider(1_000_000_000));
        Assert.Throws<NotSupportedException>(() => clock.Sleep(1));
        Assert.Throws<NotSupportedException>(() => clock.SleepMillis(1));
        Assert.Throws<NotSupportedException>(() => clock.Sleep(TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void ProviderAdvanceMakesTasksDueWithoutDispatchingThemAndPumpKeepsOrderAndAffinity()
    {
        var provider = new TimestampProvider(10_000_000, 1042);
        using var executor = new ManualExecutor(provider);
        var calls = new List<int>();
        int owner = Environment.CurrentManagedThreadId;
        Task first = executor.ScheduleAsync(() =>
        {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            calls.Add(1);
        }, TimeSpan.FromTicks(3));
        Task second = executor.ScheduleAsync(() => calls.Add(2), TimeSpan.FromTicks(3));
        Task later = executor.ScheduleAsync(() => calls.Add(3), TimeSpan.FromTicks(4));
        executor.Pump();
        provider.Advance(2);
        executor.Pump();
        Assert.False(first.IsCompleted);
        var advancing = new Thread(() => provider.Advance(1)) { IsBackground = true };
        advancing.Start();
        Assert.True(advancing.Join(TimeSpan.FromSeconds(5)));
        Assert.Empty(calls);
        executor.Pump();
        Assert.Equal(new[] { 1, 2 }, calls);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.False(later.IsCompleted);
        provider.Advance(1);
        executor.Pump();
        Assert.Equal(new[] { 1, 2, 3 }, calls);
        Assert.True(later.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DedicatedExecutorUsesProviderDeadlinesAndItsOwnThreadAfterExplicitWakeup()
    {
        var provider = new TimestampProvider(1_000_000_000, 50_000);
        var executor = new DefaultEventExecutor(provider);
        try
        {
            Task<int> delayed = executor.ScheduleAsync(() =>
            {
                Assert.True(executor.InEventLoop());
                return Environment.CurrentManagedThreadId;
            }, TimeSpan.FromDays(1));
            int owner = await executor.SubmitAsync(() => Environment.CurrentManagedThreadId)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(delayed.IsCompleted);
            provider.Advance(86_400_000_000_000L);
            await executor.SubmitAsync(() => { })
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(owner, await delayed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }
}
