/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * A {@link ThreadFactory} implementation with a simple naming rule.
 */
public class DefaultThreadFactory : IThreadFactory
{
    private static readonly AtomicInteger _poolId = new AtomicInteger();

    private readonly AtomicInteger _nextId = new AtomicInteger();
    private readonly string _prefix;
    private readonly bool _daemon;
    private readonly ThreadPriority _priority;
    protected readonly ThreadGroup _threadGroup;

    public DefaultThreadFactory(Type poolType, bool daemon = false, ThreadPriority priority = ThreadPriority.Normal)
        : this(ToPoolName(poolType), daemon, priority, null)
    {
    }

    public DefaultThreadFactory(string poolName, bool daemon = false, ThreadPriority priority = ThreadPriority.Normal)
        : this(poolName, daemon, priority, null)
    {
    }

    public DefaultThreadFactory(Type poolType, ThreadPriority priority) : this(poolType, false, priority)
    {
    }

    public DefaultThreadFactory(string poolName, ThreadPriority priority) : this(poolName, false, priority)
    {
    }

    public static string ToPoolName(Type poolType)
    {
        ObjectUtil.CheckNotNull(poolType, "poolType");

        string poolName = StringUtil.SimpleClassName(poolType);
        switch (poolName.Length)
        {
            case 0:
                return "unknown";
            case 1:
                return poolName.ToLowerInvariant();
            default:
                if (char.IsUpper(poolName[0]) && char.IsLower(poolName[1]))
                {
                    return char.ToLowerInvariant(poolName[0]) + poolName[1..];
                }
                else
                {
                    return poolName;
                }
        }
    }


    public DefaultThreadFactory(string poolName, bool daemon, ThreadPriority priority, ThreadGroup threadGroup)
    {
        ObjectUtil.CheckNotNull(poolName, "poolName");

        if (priority < ThreadPriority.Lowest || priority > ThreadPriority.Highest)
        {
            throw new ArgumentException(
                "priority: " + priority + " (expected: ThreadPriority.Lowest <= priority <= ThreadPriority.Highest)");
        }

        _prefix = poolName + '-' + _poolId.IncrementAndGet() + '-';
        _daemon = daemon;
        _priority = priority;
        _threadGroup = threadGroup;
    }


    public virtual Thread NewThread(IRunnable r)
    {
        Thread t = NewThread(FastThreadLocalRunnable.Wrap(r), _prefix + _nextId.IncrementAndGet());
        try
        {
            if (t.IsBackground != _daemon)
            {
                t.IsBackground = _daemon;
            }

            if (t.Priority != _priority)
            {
                t.Priority = _priority;
            }
        }
        catch (Exception ignored)
        {
            // Doesn't matter even if failed to set.
        }

        return t;
    }

    protected virtual Thread NewThread(IRunnable r, string name)
    {
        return new FastThreadLocalThread(_threadGroup, r, name).Thread;
    }
}
