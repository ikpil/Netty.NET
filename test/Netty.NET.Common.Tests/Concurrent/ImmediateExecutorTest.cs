/*
 * Copyright 2020 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Concurrent;

public class ImmediateExecutorTest
{
    [Fact]
    public void testExecuteNullRunnable() =>
        Assert.Throws<ArgumentNullException>(() => ImmediateExecutor.INSTANCE.execute(null));

    [Fact]
    public void testExecuteNonNullRunnable()
    {
        // CLR: Task.RunSynchronously is the BCL equivalent of executing JDK FutureTask.run.
        var task = new Task<Void>(() =>
        {
            // NOOP
            return null;
        });
        ImmediateExecutor.INSTANCE.execute(Runnables.Create(() => task.RunSynchronously()));
        Assert.True(task.IsCompleted);
        Assert.False(task.IsCanceled);
        Assert.Null(task.GetAwaiter().GetResult());
    }
}
