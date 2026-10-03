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

// CLR adaptation: nested chooser support types are separate files, as for IEventExecutorChooser.
/**
 * A container for the utilization metric of a single EventExecutor.
 * This object is intended to be created once and have its {@code utilization}
 * field updated periodically.
 */
public sealed class AutoScalingUtilizationMetric
{
    private readonly IEventExecutor associatedExecutor;
    private long utilizationBits;
    internal AutoScalingUtilizationMetric(IEventExecutor executor) => associatedExecutor = executor;

    /**
     * Returns the most recently calculated utilization for the associated executor.
     * @return a value from 0.0 to 1.0.
     */
    public double Utilization() => BitConverter.Int64BitsToDouble(Volatile.Read(ref utilizationBits));

    /**
     * Returns the {@link EventExecutor} this metric belongs too.
     * @return the executor.
     */
    public IEventExecutor Executor() => associatedExecutor;

    internal void SetUtilization(double utilization) => Volatile.Write(ref utilizationBits, BitConverter.DoubleToInt64Bits(utilization));
}
