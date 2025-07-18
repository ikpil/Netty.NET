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
namespace Netty.NET.Common.Concurrent;




/**
 * Default implementation which uses simple round-robin to choose next {@link IEventExecutor}.
 */
public final class DefaultEventExecutorChooserFactory : EventExecutorChooserFactory {

    public static readonly DefaultEventExecutorChooserFactory INSTANCE = new DefaultEventExecutorChooserFactory();

    private DefaultEventExecutorChooserFactory() { }

    @Override
    public EventExecutorChooser newChooser(IEventExecutor[] executors) {
        if (isPowerOfTwo(executors.length)) {
            return new PowerOfTwoEventExecutorChooser(executors);
        } else {
            return new GenericEventExecutorChooser(executors);
        }
    }

    private static bool isPowerOfTwo(int val) {
        return (val & -val) == val;
    }

    private static readonly class PowerOfTwoEventExecutorChooser : EventExecutorChooser {
        private readonly AtomicInteger idx = new AtomicInteger();
        private readonly IEventExecutor[] executors;

        PowerOfTwoEventExecutorChooser(IEventExecutor[] executors) {
            this.executors = executors;
        }

        @Override
        public IEventExecutor next() {
            return executors[idx.getAndIncrement() & executors.length - 1];
        }
    }

    private static readonly class GenericEventExecutorChooser : EventExecutorChooser {
        // Use a 'long' counter to avoid non-round-robin behaviour at the 32-bit overflow boundary.
        // The 64-bit long solves this by placing the overflow so far into the future, that no system
        // will encounter this in practice.
        private readonly AtomicLong idx = new AtomicLong();
        private readonly IEventExecutor[] executors;

        GenericEventExecutorChooser(IEventExecutor[] executors) {
            this.executors = executors;
        }

        @Override
        public IEventExecutor next() {
            return executors[(int) Math.abs(idx.getAndIncrement() % executors.length)];
        }
    }
}
