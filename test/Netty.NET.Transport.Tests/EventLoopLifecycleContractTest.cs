/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independently owned loop cleanup.
public class EventLoopLifecycleContractTest
{
    [Fact(Timeout = 10000)]
    public async Task ATaskConsumerObservesRegistrationFailureEvenWhenTheOwnerHasStopped()
    {
        var loop = new DefaultEventLoop();
        try
        {
            await loop.StopAsync();
            var channel = new LocalChannel();
            var registration = loop.RegisterAsync(channel);
            // Ensure the listener was notified.
            // Native Task observation uses the CLR scheduler rather than a rejected channel-associated listener executor.
            var observed = registration.ContinueWith(task => task.IsFaulted && !loop.InEventLoop(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
            await Assert.ThrowsAsync<RejectedExecutionException>(() => registration);
            Assert.True(await observed.WaitAsync(TestContext.Current.CancellationToken));
            Assert.False(channel.IsOpen);
        }
        finally { await loop.StopAsync(); }
    }
    [Fact(Timeout = 10000)]
    public async Task PendingTasksIncludesTheTailQueueThroughTheParentType()
    {
        var loop = new SingleThreadEventLoopSourceTest.LoopB();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            loop.Execute(() =>
            {
                started.TrySetResult();
                while (!release.IsSet)
                    try { release.Wait(); } catch (ThreadInterruptedException) { }
            });
            await started.Task.WaitAsync(TestContext.Current.CancellationToken);
            Action tail = () => { };
            loop.Execute(() => { }); loop.ExecuteAfterEventLoopIteration(tail);
            SingleThreadEventExecutor parent = loop;
            Assert.Equal(2, parent.PendingTasks());
            Assert.True(loop.RemoveAfterEventLoopIterationTask(tail));
            Assert.Equal(1, parent.PendingTasks());
        }
        finally { release.Set(); await loop.StopAsync(); }
    }
    [Fact(Timeout = 10000)]
    public async Task ShutdownDrainsAcceptedTailWorkOnTheOwner()
    {
        var loop = new SingleThreadEventLoopSourceTest.LoopB();
        var ran = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        loop.ExecuteAfterEventLoopIteration(() => ran.TrySetResult(loop.InEventLoop()));
        try
        {
            await loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TestContext.Current.CancellationToken);
            Assert.True(await ran.Task); Assert.Equal(0, loop.PendingTasks());
        }
        finally { await loop.StopAsync(); }
    }
    [Fact(Timeout = 10000)]
    public async Task TailExceptionsDoNotSuppressLaterAcceptedWork()
    {
        var loop = new SingleThreadEventLoopSourceTest.LoopB();
        var ran = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            loop.ExecuteAfterEventLoopIteration(() => throw new InvalidOperationException("tail failure"));
            loop.ExecuteAfterEventLoopIteration(() => ran.TrySetResult(loop.InEventLoop()));
            loop.Execute(() => { });
            Assert.True(await ran.Task.WaitAsync(TestContext.Current.CancellationToken));
        }
        finally { await loop.StopAsync(); }
    }
}
