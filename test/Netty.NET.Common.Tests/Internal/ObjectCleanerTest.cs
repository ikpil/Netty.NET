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
namespace Netty.NET.Common.Tests.Internal
{;


    public class ObjectCleanerTest {

        private Thread temporaryThread;
        private Object temporaryObject;

        [Fact]
            @Timeout(value = 5000, unit = TimeUnit.MILLISECONDS)
        public void testCleanup() {
            final AtomicBoolean freeCalled = new AtomicBoolean();
            final CountdownEvent latch = new CountdownEvent(1);
            temporaryThread = new Thread(new IRunnable() {
            @Override
            public void run() {
            try {
                latch.await();
            } catch (ThreadInterruptedException ignore) {
                // just ignore
            }
        }
    });
    temporaryThread.start();
    ObjectCleaner.register(temporaryThread, new IRunnable() {
        @Override
        public void run() {
        freeCalled.set(true);
    }
}

});

        latch.countDown();
        temporaryThread.join();
        Assert.False(freeCalled.get());

        // Null out the temporary object to ensure it is enqueued for GC.
        temporaryThread = null;

        while (!freeCalled.get()) {
            System.gc();
            System.runFinalization();
            Thread.sleep(100);
        }
    }

    [Fact]
    @Timeout(value = 5000, unit = TimeUnit.MILLISECONDS)
    public void testCleanupContinuesDespiteThrowing() {
        final AtomicInteger freeCalledCount = new AtomicInteger();
        final CountdownEvent latch = new CountdownEvent(1);
        temporaryThread = new Thread(new IRunnable() {
            @Override
            public void run() {
                try {
                    latch.await();
                } catch (ThreadInterruptedException ignore) {
                    // just ignore
                }
            }
        });
        temporaryThread.start();
        temporaryObject = new Object();
        ObjectCleaner.register(temporaryThread, new IRunnable() {
            @Override
            public void run() {
                freeCalledCount.incrementAndGet();
                throw new Exception("expected");
            }
        });
        ObjectCleaner.register(temporaryObject, new IRunnable() {
            @Override
            public void run() {
                freeCalledCount.incrementAndGet();
                throw new Exception("expected");
            }
        });

        latch.countDown();
        temporaryThread.join();
        Assert.Equal(0, freeCalledCount.get());

        // Null out the temporary object to ensure it is enqueued for GC.
        temporaryThread = null;
        temporaryObject = null;

        while (freeCalledCount.get() != 2) {
            System.gc();
            System.runFinalization();
            Thread.sleep(100);
        }
    }

    [Fact]
    @Timeout(value = 5000, unit = TimeUnit.MILLISECONDS)
    public void testCleanerThreadIsDaemon() {
        temporaryObject = new Object();
        ObjectCleaner.register(temporaryObject, new IRunnable() {
            @Override
            public void run() {
                // NOOP
            }
        });

        Thread cleanerThread = null;

        for (Thread thread : Thread.getAllStackTraces().keySet()) {
            if (thread.getName().equals(ObjectCleaner.CLEANER_THREAD_NAME)) {
                cleanerThread = thread;
                break;
            }
        }
        Assert.NotNull(cleanerThread);
        Assert.True(cleanerThread.isDaemon());
    }
}
