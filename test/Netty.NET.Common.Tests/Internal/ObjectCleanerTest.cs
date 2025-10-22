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
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

public class ObjectCleanerTest
{
    private Thread temporaryThread;
    private object temporaryObject;

    [Fact(Timeout = 5000)]
    public void testCleanup()
    {
        AtomicBoolean freeCalled = new AtomicBoolean();
        CountdownEvent latch = new CountdownEvent(1);
        temporaryThread = new Thread(() =>
            {
                try
                {
                    latch.Wait();
                }
                catch (ThreadInterruptedException ignore)
                {
                    // just ignore
                }
            }
        );
        temporaryThread.Start();
        ObjectCleaner.register(temporaryThread, Runnables.Create(() =>
        {
            freeCalled.set(true);
        }));


        latch.Signal();
        temporaryThread.Join();
        Assert.False(freeCalled.get());

        // Null out the temporary object to ensure it is enqueued for GC.
        temporaryThread = null;

        while (!freeCalled.get())
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(100);
        }
    }

    [Fact(Timeout = 5000)]
    public void testCleanupContinuesDespiteThrowing()
    {
        AtomicInteger freeCalledCount = new AtomicInteger();
        CountdownEvent latch = new CountdownEvent(1);
        temporaryThread = new Thread(() =>
        {
            try
            {
                latch.Wait();
            }
            catch (ThreadInterruptedException ignore)
            {
                // just ignore
            }
        });
        temporaryThread.Start();
        temporaryObject = new object();
        ObjectCleaner.register(temporaryThread, Runnables.Create(() =>
        {
            freeCalledCount.incrementAndGet();
            throw new Exception("expected");
        }));
        ObjectCleaner.register(temporaryObject, Runnables.Create(() =>
        {
            freeCalledCount.incrementAndGet();
            throw new Exception("expected");
        }));

        latch.Signal();
        temporaryThread.Join();
        Assert.Equal(0, freeCalledCount.get());

        // Null out the temporary object to ensure it is enqueued for GC.
        temporaryThread = null;
        temporaryObject = null;

        while (freeCalledCount.get() != 2)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(100);
        }
    }

    [Fact(Timeout = 5000)]
    public void testCleanerThreadIsDaemon()
    {
        temporaryObject = new object();
        ObjectCleaner.register(temporaryObject, Runnables.Create(() =>
        {
            // NOOP
        }));

        Thread cleanerThread = ObjectCleaner.CLEANUP_THREAD;
        // foreach (Thread thread in Thread.getAllStackTraces().keySet()) {
        //     if (thread.Name.Equals(ObjectCleaner.CLEANER_THREAD_NAME))
        //     {
        //         cleanerThread = thread;
        //         break;
        //     }
        // }
        Assert.NotNull(cleanerThread);
        Assert.True(cleanerThread.IsBackground);
    }
}