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

namespace Netty.Common.Tests.Concurrent;

class DefaultMockTickerTest
{
    [Fact]
    void newMockTickerShouldReturnDefaultMockTicker()
    {
        Assert.True(Ticker.newMockTicker() instanceof DefaultMockTicker);
    }

    [Fact]
    void defaultValues()
    {
        final MockTicker ticker = Ticker.newMockTicker();
        Assert.Equal(0, ticker.initialNanoTime());
        Assert.Equal(0, ticker.nanoTime());
    }

    [Fact]
    void advanceWithoutWaiters()
    {
        final MockTicker ticker = Ticker.newMockTicker();
        ticker.advance(42, TimeUnit.NANOSECONDS);
        Assert.Equal(0, ticker.initialNanoTime());
        Assert.Equal(42, ticker.nanoTime());

        ticker.advanceMillis(42);
        Assert.Equal(42_000_042, ticker.nanoTime());
    }

    [Fact]
    void advanceWithNegativeAmount()
    {
        final MockTicker ticker = Ticker.newMockTicker();
        Assert.Throws<ArgumentException>(()-> {
            ticker.advance(-1, TimeUnit.SECONDS);
        });

        Assert.Throws<ArgumentException>(()-> {
            ticker.advanceMillis(-1);
        });
    }

    [Timeout(60)]
    [Fact]
    void advanceWithWaiters()
    {
        final List<Thread>
        threads = new List<>();
        final DefaultMockTicker ticker = (DefaultMockTicker)Ticker.newMockTicker();
        final int numWaiters = 4;
        final List<FutureTask<Void>> futures = new List<>();
        for (int i = 0;
             i < numWaiters;
             i++)
        {
            FutureTask<Void> task = new FutureTask<>(()->
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
            for (Thread thread :
            threads) {
                ticker.awaitSleepingThread(thread);
            }

            // Time did not advance at all, and thus future will not complete.
            for (int i = 0; i < numWaiters; i++)
            {
                final int finalCnt = i;
                Assert.Throws<TimeoutException>(()-> {
                    futures.get(finalCnt).get(1, TimeUnit.MILLISECONDS);
                });
            }

            // Advance just one nanosecond before completion.
            ticker.advance(999_999, TimeUnit.NANOSECONDS);

            // All threads should still be sleeping.
            for (Thread thread :
            threads) {
                ticker.awaitSleepingThread(thread);
            }

            // Still needs one more nanosecond for our futures.
            for (int i = 0; i < numWaiters; i++)
            {
                final int finalCnt = i;
                Assert.Throws<TimeoutException>(()-> {
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
            for (Thread thread :
            threads) {
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
        final MockTicker ticker = Ticker.newMockTicker();
        // All sleep calls with 0 delay should return immediately.
        ticker.sleep(0, TimeUnit.SECONDS);
        ticker.sleepMillis(0);
        Assert.Equal(0, ticker.nanoTime());
    }
}