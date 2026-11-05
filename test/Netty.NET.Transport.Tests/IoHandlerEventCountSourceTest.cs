/*
 * Copyright 2026 The Netty Project
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
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.ManualIoEventLoopSourceTest;

namespace Netty.NET.Transport.Tests;

/**
 * Verifies that {@link IoHandler#run(IoHandlerContext)} returns only the count of real I/O events,
 * excluding internal events such as wakeups and timer expirations.
 * <p>
 * Subclasses provide the {@link IoHandlerFactory} for each transport. The contract tested here
 * must hold regardless of the transport implementation.
 */
public class IoHandlerEventCountSourceTest
{
    [Fact(Timeout = 5000)]
    public void TestTimerExpirationDoesNotCountAsIoEvent()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        try
        {
            Assert.Equal(0, loop.Run(100_000_000, -1));
        }
        finally { Terminate(loop); }
    }

    [Fact(Timeout = 5000)]
    public void TestWakeupDoesNotCountAsIoEvent()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        using var watchdog = cancellation.Register(() => _ = loop.StopAsync());
        using var aboutToBlock = new ManualResetEventSlim();
        var waker = new Thread(() =>
        {
            aboutToBlock.Wait();
            Thread.Sleep(300);
            loop.Wakeup();
        }) { IsBackground = true };
        waker.Start();
        try
        {
            aboutToBlock.Set();
            Assert.Equal(0, loop.Run(0, -1));
            cancellation.ThrowIfCancellationRequested();
            Assert.True(waker.Join(TimeSpan.FromSeconds(1)));
        }
        finally { Terminate(loop); }
    }

    [Fact(Timeout = 5000)]
    public void TestScheduledTaskDoesNotInflateIoCount()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        using var watchdog = cancellation.Register(() => _ = loop.StopAsync());
        try
        {
            bool ran = false;
            var task = loop.ScheduleAsync(() => ran = true, TimeSpan.FromMilliseconds(100), cancellation);

            // Block until the scheduled task fires and is picked up by runAllTasks.
            // The handler may return slightly before the task deadline due to timer precision,
            // so we loop until the task actually executes.
            int result = 0;
            while (!ran)
            {
                cancellation.ThrowIfCancellationRequested();
                result += loop.Run(0);
            }
            Assert.True(task.IsCompletedSuccessfully);
            Assert.Equal(1, result);
        }
        finally { Terminate(loop); }
    }
}
