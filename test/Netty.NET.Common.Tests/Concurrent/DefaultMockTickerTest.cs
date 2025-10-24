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
using Netty.NET.Common.Concurrent;
using Void = Netty.NET.Common.Concurrent.Void;

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
        ticker.advance(42, TimeUnit.NANOSECONDS);
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
            ticker.advance(-1, TimeUnit.SECONDS);
        });

        Assert.Throws<ArgumentException>(() => {
            ticker.advanceMillis(-1);
        });
    }

    [Fact(Timeout = 60)]
    void advanceWithWaiters()
    {
        List<Thread> threads = new List<>();
        DefaultMockTicker ticker = (DefaultMockTicker)Ticker.newMockTicker();
        int numWaiters = 4;
        List<FutureTask<Void>> futures = new List<>();
        for (int i = 0; i < numWaiters; i++)
        {
            FutureTask<Void> task = new FutureTask<>(() =>
            {
                try {
                ticker.sleep(1, TimeUnit.MILLISECONDS);
            } catch (ThreadInterruptedException e) {
                throw new CompletionException(e);
            }
            return null;
            });
            Thread thread = new Thread(task);
            threads.add(thread);
            futures.add(task);
            thread.start();
        }

        try
        {
            // Wait for all threads to be sleeping.
            foreach (Thread thread in threads) {
                ticker.awaitSleepingThread(thread);
            }

            // Time did not advance at all, and thus future will not complete.
            for (int i = 0; i < numWaiters; i++)
            {
                int finalCnt = i;
                Assert.Throws<TimeoutException>(() => {
                    futures.get(finalCnt).get(1, TimeUnit.MILLISECONDS);
                });
            }

            // Advance just one nanosecond before completion.
            ticker.advance(999_999, TimeUnit.NANOSECONDS);

            // All threads should still be sleeping.
            foreach (Thread thread in threads) {
                ticker.awaitSleepingThread(thread);
            }

            // Still needs one more nanosecond for our futures.
            for (int i = 0; i < numWaiters; i++)
            {
                int finalCnt = i;
                Assert.Throws<TimeoutException>(() => {
                    futures.get(finalCnt).get(1, TimeUnit.MILLISECONDS);
                });
            }

            // Reach at the 1 millisecond mark and ensure the future is complete.
            ticker.advance(1, TimeUnit.NANOSECONDS);
            for (int i = 0; i < numWaiters; i++)
            {
                futures.get(i).get();
            }
        }
        catch (ThreadInterruptedException ie)
        {
            foreach (Thread thread in threads) {
                string name = thread.getName();
                Thread.State state = thread.getState();
                StackTraceElement[] stackTrace = thread.getStackTrace();
                thread.interrupt();
                ThreadInterruptedException threadStackTrace = new ThreadInterruptedException(name + ": " + state);
                threadStackTrace.setStackTrace(stackTrace);
                ie.addSuppressed(threadStackTrace);
            }
            throw ie;
        }
    }


    [Fact]
    void sleepZero()
    {
        MockTicker ticker = Ticker.newMockTicker();
        // All sleep calls with 0 delay should return immediately.
        ticker.sleep(0, TimeUnit.SECONDS);
        ticker.sleepMillis(0);
        Assert.Equal(0, ticker.nanoTime());
    }
}