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
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * A {@link IThreadFactory} implementation with a simple naming rule.
 */
public class DefaultThreadFactory : IThreadFactory
{
    private static readonly AtomicInteger _poolId = new AtomicInteger();

    private readonly AtomicInteger _nextId = new AtomicInteger();
    private readonly string _prefix;
    private readonly bool _daemon;
    private readonly ThreadPriority _priority;
    //protected readonly ThreadGroup _threadGroup;

    public DefaultThreadFactory(Type poolType, bool daemon = false, ThreadPriority priority = ThreadPriority.Normal)
        : this(toPoolName(poolType), daemon, priority)
    {
    }
    
    public static string toPoolName(Type poolType)
    {
        ObjectUtil.checkNotNull(poolType, "poolType");

        string poolName = StringUtil.simpleClassName(poolType);
        switch (poolName.length())
        {
            case 0:
                return "unknown";
            case 1:
                return poolName.ToLowerInvariant();
            default:
                if (char.IsUpper(poolName.charAt(0)) && char.IsLower(poolName.charAt(1)))
                {
                    return char.ToLowerInvariant(poolName.charAt(0)) + poolName.substring(1);
                }
                else
                {
                    return poolName;
                }
        }
    }


    public DefaultThreadFactory(string poolName, bool daemon, ThreadPriority priority)
    {
        ObjectUtil.checkNotNull(poolName, "poolName");

        if (priority < ThreadPriority.Lowest || priority > ThreadPriority.Highest)
        {
            throw new ArgumentException(
                "priority: " + priority + " (expected: ThreadPriority.Lowest <= priority <= ThreadPriority.Highest)");
        }

        _prefix = poolName + '-' + _poolId.incrementAndGet() + '-';
        _daemon = daemon;
        _priority = priority;
        //_threadGroup = threadGroup;
    }


    public Thread newThread(IRunnable r)
    {
        Thread t = newThread(FastThreadLocalRunnable.wrap(r), _prefix + _nextId.incrementAndGet());
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

    private Thread newThread(IRunnable r, string name)
    {
        var t = new Thread(r.run);
        t.Name = name;
        return t;
    }
}