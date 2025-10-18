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
namespace Netty.Common.Tests.Concurrent
{
    public class UnorderedThreadPoolEventExecutorTest {

        // See https://github.com/netty/netty/issues/6507
        [Fact]
        public void testNotEndlessExecute() {
            UnorderedThreadPoolEventExecutor executor = new UnorderedThreadPoolEventExecutor(1);

            try {
            // Having the first task wait on an exchanger allow us to make sure that the lister on the second task
            // is not added *after* the promise completes. We need to do this to prevent a race where the second task
            // and listener are completed before the DefaultPromise.NotifyListeners task get to run, which means our
            // queue inspection might observe this task after the CountdownEvent opens.
            final Exchanger<Void> exchanger = new Exchanger<Void>();
            final CountdownEvent latch = new CountdownEvent(3);
            IRunnable task = new IRunnable() {
            @Override
            public void run() {
            try {
                exchanger.exchange(null);
            } catch (ThreadInterruptedException e) {
                throw new Exception(e);
            }
            latch.countDown();
        }
    };
    executor.execute(task);
    Future<?> future = executor.submit(new IRunnable() {
        @Override
        public void run() {
        latch.countDown();
    }
}

}).addListener(new FutureListener<Object>() {
                @Override
                public void operationComplete(Future<Object> future) {
                    latch.countDown();
                }
            });
            exchanger.exchange(null);
            latch.await();
            future.syncUninterruptibly();

            // Now just check if the queue stays empty multiple times. This is needed as the submit to execute(...)
            // by DefaultPromise may happen in an async fashion
            for (int i = 0; i < 10000; i++) {
                Assert.True(executor.getQueue().isEmpty());
            }
        } finally {
            executor.shutdownGracefully();
        }
    }

    [Fact]
    @Timeout(value = 10000, unit = TimeUnit.MILLISECONDS)
    public void scheduledAtFixedRateMustRunTaskRepeatedly() throws ThreadInterruptedException {
        UnorderedThreadPoolEventExecutor executor = new UnorderedThreadPoolEventExecutor(1);
        final CountdownEvent latch = new CountdownEvent(3);
        Future<?> future = executor.scheduleAtFixedRate(new IRunnable() {
            @Override
            public void run() {
                latch.countDown();
            }
        }, 1, 1, TimeUnit.MILLISECONDS);
        try {
            latch.await();
        } finally {
            future.cancel(true);
            executor.shutdownGracefully();
        }
    }

    [Fact]
    public void testGetReturnsCorrectValueOnSuccess() {
        UnorderedThreadPoolEventExecutor executor = new UnorderedThreadPoolEventExecutor(1);
        try {
            final string expected = "expected";
            Future<string> f = executor.submit(new Callable<string>() {
                @Override
                public string call() {
                    return expected;
                }
            });

            Assert.Equal(expected, f.get());
        } finally {
            executor.shutdownGracefully();
        }
    }

    [Fact]
    public void testGetReturnsCorrectValueOnFailure() {
        UnorderedThreadPoolEventExecutor executor = new UnorderedThreadPoolEventExecutor(1);
        try {
            final Exception cause = new Exception();
            Future<string> f = executor.submit(new Callable<string>() {
                @Override
                public string call() {
                    throw cause;
                }
            });

            Assert.Same(cause, f.await().cause());
        } finally {
            executor.shutdownGracefully();
        }
    }

    [Fact]
    void tasksRunningInUnorderedExecutorAreInEventLoop() {
        UnorderedThreadPoolEventExecutor executor = new UnorderedThreadPoolEventExecutor(1);
        try {
            Future<Boolean> future = executor.submit(()() => executor.inEventLoop());
            Assert.True(future.get());
        } finally {
            executor.shutdownGracefully();
        }
    }
}
