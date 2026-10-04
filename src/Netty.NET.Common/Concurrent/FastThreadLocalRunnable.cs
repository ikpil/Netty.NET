/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

// Internal cleanup policy for native delegates; no public Runnable facade is needed.
internal sealed class FastThreadLocalRunnable
{
    private readonly Action _runnable;

    private FastThreadLocalRunnable(Action runnable)
    {
        _runnable = ObjectUtil.CheckNotNull(runnable, "runnable");
    }

    public void Run()
    {
        try
        {
            _runnable();
        }
        finally
        {
            FastThreadLocal.RemoveAll();
        }
    }

    internal static Action Wrap(Action runnable)
    {
        ArgumentNullException.ThrowIfNull(runnable);
        return runnable.Target is FastThreadLocalRunnable && runnable.GetInvocationList().Length == 1
            ? runnable : new FastThreadLocalRunnable(runnable).Run;
    }
}
