/*
 * Copyright 2016 The Netty Project
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

/**
 * Default implementation which uses simple round-robin to choose next {@link EventExecutor}.
 */
public sealed class DefaultEventExecutorChooserFactory : IEventExecutorChooserFactory
{
    public static readonly DefaultEventExecutorChooserFactory INSTANCE = new DefaultEventExecutorChooserFactory();

    private DefaultEventExecutorChooserFactory() { }

    public IEventExecutorChooser NewChooser(IEventExecutor[] executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        if (IsPowerOfTwo(executors.Length))
        {
            return new PowerOfTwoEventExecutorChooser(executors);
        }
        else
        {
            return new GenericEventExecutorChooser(executors);
        }
    }

    private static bool IsPowerOfTwo(int val)
    {
        return (val & -val) == val;
    }

    private sealed class PowerOfTwoEventExecutorChooser : IEventExecutorChooser
    {
        private int idx;
        private readonly IEventExecutor[] executors;

        internal PowerOfTwoEventExecutorChooser(IEventExecutor[] executors) => this.executors = executors;

        public IEventExecutor Next()
        {
            // CLR checked builds must preserve Java's wrapping get-and-increment.
            int ticket = unchecked(Interlocked.Increment(ref idx) - 1);
            return executors[ticket & executors.Length - 1];
        }
    }

    private sealed class GenericEventExecutorChooser : IEventExecutorChooser
    {
        // Use a 'long' counter to avoid non-round-robin behaviour at the 32-bit overflow boundary.
        // The 64-bit long solves this by placing the overflow so far into the future, that no system
        // will encounter this in practice.
        private long idx;
        private readonly IEventExecutor[] executors;

        internal GenericEventExecutorChooser(IEventExecutor[] executors) => this.executors = executors;

        public IEventExecutor Next()
        {
            // Take the remainder before Abs, so long.MinValue stays a bounded index.
            long ticket = unchecked(Interlocked.Increment(ref idx) - 1);
            return executors[(int)Math.Abs(ticket % executors.Length)];
        }
    }
}
