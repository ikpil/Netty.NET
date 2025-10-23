/*
 * Copyright 2014 The Netty Project
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

using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

public class ThreadLocalRandomTest
{
    [Fact]
    public void getInitialSeedUniquifierPreservesInterrupt()
    {
        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.Interrupt();
                Assert.Throws<ThreadInterruptedException>(() => Thread.Sleep(100), 
                    "Assert that thread is interrupted before invocation of getInitialSeedUniquifier()");
                ThreadLocalRandom.current();
                Assert.Throws<ThreadInterruptedException>(() => Thread.Sleep(100), 
                    "Assert that thread is interrupted after invocation of getInitialSeedUniquifier()");
            }
            finally
            {
                //Thread.interrupted(); // clear interrupted status in order to not affect other tests
            }
        });

        thread.Start();
        thread.Join();
    }
}