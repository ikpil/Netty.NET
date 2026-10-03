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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Tests.Porting;

namespace Netty.NET.Common.Tests.Internal;

[Collection("GC cleanup registrations")]
public class ObjectCleanerTest
{
    private Thread temporaryThread;
    private object temporaryObject;

    [Fact(Timeout = 5000)]
    public async Task TestCleanup()
    {
        int freeCalled = 0;
        using var latch = new CountdownEvent(1);
        temporaryThread = new Thread(() =>
        {
            try { latch.Wait(); }
            catch (ThreadInterruptedException)
            {
                // just ignore
            }
        });
        temporaryThread.Start();
        ObjectCleaner.Register(temporaryThread, () => Interlocked.Increment(ref freeCalled));
        latch.Signal();
        temporaryThread.Join();
        Assert.Equal(0, Volatile.Read(ref freeCalled));

        // Null out the temporary object to ensure it is enqueued for GC.
        temporaryThread = null;
        await ObjectCleanerNativeContractTest.CollectUntil(() =>
            Volatile.Read(ref freeCalled) == 1 && ObjectCleaner.PendingCount == 0);
    }

    [Fact(Timeout = 5000)]
    public async Task TestCleanupContinuesDespiteThrowing()
    {
        int freeCalledCount = 0;
        using var latch = new CountdownEvent(1);
        temporaryThread = new Thread(() =>
        {
            try { latch.Wait(); }
            catch (ThreadInterruptedException)
            {
                // just ignore
            }
        });
        temporaryThread.Start();
        ObjectCleaner.Register(temporaryThread, () =>
        {
            Interlocked.Increment(ref freeCalledCount);
            throw new Exception("expected");
        });
        temporaryObject = new object();
        ObjectCleaner.Register(temporaryObject, () =>
        {
            Interlocked.Increment(ref freeCalledCount);
            throw new Exception("expected");
        });
        latch.Signal();
        temporaryThread.Join();
        Assert.Equal(0, Volatile.Read(ref freeCalledCount));

        // Null out the temporary object to ensure it is enqueued for GC.
        temporaryThread = null;
        temporaryObject = null;
        await ObjectCleanerNativeContractTest.CollectUntil(() =>
            Volatile.Read(ref freeCalledCount) == 2 && ObjectCleaner.PendingCount == 0);
    }

    [Fact(Timeout = 5000)]
    public async Task TestCleanerThreadIsDaemon()
    {
        var callbackThread = new TaskCompletionSource<(bool Background, bool ThreadPool)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        temporaryObject = new object();
        ObjectCleaner.Register(temporaryObject, () =>
        {
            // NOOP
            // CLR adaptation: inspect the actual cleanup worker after collection;
            // the thread pool does not create a dedicated named polling thread.
            Thread thread = Thread.CurrentThread;
            callbackThread.SetResult((thread.IsBackground, thread.IsThreadPoolThread));
        });
        temporaryObject = null;
        await ObjectCleanerNativeContractTest.CollectUntil(() =>
            callbackThread.Task.IsCompleted && ObjectCleaner.PendingCount == 0);
        var result = await callbackThread.Task;
        Assert.True(result.Background);
        Assert.True(result.ThreadPool);
    }

}
