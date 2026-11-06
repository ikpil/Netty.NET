/*
 * Copyright 2025 The Netty Project
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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

public class MultiThreadIoEventLoopGroupSourceTest
{
    [Fact(Timeout = 30000)]
    public async Task TestScalingWithIoRegistrationLifecycle()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var group = NewGroup();
        using var workload = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try
        {
            await StartAllExecutors(group, cancellation);
            Assert.Equal(2, CountActiveExecutors(group));

            await WaitForSuspension(group, 1, cancellation);
            Assert.Equal(1, CountActiveExecutors(group));
            var active = Assert.IsType<DefaultEventLoop>(FindActiveExecutor(group));

            var registration = await active.RegisterIoAsync(new TestIoHandle(), cancellation);
            StartWorkload(active, workload.Token);
            await Task.Delay(450, cancellation);

            // The executor will not be suspended because its workload generates utilization.
            Assert.False(active.IsSuspended(), "Executor with active handle should not be suspended");

            workload.Cancel();
            await CancelRegistration(registration, active, cancellation);
            await WaitForSuspension(group, 2, cancellation);
            Assert.True(active.IsSuspended(), "Executor should suspend after handle is cancelled and idle");
        }
        finally
        {
            workload.Cancel();
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestShouldNotSuspendExecutorWithActiveRegistration()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var group = NewGroup();
        try
        {
            await StartAllExecutors(group, cancellation);
            await WaitForSuspension(group, 1, cancellation);
            Assert.Equal(1, CountActiveExecutors(group));
            var active = Assert.IsType<DefaultEventLoop>(FindActiveExecutor(group));

            // Register a handle with the active executor. This makes registeredChannels() return 1.
            var registration = await active.RegisterIoAsync(new TestIoHandle(), cancellation);
            Assert.Equal(1, active.RegisteredIoHandles);

            // The executor is now a candidate for suspension based on utilization,
            // but not based on registered channels.
            // Wait for a few monitor cycles. The monitor will run and see the executor is idle.
            await Task.Delay(600, cancellation);

            Assert.False(active.IsSuspended(),
                "Executor with an active registration should not be suspended, even if idle");
            Assert.Equal(1, CountActiveExecutors(group));

            await CancelRegistration(registration, active, cancellation);
            await WaitForSuspension(group, 2, cancellation);
            Assert.True(active.IsSuspended(), "Executor should suspend after registration is cancelled and it remains idle");
        }
        finally { await group.StopAsync(); }
    }

    internal static DefaultEventLoopGroup NewGroup(int minimum = 0, IThreadFactory threadFactory = null) =>
        new(2, threadFactory, new AutoScalingEventExecutorChooserFactory(
            minimum, 2, TimeSpan.FromMilliseconds(200), 0.4, 0.8, 2, 1, 1));

    internal static void StartWorkload(IEventExecutor executor, CancellationToken cancellation)
    {
        executor.Execute(Work);

        void Work()
        {
            if (cancellation.IsCancellationRequested) return;
            Thread.Sleep(100);
            if (!cancellation.IsCancellationRequested) executor.Execute(Work);
        }
    }

    internal static async Task StartAllExecutors(DefaultEventLoopGroup group, CancellationToken cancellation)
    {
        var started = group.Iterator().Select(executor => executor.SubmitAsync(() => { /* NO-OP */ }, cancellation));
        await Task.WhenAll(started).WaitAsync(TimeSpan.FromSeconds(2), cancellation);
    }

    internal static int CountActiveExecutors(DefaultEventLoopGroup group) =>
        group.Iterator().Count(executor => !executor.IsSuspended());

    internal static IEventExecutor FindActiveExecutor(DefaultEventLoopGroup group) =>
        group.Iterator().FirstOrDefault(executor => !executor.IsSuspended());

    internal static async Task WaitForSuspension(DefaultEventLoopGroup group, int count, CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 2000)
        {
            if (group.ExecutorCount() - CountActiveExecutors(group) >= count) return;
            await Task.Delay(50, cancellation);
        }
        Assert.Fail($"Timed out waiting for {count} executor(s) to suspend.");
    }

    private static async Task CancelRegistration(IoRegistration registration, DefaultEventLoop owner, CancellationToken cancellation)
    {
        Assert.True(registration.IsValid);
        Assert.True(await registration.CancelAsync().WaitAsync(cancellation));
        Assert.False(registration.IsValid);
        Assert.Equal(0, owner.RegisteredIoHandles);
    }

    private sealed class TestIoHandle : IIoHandle
    {
        public void HandleCompletion(IoRegistration registration, object completion) { }
        public void Dispose() { }
    }

    internal static async Task Cleanup(DefaultEventLoopGroup group, LocalChannel channel)
    {
        try { if (channel.EventLoop != null) await channel.CloseAsync(); }
        finally { await group.StopAsync(); }
    }
}
