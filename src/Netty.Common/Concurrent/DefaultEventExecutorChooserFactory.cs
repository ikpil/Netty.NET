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
public sealed class DefaultEventExecutorChooserFactory : IEventExecutorChooserFactory
{
    public static readonly DefaultEventExecutorChooserFactory INSTANCE = new DefaultEventExecutorChooserFactory();

    private DefaultEventExecutorChooserFactory() { }

    public IEventExecutorChooser newChooser(IEventExecutor[] executors)
    {
        if (isPowerOfTwo(executors.Length))
        {
            return new PowerOfTwoEventExecutorChooser(executors);
        }
        else
        {
            return new GenericEventExecutorChooser(executors);
        }
    }

    private static bool isPowerOfTwo(int val)
    {
        return (val & -val) == val;
    }
}