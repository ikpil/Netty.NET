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
using Xunit;

namespace Netty.NET.Common.Tests.Concurrent;

public class ImmediateExecutorTest
{
    [Fact]
    public void TestExecuteNullRunnable() =>
        Assert.Throws<ArgumentNullException>(() => ImmediateExecutor.INSTANCE.Execute(null));

    [Fact]
    public void TestExecuteNonNullRunnable()
    {
        // CLR: run the fresh, uncanceled Task once through the native Action hook.
        var task = new Task(() =>
        {
            // NOOP
        });
        ImmediateExecutor.INSTANCE.Execute(() => task.RunSynchronously());
        Assert.True(task.IsCompleted);
        Assert.False(task.IsCanceled);
        // CLR: an operation with no result returns Task, without a fabricated Void value.
        task.GetAwaiter().GetResult();
        Assert.True(task.IsCompletedSuccessfully);
    }
}
