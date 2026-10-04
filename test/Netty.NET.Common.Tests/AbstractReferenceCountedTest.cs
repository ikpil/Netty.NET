/*
 * Copyright 2016 The Netty Project
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests;

public class AbstractReferenceCountedTest
{
    [Fact]
    public void TestRetainOverflow()
    {
        AbstractReferenceCounted referenceCounted = NewReferenceCounted();
        referenceCounted.SetRefCnt(int.MaxValue);
        Assert.Equal(int.MaxValue, referenceCounted.RefCnt());
        Assert.Throws<IllegalReferenceCountException>(referenceCounted.Retain);
    }

    [Fact]
    public void TestRetainOverflow2()
    {
        AbstractReferenceCounted referenceCounted = NewReferenceCounted();
        Assert.Equal(1, referenceCounted.RefCnt());
        Assert.Throws<IllegalReferenceCountException>(() => referenceCounted.Retain(int.MaxValue));
    }

    [Fact]
    public void TestReleaseOverflow()
    {
        AbstractReferenceCounted referenceCounted = NewReferenceCounted();
        referenceCounted.SetRefCnt(0);
        Assert.Equal(0, referenceCounted.RefCnt());
        Assert.Throws<IllegalReferenceCountException>(() => referenceCounted.Release(int.MaxValue));
    }

    [Fact]
    public void TestReleaseErrorMessage()
    {
        AbstractReferenceCounted referenceCounted = NewReferenceCounted();
        Assert.True(referenceCounted.Release());
        try
        {
            referenceCounted.Release(1);
            Assert.Fail("IllegalReferenceCountException didn't occur");
        }
        catch (IllegalReferenceCountException e)
        {
            Assert.Equal("refCnt: 0, decrement: 1", e.Message);
        }
    }

    [Fact]
    public void TestRetainResurrect()
    {
        AbstractReferenceCounted referenceCounted = NewReferenceCounted();
        Assert.True(referenceCounted.Release());
        Assert.Equal(0, referenceCounted.RefCnt());
        Assert.Throws<IllegalReferenceCountException>(referenceCounted.Retain);
    }

    [Fact]
    public void TestRetainResurrect2()
    {
        AbstractReferenceCounted referenceCounted = NewReferenceCounted();
        Assert.True(referenceCounted.Release());
        Assert.Equal(0, referenceCounted.RefCnt());
        Assert.Throws<IllegalReferenceCountException>(() => referenceCounted.Retain(2));
    }

    [Fact(Timeout = 30000)]
    public async Task TestRetainFromMultipleThreadsThrowsReferenceCountException()
    {
        int threads = 4;
        Queue<Task> futures = new Queue<Task>(threads);
        // BCL Task.Run supplies the Java ExecutorService test harness.
        AtomicInteger refCountExceptions = new AtomicInteger();

        try
        {
            for (int i = 0; i < 10000; i++)
            {
                AbstractReferenceCounted referenceCounted = NewReferenceCounted();
                using CountdownEvent retainLatch = new CountdownEvent(1);
                Assert.True(referenceCounted.Release());

                for (int a = 0; a < threads; a++)
                {
                    int retainCnt = Random.Shared.Next(1, int.MaxValue);
                    futures.Enqueue(Task.Run(() =>
                    {
                        try
                        {
                            retainLatch.Wait();
                            try
                            {
                                referenceCounted.Retain(retainCnt);
                            }
                            catch (IllegalReferenceCountException e)
                            {
                                refCountExceptions.IncrementAndGet();
                            }
                        }
                        catch (ThreadInterruptedException e)
                        {
                            Thread.CurrentThread.Interrupt();
                        }
                    }));
                }

                retainLatch.Signal();

                for (;;)
                {
                    futures.TryDequeue(out var f);
                    if (f == null)
                    {
                        break;
                    }

                    await f;
                }

                Assert.Equal(4, refCountExceptions.Get());
                refCountExceptions.Set(0);
            }
        }
        finally
        {
            // All submitted tasks are awaited before the next iteration.
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestReleaseFromMultipleThreadsThrowsReferenceCountException()
    {
        int threads = 4;
        Queue<Task> futures = new Queue<Task>(threads);
        // BCL Task.Run supplies the Java ExecutorService test harness.
        AtomicInteger refCountExceptions = new AtomicInteger();

        try
        {
            for (int i = 0; i < 10000; i++)
            {
                AbstractReferenceCounted referenceCounted = NewReferenceCounted();
                using CountdownEvent releaseLatch = new CountdownEvent(1);
                AtomicInteger releasedCount = new AtomicInteger();

                for (int a = 0; a < threads; a++)
                {
                    AtomicInteger releaseCnt = new AtomicInteger(0);

                    futures.Enqueue(Task.Run(() =>
                    {
                        try
                        {
                            releaseLatch.Wait();
                            try
                            {
                                if (referenceCounted.Release(releaseCnt.IncrementAndGet()))
                                {
                                    releasedCount.IncrementAndGet();
                                }
                            }
                            catch (IllegalReferenceCountException e)
                            {
                                refCountExceptions.IncrementAndGet();
                            }
                        }
                        catch (ThreadInterruptedException e)
                        {
                            Thread.CurrentThread.Interrupt();
                        }
                    }));
                }

                releaseLatch.Signal();

                for (;;)
                {
                    futures.TryDequeue(out var f);
                    if (f == null)
                    {
                        break;
                    }

                    await f;
                }

                Assert.Equal(3, refCountExceptions.Get());
                Assert.Equal(1, releasedCount.Get());

                refCountExceptions.Set(0);
            }
        }
        finally
        {
            // All submitted tasks are awaited before the next iteration.
        }
    }

    public class TestReferenceCounted : AbstractReferenceCounted
    {
        protected override void Deallocate()
        {
            // NOOP
        }

        public override IReferenceCounted Touch(object hint)
        {
            return this;
        }
    }

    public static AbstractReferenceCounted NewReferenceCounted()
    {
        return new TestReferenceCounted();
    }
}
