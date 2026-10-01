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
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

public class ThreadLocalRandomTest
{
    [Fact]
    public async Task getInitialSeedUniquifierPreservesInterrupt()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.Interrupt();
                // CLR cannot inspect pending interruption without consuming it.
                // Arrange it, initialize the random, and consume it only here.
                ThreadLocalRandom.current();
                Assert.Throws<ThreadInterruptedException>(() => Thread.Sleep(100), 
                    "Assert that thread is interrupted after invocation of getInitialSeedUniquifier()");
                // clear interrupted status in order to not affect other tests
                // CLR adaptation: the assertion above consumes interruption on this dedicated thread.
                completion.SetResult();
            }
            catch (System.Exception cause)
            {
                completion.TrySetException(cause);
            }
        }) { IsBackground = true };

        thread.Start();
        await completion.Task.WaitAsync(System.TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(thread.Join(System.TimeSpan.FromSeconds(5)));
    }
}
