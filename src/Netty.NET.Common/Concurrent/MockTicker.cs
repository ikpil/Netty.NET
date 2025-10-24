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

namespace Netty.NET.Common.Concurrent;

/**
 * A fake {@link Ticker} that allows the caller control the flow of time.
 * This can be useful when you test time-sensitive logic without waiting for too long
 * or introducing flakiness due to non-deterministic nature of system clock.
 */
public abstract class MockTicker : Ticker
{
    public override long initialNanoTime()
    {
        return 0;
    }

    /**
     * Advances the current {@link #nanoTime()} by the given amount of time.
     *
     * @param amount the amount of time to advance this ticker by.
     * @param unit the {@link TimeSpan} of {@code amount}.
     */
    public abstract void advance(long amountNanos);

    /**
     * Advances the current {@link #nanoTime()} by the given amount of time.
     *
     * @param amountMillis the number of milliseconds to advance this ticker by.
     */
    public void advanceMillis(long amountMillis)
    {
        advance(TimeSpan.FromMilliseconds(amountMillis));
    }

    public void advance(TimeSpan amount)
    {
        advance((long)amount.TotalNanoseconds);
    }
}