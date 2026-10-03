using System;
using System.Reflection;
using System.Threading;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class MockTickerPhaseContractTest
{
    [Fact]
    public void AWaiterSurvivesSignedClockWrapAndOnlyCompletesAtItsDeadline()
    {
        var ticker = (DefaultMockTicker)Ticker.newMockTicker();
        ticker.advance(long.MaxValue - 50);
        Exception failure = null;
        var sleeper = new Thread(() =>
        {
            try { ticker.sleep(100); }
            catch (Exception cause) { failure = cause; }
        }) { IsBackground = true };
        sleeper.Start();
        try
        {
            ticker.awaitSleepingThread(sleeper);
            ticker.advance(99);
            ticker.awaitSleepingThread(sleeper);
            Assert.Equal(unchecked(long.MaxValue + 49), ticker.nanoTime());
            Assert.True(sleeper.IsAlive);
            ticker.advance(1);
            Assert.True(sleeper.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
        }
        finally
        {
            if (sleeper.IsAlive) sleeper.Interrupt();
            Assert.True(sleeper.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void InterruptedSleepRegistrationDoesNotBlockLaterClockPhases()
    {
        var ticker = (DefaultMockTicker)Ticker.newMockTicker();
        Exception interrupted = null;
        var first = new Thread(() =>
        {
            try { ticker.sleep(100); }
            catch (Exception cause) { interrupted = cause; }
        }) { IsBackground = true };
        using var observed = new ManualResetEventSlim();
        Exception failure = null;
        var next = new Thread(() =>
        {
            try { ticker.sleep(1); }
            catch (Exception cause) { failure = cause; }
        }) { IsBackground = true };
        var observer = new Thread(() =>
        {
            try { ticker.awaitSleepingThread(next); observed.Set(); }
            catch (ThreadInterruptedException) { }
        }) { IsBackground = true };
        first.Start();
        try
        {
            ticker.awaitSleepingThread(first);
            first.Interrupt();
            Assert.True(first.Join(TimeSpan.FromSeconds(5)));
            Assert.IsType<ThreadInterruptedException>(interrupted);
            ticker.advance(1);
            next.Start();
            observer.Start();
            Assert.True(observed.Wait(TimeSpan.FromSeconds(5)));
            ticker.advance(1);
            Assert.True(next.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.Equal(2, ticker.nanoTime());
        }
        finally
        {
            foreach (var thread in new[] { first, next, observer })
            {
                if ((thread.ThreadState & ThreadState.Unstarted) != 0) continue;
                if (thread.IsAlive) thread.Interrupt();
                Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            }
        }
    }

    [Fact]
    public void ContendedAdvancePreservesInterruptForTheNextInterruptibleWait()
    {
        var ticker = (DefaultMockTicker)Ticker.newMockTicker();
        // Only hold the native gate to force contention; assertions concern the
        // public advance result and interrupt policy, not private state values.
        object gate = typeof(DefaultMockTicker).GetField("_lock", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(ticker);
        Exception failure = null;
        bool preserved = false;
        using var done = new ManualResetEventSlim();
        var advancer = new Thread(() =>
        {
            try
            {
                ticker.advance(7);
                try { Thread.Sleep(1); }
                catch (ThreadInterruptedException) { preserved = true; }
            }
            catch (Exception cause) { failure = cause; }
            finally { done.Set(); }
        }) { IsBackground = true };
        try
        {
            lock (gate)
            {
                advancer.Start();
                Assert.True(SpinWait.SpinUntil(() =>
                    (advancer.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
                advancer.Interrupt();
                Assert.False(done.Wait(TimeSpan.FromMilliseconds(25)), "Advance must wait for admission despite interruption");
            }
            Assert.True(advancer.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.True(preserved);
            Assert.Equal(7, ticker.nanoTime());
        }
        finally
        {
            if (advancer.IsAlive) advancer.Interrupt();
            Assert.True(advancer.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void ObservingTheNextSleepCannotConsumeThePreviousSleepRegistration()
    {
        const int phases = 128;
        var ticker = (DefaultMockTicker)Ticker.newMockTicker();
        Exception failure = null;
        int completed = 0;
        var sleeper = new Thread(() =>
        {
            try
            {
                for (int phase = 0; phase < phases; phase++)
                {
                    ticker.sleep(1);
                    Interlocked.Increment(ref completed);
                }
            }
            catch (Exception cause) { failure = cause; }
        }) { IsBackground = true };
        sleeper.Start();
        try
        {
            for (int phase = 0; phase < phases; phase++)
            {
                ticker.awaitSleepingThread(sleeper);
                ticker.advance(1);
            }
            Assert.True(sleeper.Join(TimeSpan.FromSeconds(2)),
                "Each advancement must reach the next sleep phase, rather than advancing past a stale registration");
            Assert.Null(failure);
            Assert.Equal(phases, completed);
        }
        finally
        {
            if (sleeper.IsAlive) sleeper.Interrupt();
            Assert.True(sleeper.Join(TimeSpan.FromSeconds(5)));
        }
    }
}
