/*
 * Copyright 2024 The Netty Project
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
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

public class AdaptiveByteBufAllocatorUseCacheForNonEventLoopThreadsTest : AdaptiveByteBufAllocatorTest
{
    protected override AdaptiveByteBufAllocator NewAllocator(bool preferDirect)
    {
        return Track(new AdaptiveByteBufAllocator(preferDirect, true));
    }

    protected override AdaptiveByteBufAllocator NewUnpooledAllocator()
    {
        return NewAllocator(false);
    }

    [Fact]
    public void TestFastThreadLocalThreadWithoutCleanupFastThreadLocals()
    {
        Exception throwable = null;
        Action task = () =>
        {
            try
            {
                base.TestUsedHeapMemory();
                base.TestUsedDirectMemory();
            }
            catch (Exception e)
            {
                Volatile.Write(ref throwable, e);
            }
        };
        // CLR: FastThreadLocalThread owns its System.Threading.Thread, which cannot be subclassed
        // (src/Netty.NET.Common/Concurrent/FastThreadLocalThread.cs).
        FastThreadLocalThread customizefastThreadLocalThread = new CustomizeFastThreadLocalThreadWithoutCleanupFastThreadLocals(task);
        customizefastThreadLocalThread.Thread.Start();
        customizefastThreadLocalThread.Thread.Join();
        Assert.Null(Volatile.Read(ref throwable));
    }

    private sealed class CustomizeFastThreadLocalThreadWithoutCleanupFastThreadLocals : FastThreadLocalThread
    {

        private readonly Action runnable;

        internal CustomizeFastThreadLocalThreadWithoutCleanupFastThreadLocals(Action runnable)
        {
            this.runnable = ObjectUtil.CheckNotNull(runnable, "runnable");
        }

        // CLR: CleansFastThreadLocals is the declaration hook behind willCleanupFastThreadLocals().
        public override bool CleansFastThreadLocals => false;

        public override void Run()
        {
            runnable(); // Without calling `FastThreadLocal.removeAll()`.
        }
    }
}
