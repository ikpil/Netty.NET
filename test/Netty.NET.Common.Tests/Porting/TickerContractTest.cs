using System;
using System.Numerics;
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class TickerContractTest
{
    [Theory]
    [InlineData(9_000_000_000_000_001L, 900_000_000_000_000_100L)]
    [InlineData(92_233_720_368_547_758L, 9_223_372_036_854_775_800L)]
    [InlineData(long.MaxValue, long.MaxValue)]
    public void TimeSpanAdvanceUsesExactIntegerNanosecondsAndSaturates(long ticks, long expected)
    {
        var ticker = Ticker.NewMockTicker();
        ticker.Advance(TimeSpan.FromTicks(ticks));
        Assert.Equal(expected, ticker.NanoTime());
    }

    [Fact]
    public void MillisecondAdvanceSaturatesBeforeCreatingTimeSpan()
    {
        var ticker = Ticker.NewMockTicker();
        ticker.AdvanceMillis(long.MaxValue);
        Assert.Equal(long.MaxValue, ticker.NanoTime());
        ticker.Advance(1);
        Assert.Equal(long.MinValue, ticker.NanoTime());
    }

    [Theory]
    [InlineData(9_007_199_254_740_993L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void NativeTimestampScalingPreservesIntegerPrecisionAndSignedWrap(long timestamp)
    {
        long frequency = TimeProvider.System.TimestampFrequency;
        foreach (long units in new[] { 1_000L, SystemTimer.NanosecondsPerSecond, frequency + 1 })
        {
            BigInteger scaled = (BigInteger)timestamp * units / frequency;
            BigInteger modulus = BigInteger.One << 64;
            BigInteger wrapped = (scaled % modulus + modulus) % modulus;
            long expected = wrapped > long.MaxValue ? (long)(wrapped - modulus) : (long)wrapped;
            Assert.Equal(expected, SystemTimer.ScaleTimestamp(timestamp, units));
        }
    }

    [Fact]
    public void NativeDurationSleepConversionSaturatesBothSignsWithoutRounding()
    {
        var ticker = new RecordingTicker();
        ticker.Sleep(TimeSpan.FromTicks(9_000_000_000_000_001L));
        Assert.Equal(900_000_000_000_000_100L, ticker.Delay);
        ticker.Sleep(TimeSpan.MaxValue);
        Assert.Equal(long.MaxValue, ticker.Delay);
        ticker.Sleep(TimeSpan.MinValue);
        Assert.Equal(long.MinValue, ticker.Delay);
        ticker.SleepMillis(long.MaxValue);
        Assert.Equal(long.MaxValue, ticker.Delay);
        ticker.SleepMillis(long.MinValue);
        Assert.Equal(long.MinValue, ticker.Delay);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    public void NegativeMockAdvanceDoesNotChangeClock(long amount)
    {
        var ticker = Ticker.NewMockTicker();
        Assert.Throws<ArgumentException>(() => ticker.Advance(TimeSpan.FromTicks(amount)));
        Assert.Throws<ArgumentException>(() => ticker.AdvanceMillis(amount));
        Assert.Equal(0, ticker.NanoTime());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void NonpositiveSystemSleepLeavesPendingInterruptForTheNextWait(long delay)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.Interrupt();
                Ticker.SystemTicker().Sleep(delay);
                Ticker.SystemTicker().SleepMillis(delay);
                Ticker.SystemTicker().Sleep(TimeSpan.FromTicks(delay));
                Assert.Throws<ThreadInterruptedException>(() => Thread.Sleep(1));
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(2_147_483_648_000_000L)]
    [InlineData(long.MaxValue)]
    [InlineData(-1L)]
    [InlineData(-2L)]
    public void LongSystemSleepRemainsInterruptibleWithoutNarrowingOverflow(long delay)
    {
        Exception outcome = null;
        using var entered = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            entered.Set();
            try
            {
                if (delay == -1) Ticker.SystemTicker().SleepMillis(long.MaxValue);
                else if (delay == -2) Ticker.SystemTicker().Sleep(TimeSpan.MaxValue);
                else Ticker.SystemTicker().Sleep(delay);
            }
            catch (Exception error) { outcome = error; }
        }) { IsBackground = true };
        thread.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => !thread.IsAlive ||
                (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
            Assert.True(thread.IsAlive, "A distant deadline must not return or throw immediately");
            thread.Interrupt();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.IsType<ThreadInterruptedException>(outcome);
        }
        finally
        {
            if (thread.IsAlive) thread.Interrupt();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        }
    }

    private sealed class RecordingTicker : Ticker
    {
        public long Delay { get; private set; }
        public override long InitialNanoTime() => 0;
        public override long NanoTime() => 0;
        public override void Sleep(long delayNanos) => Delay = delayNanos;
    }
}
