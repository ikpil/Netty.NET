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
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Concurrent;

public class UnorderedThreadPoolEventExecutorTest
{
    private sealed class Listener : IGenericFutureListener<IFuture<Void>>
    {
        private readonly Action<IFuture<Void>> action;
        internal Listener(Action<IFuture<Void>> action) => this.action = action;
        public void operationComplete(IFuture<Void> future) => action(future);
    }

    // See https://github.com/netty/netty/issues/6507
    [Fact]
    public void testNotEndlessExecute()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            // Having the first task wait on an exchanger allow us to make sure that the lister on the second task
            // is not added *after* the promise completes. We need to do this to prevent a race where the second task
            // and listener are completed before the DefaultPromise.NotifyListeners task get to run, which means our
            // queue inspection might observe this task after the CountDownLatch opens.
            // CLR Barrier retains the two-party rendezvous; the exchanged Void value is always null.
            using var exchanger = new Barrier(2);
            using var latch = new CountdownEvent(3);
            executor.execute(Runnables.Create(() =>
            {
                try { Assert.True(exchanger.SignalAndWait(TimeSpan.FromSeconds(5))); }
                catch (ThreadInterruptedException e) { throw new InvalidOperationException("interrupted", e); }
                latch.Signal();
            }));
            var future = executor.submit(Runnables.Create(() => latch.Signal()));
            future.addListener(new Listener(_ => latch.Signal()));
            Assert.True(exchanger.SignalAndWait(TimeSpan.FromSeconds(5)));
            Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
            future.syncUninterruptibly();

            // Now just check if the queue stays empty multiple times. This is needed as the submit to execute(...)
            // by DefaultPromise may happen in an async fashion
            for (int i = 0; i < 10000; i++) Assert.True(executor.getQueue().isEmpty());
        }
        finally { stop(executor); }
    }

    [Fact(Timeout = 10000)]
    public void scheduledAtFixedRateMustRunTaskRepeatedly()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var latch = new CountdownEvent(3);
        var future = executor.scheduleAtFixedRate(Runnables.Create(() =>
        {
            // CLR CountdownEvent throws if already zero; Java CountDownLatch ignores further decrements.
            if (!latch.IsSet) latch.Signal();
        }), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));
        try { Assert.True(latch.Wait(TimeSpan.FromSeconds(5))); }
        finally { future.cancel(true); stop(executor); }
    }

    [Fact]
    public void testGetReturnsCorrectValueOnSuccess()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            const string expected = "expected";
            var future = executor.submit(new AnonymousCallable<string>(() => expected));
            Assert.Equal(expected, future.get(TimeSpan.FromSeconds(5)));
        }
        finally { stop(executor); }
    }

    [Fact]
    public void testGetReturnsCorrectValueOnFailure()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            var cause = new InvalidOperationException();
            var future = executor.submit(new AnonymousCallable<string>(() => throw cause));
            Assert.True(future.await(TimeSpan.FromSeconds(5)));
            Assert.Same(cause, future.cause());
        }
        finally { stop(executor); }
    }

    [Fact]
    public void tasksRunningInUnorderedExecutorAreInEventLoop()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            var future = executor.submit(new AnonymousCallable<bool>(() => executor.inEventLoop()));
            Assert.True(future.get(TimeSpan.FromSeconds(5)));
        }
        finally { stop(executor); }
    }

    private static void stop(UnorderedThreadPoolEventExecutor executor)
    {
        executor.shutdownGracefully();
        if (!executor.awaitTermination(TimeSpan.FromSeconds(5))) executor.shutdownNow();
        Assert.True(executor.awaitTermination(TimeSpan.FromSeconds(5)));
    }
}
