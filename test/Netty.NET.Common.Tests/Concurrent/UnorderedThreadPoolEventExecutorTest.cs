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
using System.Threading.Tasks;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Concurrent;

public class UnorderedThreadPoolEventExecutorTest
{
    // See https://github.com/netty/netty/issues/6507
    [Fact]
    public void TestNotEndlessExecute()
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
            executor.Execute(() =>
            {
                try { Assert.True(exchanger.SignalAndWait(TimeSpan.FromSeconds(5))); }
                catch (ThreadInterruptedException e) { throw new InvalidOperationException("interrupted", e); }
                latch.Signal();
            });
            var future = executor.SubmitAsync(() => latch.Signal());
            using var completion = new ExecutorCompletion(executor, future);
            using var listener = completion.Register(_ => latch.Signal());
            Assert.True(exchanger.SignalAndWait(TimeSpan.FromSeconds(5)));
            Assert.True(latch.Wait(TimeSpan.FromSeconds(5)));
            future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            listener.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

            // Now just check if the queue stays empty multiple times. This is needed as the submit to execute(...)
            // by DefaultPromise may happen in an async fashion
            for (int i = 0; i < 10000; i++) Assert.Equal(0, executor.PendingTaskCount);
        }
        finally { Stop(executor); }
    }

    [Fact(Timeout = 10000)]
    public void ScheduledAtFixedRateMustRunTaskRepeatedly()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var latch = new CountdownEvent(3);
        using var cancellation = new CancellationTokenSource();
        var future = executor.ScheduleAtFixedRateAsync(() =>
        {
            // CLR CountdownEvent throws if already zero; Java CountDownLatch ignores further decrements.
            if (!latch.IsSet) latch.Signal();
        }, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), cancellation.Token);
        try { Assert.True(latch.Wait(TimeSpan.FromSeconds(5))); }
        finally { cancellation.Cancel(); Stop(executor); }
    }

    [Fact]
    public void TestGetReturnsCorrectValueOnSuccess()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            const string expected = "expected";
            var future = executor.SubmitAsync<string>(() => expected);
            Assert.Equal(expected, future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally { Stop(executor); }
    }

    [Fact]
    public void TestGetReturnsCorrectValueOnFailure()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            var cause = new InvalidOperationException();
            var future = executor.SubmitAsync(string () => throw cause);
            Assert.Same(cause, Assert.Throws<InvalidOperationException>(() =>
                future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()));
        }
        finally { Stop(executor); }
    }

    [Fact]
    public void TasksRunningInUnorderedExecutorAreInEventLoop()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            var future = executor.SubmitAsync<bool>(() => executor.InEventLoop());
            Assert.True(future.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally { Stop(executor); }
    }

    private static void Stop(UnorderedThreadPoolEventExecutor executor)
    {
        executor.ShutdownGracefullyAsync();
        if (!executor.AwaitTermination(TimeSpan.FromSeconds(5))) _ = executor.StopAsync();
        Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
    }
}
