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
    void newMockTickerShouldReturnDefaultMockTicker()
    {
        Assert.True(Ticker.newMockTicker() is DefaultMockTicker);
    }

    [Fact]
    void defaultValues()
    {
        MockTicker ticker = Ticker.newMockTicker();
        Assert.Equal(0, ticker.initialNanoTime());
        Assert.Equal(0, ticker.nanoTime());
    }

    [Fact]
    void advanceWithoutWaiters()
    {
        MockTicker ticker = Ticker.newMockTicker();
        ticker.advance(42);
        Assert.Equal(0, ticker.initialNanoTime());
        Assert.Equal(42, ticker.nanoTime());

        ticker.advanceMillis(42);
        Assert.Equal(42_000_042, ticker.nanoTime());
    }

    [Fact]
    void advanceWithNegativeAmount()
    {
        MockTicker ticker = Ticker.newMockTicker();
        Assert.Throws<ArgumentException>(() => {
            ticker.advance(-1);
        });

        Assert.Throws<ArgumentException>(() => {
            ticker.advanceMillis(-1);
        });
    }

    [Fact(Timeout = 60000)]
    public async Task advanceWithWaiters()
    {
        var threads = new List<Thread>();
        var futures = new List<Task>();
        var ticker = (DefaultMockTicker)Ticker.newMockTicker();
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var thread = new Thread(() =>
                {
                    try { ticker.sleep(TimeSpan.FromMilliseconds(1)); completion.SetResult(); }
                    catch (Exception cause) { completion.TrySetException(cause); }
                }) { IsBackground = true };
                threads.Add(thread);
                futures.Add(completion.Task);
                thread.Start();
            }
            // Wait for all threads to be sleeping.
            foreach (Thread thread in threads) ticker.awaitSleepingThread(thread);
            // Time did not advance at all, and thus future will not complete.
            foreach (Task future in futures) Assert.False(future.Wait(TimeSpan.FromMilliseconds(1)));
            // Advance just one nanosecond before completion.
            ticker.advance(999_999);
            // All threads should still be sleeping.
            foreach (Thread thread in threads) ticker.awaitSleepingThread(thread);
            // Still needs one more nanosecond for our futures.
            foreach (Task future in futures) Assert.False(future.Wait(TimeSpan.FromMilliseconds(1)));
            // Reach at the 1 millisecond mark and ensure the future is complete.
            ticker.advance(1);
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
    void sleepZero()
    {
        MockTicker ticker = Ticker.newMockTicker();
        // All sleep calls with 0 delay should return immediately.
        ticker.sleep(0);
        ticker.sleepMillis(0);
        Assert.Equal(0, ticker.nanoTime());
    }
}
