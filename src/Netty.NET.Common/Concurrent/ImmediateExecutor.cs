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

using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * {@link IExecutor} which execute tasks in the callers thread.
 */
public class ImmediateExecutor : IExecutor
{
    public static readonly ImmediateExecutor INSTANCE = new ImmediateExecutor();

    private ImmediateExecutor()
    {
        // use static instance
    }

    public void execute(IRunnable command)
    {
        ObjectUtil.checkNotNull(command, "command").run();
    }
}