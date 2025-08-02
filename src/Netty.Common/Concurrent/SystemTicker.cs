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
using System.Threading;

namespace Netty.NET.Common.Concurrent;

sealed class SystemTicker : Ticker
{
    public static readonly SystemTicker INSTANCE = new SystemTicker();
    private static readonly long START_TIME = NanoTimer.nanoTime();

    public override long initialNanoTime()
    {
        return START_TIME;
    }

    public override long nanoTime()
    {
        return NanoTimer.nanoTime() - START_TIME;
    }

    public override void sleep(TimeSpan delay)
    {
        //Objects.requireNonNull(unit, "unit");
        Thread.Sleep(delay);
    }
}