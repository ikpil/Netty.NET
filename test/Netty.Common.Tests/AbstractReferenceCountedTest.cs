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

using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.Common.Tests;

public class AbstractReferenceCountedTest 
{

    [Fact]
    public void testRetainOverflow() {
        AbstractReferenceCounted referenceCounted = newReferenceCounted();
        referenceCounted.setRefCnt(int.MaxValue);
        Assert.Equal(int.MaxValue, referenceCounted.refCnt());
        Assert.Throws<IllegalReferenceCountException>(referenceCounted.retain);
    }

    [Fact]
    public void testRetainOverflow2() {
        AbstractReferenceCounted referenceCounted = newReferenceCounted();
        Assert.Equal(1, referenceCounted.refCnt());
        Assert.Throws<IllegalReferenceCountException>(() => referenceCounted.retain(int.MaxValue));
    }

    [Fact]
    public void testReleaseOverflow() {
        AbstractReferenceCounted referenceCounted = newReferenceCounted();
        referenceCounted.setRefCnt(0);
        Assert.Equal(0, referenceCounted.refCnt());
        Assert.Throws<IllegalReferenceCountException>(() => referenceCounted.release(int.MaxValue));
    }

    [Fact]
    public void testReleaseErrorMessage() {
        AbstractReferenceCounted referenceCounted = newReferenceCounted();
        Assert.True(referenceCounted.release());
        try {
            referenceCounted.release(1);
            Assert.Fail("IllegalReferenceCountException didn't occur");
        } catch (IllegalReferenceCountException e) {
            Assert.Equal("refCnt: 0, decrement: 1", e.Message);
        }
    }

    [Fact]
    public void testRetainResurrect() {
        AbstractReferenceCounted referenceCounted = newReferenceCounted();
        Assert.True(referenceCounted.release());
        Assert.Equal(0, referenceCounted.refCnt());
        Assert.Throws<IllegalReferenceCountException>(referenceCounted.retain);
    }

    [Fact]
    public void testRetainResurrect2() {
        AbstractReferenceCounted referenceCounted = newReferenceCounted();
        Assert.True(referenceCounted.release());
        Assert.Equal(0, referenceCounted.refCnt());
        Assert.Throws<IllegalReferenceCountException>(() => referenceCounted.retain(2));
    }

    [Fact(Timeout = 30000)]
    public void testRetainFromMultipleThreadsThrowsReferenceCountException() {
        int threads = 4;
        Queue<Future<?>> futures = new ArrayDeque<>(threads);
        ExecutorService service = Executors.newFixedThreadPool(threads);
        AtomicInteger refCountExceptions = new AtomicInteger();

        try {
            for (int i = 0; i < 10000; i++) {
                AbstractReferenceCounted referenceCounted = newReferenceCounted();
                CountdownEvent retainLatch = new CountdownEvent(1);
                Assert.True(referenceCounted.release());

                for (int a = 0; a < threads; a++) {
                    int retainCnt = ThreadLocalRandom.current().nextInt(1, int.MaxValue);
                    futures.add(service.submit(()() => {
                        try {
                            retainLatch.await();
                            try {
                                referenceCounted.retain(retainCnt);
                            } catch (IllegalReferenceCountException e) {
                                refCountExceptions.incrementAndGet();
                            }
                        } catch (ThreadInterruptedException e) {
                            Thread.CurrentThread.interrupt();
                        }
                    }));
                }
                retainLatch.countDown();

                for (;;) {
                    Future<?> f = futures.poll();
                    if (f == null) {
                        break;
                    }
                    f.get();
                }
                Assert.Equal(4, refCountExceptions.get());
                refCountExceptions.set(0);
            }
        } finally {
            service.shutdown();
        }
    }

    [Fact(Timeout = 30000)]
    public void testReleaseFromMultipleThreadsThrowsReferenceCountException() {
        int threads = 4;
        Queue<Future<?>> futures = new ArrayDeque<>(threads);
        ExecutorService service = Executors.newFixedThreadPool(threads);
        AtomicInteger refCountExceptions = new AtomicInteger();

        try {
            for (int i = 0; i < 10000; i++) {
                AbstractReferenceCounted referenceCounted = newReferenceCounted();
                CountdownEvent releaseLatch = new CountdownEvent(1);
                AtomicInteger releasedCount = new AtomicInteger();

                for (int a = 0; a < threads; a++) {
                    AtomicInteger releaseCnt = new AtomicInteger(0);

                    futures.add(service.submit(()() => {
                        try {
                            releaseLatch.await();
                            try {
                                if (referenceCounted.release(releaseCnt.incrementAndGet())) {
                                    releasedCount.incrementAndGet();
                                }
                            } catch (IllegalReferenceCountException e) {
                                refCountExceptions.incrementAndGet();
                            }
                        } catch (ThreadInterruptedException e) {
                            Thread.CurrentThread.interrupt();
                        }
                    }));
                }
                releaseLatch.countDown();

                for (;;) {
                    Future<?> f = futures.poll();
                    if (f == null) {
                        break;
                    }
                    f.get();
                }
                Assert.Equal(3, refCountExceptions.get());
                Assert.Equal(1, releasedCount.get());

                refCountExceptions.set(0);
            }
        } finally {
            service.shutdown();
        }
    }

    public static AbstractReferenceCounted newReferenceCounted() {
        return new AbstractReferenceCounted() {
            @Override
            protected void deallocate() {
                // NOOP
            }

            @Override
            public IReferenceCounted touch(Object hint) {
                return this;
            }
        };
    }
}
