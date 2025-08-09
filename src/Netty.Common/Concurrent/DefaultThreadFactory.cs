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
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

public class ThreadGroup
{
}

/**
 * A {@link IThreadFactory} implementation with a simple naming rule.
 */
public class DefaultThreadFactory : IThreadFactory 
{
    private static readonly AtomicInteger poolId = new AtomicInteger();

    private readonly AtomicInteger nextId = new AtomicInteger();
    private readonly string prefix;
    private readonly bool daemon;
    private readonly ThreadPriority priority;
    protected readonly ThreadGroup threadGroup;

    public DefaultThreadFactory(Type poolType) 
        : this(poolType, false, ThreadPriority.Normal)
    {
    }

    public DefaultThreadFactory(string poolName) 
        : this(poolName, false, ThreadPriority.Normal)
    {
    }

    public DefaultThreadFactory(Type poolType, bool daemon) 
        : this(poolType, daemon, ThreadPriority.Normal)
    {
    }

    public DefaultThreadFactory(string poolName, bool daemon) 
        : this(poolName, daemon, ThreadPriority.Normal)
    {
    }

    public DefaultThreadFactory(Type poolType, ThreadPriority priority) 
        : this(poolType, false, priority)
    {
    }

    public DefaultThreadFactory(string poolName, ThreadPriority priority) 
        : this(poolName, false, priority)
    {
    }

    public DefaultThreadFactory(Type poolType, bool daemon, ThreadPriority priority) 
        : this(toPoolName(poolType), daemon, priority)
    {
    }

    public static string toPoolName(Type poolType) 
    {
        ObjectUtil.checkNotNull(poolType, "poolType");

        string poolName = StringUtil.simpleClassName(poolType);
        switch (poolName.length()) {
            case 0:
                return "unknown";
            case 1:
                return poolName.ToLowerInvariant();
            default:
                if (char.IsUpper(poolName.charAt(0)) && char.IsLower(poolName.charAt(1))) {
                    return char.ToLowerInvariant(poolName.charAt(0)) + poolName.substring(1);
                } else {
                    return poolName;
                }
        }
    }

    public DefaultThreadFactory(string poolName, bool daemon, ThreadPriority priority) 
        : this(poolName, daemon, priority, null)
    {
    }
    
    public DefaultThreadFactory(string poolName, bool daemon, ThreadPriority priority, ThreadGroup threadGroup) {
        ObjectUtil.checkNotNull(poolName, "poolName");

        if (priority < ThreadPriority.Lowest || priority > ThreadPriority.Highest) {
            throw new ArgumentException(
                    "priority: " + priority + " (expected: Thread.MIN_PRIORITY <= priority <= Thread.MAX_PRIORITY)");
        }

        prefix = poolName + '-' + poolId.incrementAndGet() + '-';
        this.daemon = daemon;
        this.priority = priority;
        this.threadGroup = threadGroup;
    }


    public Thread newThread(IRunnable r) {
        Thread t = newThread(FastThreadLocalRunnable.wrap(r), prefix + nextId.incrementAndGet());
        try {
            if (t.isDaemon() != daemon) {
                t.setDaemon(daemon);
            }

            if (t.getPriority() != priority) {
                t.setPriority(priority);
            }
        } catch (Exception ignored) {
            // Doesn't matter even if failed to set.
        }
        return t;
    }

    protected Thread newThread(Runnable r, string name) {
        return new FastThreadLocalThread(threadGroup, r, name);
    }
}
