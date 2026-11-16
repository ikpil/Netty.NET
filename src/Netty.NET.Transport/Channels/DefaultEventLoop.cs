/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels;

public class DefaultEventLoop : SingleThreadEventLoop
{
    public DefaultEventLoop() : this((IEventLoopGroup)null)
    {
    }

    public DefaultEventLoop(IThreadFactory threadFactory) : this(null, threadFactory)
    {
    }

    public DefaultEventLoop(Action<Action> executor) : this(null, executor)
    {
    }

    public DefaultEventLoop(IEventLoopGroup parent) : this(parent, new DefaultThreadFactory(typeof(DefaultEventLoop)))
    {
    }

    public DefaultEventLoop(IEventLoopGroup parent, IThreadFactory threadFactory) : base(parent, threadFactory, true)
    {
    }

    public DefaultEventLoop(IEventLoopGroup parent, Action<Action> executor) : base(parent, executor, true)
    {
    }

    protected override void Run()
    {
        for (;;)
        {
            Action task = TakeTask();
            if (task != null)
            {
                RunTask(task);
                UpdateLastExecutionTime();
            }

            if (ConfirmShutdown())
            {
                break;
            }
        }
    }
}
