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
        var ticker = Ticker.newMockTicker();
        ticker.advance(TimeSpan.FromTicks(ticks));
        Assert.Equal(expected, ticker.nanoTime());
    }

    [Fact]
    public void MillisecondAdvanceSaturatesBeforeCreatingTimeSpan()
    {
        var ticker = Ticker.newMockTicker();
        ticker.advanceMillis(long.MaxValue);
        Assert.Equal(long.MaxValue, ticker.nanoTime());
        ticker.advance(1);
        Assert.Equal(long.MinValue, ticker.nanoTime());
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
        ticker.sleep(TimeSpan.FromTicks(9_000_000_000_000_001L));
        Assert.Equal(900_000_000_000_000_100L, ticker.Delay);
        ticker.sleep(TimeSpan.MaxValue);
        Assert.Equal(long.MaxValue, ticker.Delay);
        ticker.sleep(TimeSpan.MinValue);
        Assert.Equal(long.MinValue, ticker.Delay);
        ticker.sleepMillis(long.MaxValue);
        Assert.Equal(long.MaxValue, ticker.Delay);
        ticker.sleepMillis(long.MinValue);
        Assert.Equal(long.MinValue, ticker.Delay);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    public void NegativeMockAdvanceDoesNotChangeClock(long amount)
    {
        var ticker = Ticker.newMockTicker();
        Assert.Throws<ArgumentException>(() => ticker.advance(TimeSpan.FromTicks(amount)));
        Assert.Throws<ArgumentException>(() => ticker.advanceMillis(amount));
        Assert.Equal(0, ticker.nanoTime());
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
                Ticker.systemTicker().sleep(delay);
                Ticker.systemTicker().sleepMillis(delay);
                Ticker.systemTicker().sleep(TimeSpan.FromTicks(delay));
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
                if (delay == -1) Ticker.systemTicker().sleepMillis(long.MaxValue);
                else if (delay == -2) Ticker.systemTicker().sleep(TimeSpan.MaxValue);
                else Ticker.systemTicker().sleep(delay);
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
        public override long initialNanoTime() => 0;
        public override long nanoTime() => 0;
        public override void sleep(long delayNanos) => Delay = delayNanos;
    }
}
