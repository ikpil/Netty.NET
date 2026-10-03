/*
 * Copyright 2025 The Netty Project
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Concurrent;

public class DefaultMockTickerTest
{
    [Fact]
    void NewMockTickerShouldReturnDefaultMockTicker()
    {
        Assert.True(Ticker.NewMockTicker() is DefaultMockTicker);
    }

    [Fact]
    void DefaultValues()
    {
        MockTicker ticker = Ticker.NewMockTicker();
        Assert.Equal(0, ticker.InitialNanoTime());
        Assert.Equal(0, ticker.NanoTime());
    }

    [Fact]
    void AdvanceWithoutWaiters()
    {
        MockTicker ticker = Ticker.NewMockTicker();
        ticker.Advance(42);
        Assert.Equal(0, ticker.InitialNanoTime());
        Assert.Equal(42, ticker.NanoTime());

        ticker.AdvanceMillis(42);
        Assert.Equal(42_000_042, ticker.NanoTime());
    }

    [Fact]
    void AdvanceWithNegativeAmount()
    {
        MockTicker ticker = Ticker.NewMockTicker();
        Assert.Throws<ArgumentException>(() => {
            ticker.Advance(-1);
        });

        Assert.Throws<ArgumentException>(() => {
            ticker.AdvanceMillis(-1);
        });
    }

    [Fact(Timeout = 60000)]
    public async Task AdvanceWithWaiters()
    {
        var threads = new List<Thread>();
        var futures = new List<Task>();
        var ticker = (DefaultMockTicker)Ticker.NewMockTicker();
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var thread = new Thread(() =>
                {
                    try { ticker.Sleep(TimeSpan.FromMilliseconds(1)); completion.SetResult(); }
                    catch (Exception cause) { completion.TrySetException(cause); }
                }) { IsBackground = true };
                threads.Add(thread);
                futures.Add(completion.Task);
                thread.Start();
            }
            // Wait for all threads to be sleeping.
            foreach (Thread thread in threads) ticker.AwaitSleepingThread(thread);
            // Time did not advance at all, and thus future will not complete.
            foreach (Task future in futures) Assert.False(future.Wait(TimeSpan.FromMilliseconds(1)));
            // Advance just one nanosecond before completion.
            ticker.Advance(999_999);
            // All threads should still be sleeping.
            foreach (Thread thread in threads) ticker.AwaitSleepingThread(thread);
            // Still needs one more nanosecond for our futures.
            foreach (Task future in futures) Assert.False(future.Wait(TimeSpan.FromMilliseconds(1)));
            // Reach at the 1 millisecond mark and ensure the future is complete.
            ticker.Advance(1);
            await Task.WhenAll(futures).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            foreach (Thread thread in threads)
            {
                if (thread.IsAlive) thread.Interrupt();
                Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Ticker waiter did not stop");
            }
        }
    }
    [Fact]
    void SleepZero()
    {
        MockTicker ticker = Ticker.NewMockTicker();
        // All sleep calls with 0 delay should return immediately.
        ticker.Sleep(0);
        ticker.SleepMillis(0);
        Assert.Equal(0, ticker.NanoTime());
    }
}
