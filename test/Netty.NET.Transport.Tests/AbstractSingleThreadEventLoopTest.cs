/*
 * Copyright 2019 The Netty Project
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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable CS0612, CS0618, xUnit1031, xUnit1051 // Original deprecated shutdown(), blocking sync() and fire-and-forget group shutdowns.
// CLR: the source testsuite runs its classes one after another (see AbstractTestsuiteTest); this plain JUnit fixture
// joins the same collection so its 100 ms auto-scaling windows are not measured while other classes load the host.
[Collection("io.netty.testsuite.transport")]
public abstract class AbstractSingleThreadEventLoopTest
{
    protected const int SCALING_MIN_THREADS = 1;
    protected const int SCALING_MAX_THREADS = 2;
    protected const long SCALING_WINDOW_SECONDS = 100;
    // CLR: TimeUnit is replaced by TimeSpan (JVM-only API family). MILLISECONDS is one millisecond, so the factory's
    // TimeSpan window SCALING_WINDOW_UNIT * SCALING_WINDOW_SECONDS is the source's (100, MILLISECONDS) pair.
    protected static readonly TimeSpan SCALING_WINDOW_UNIT = TimeSpan.FromMilliseconds(1);
    protected const double SCALEDOWN_THRESHOLD = 0.2;
    protected const double SCALEUP_THRESHOLD = 0.9;
    protected const int SCALING_PATIENCE_CYCLES = 1;

    protected static readonly IEventExecutorChooserFactory AUTO_SCALING_CHOOSER_FACTORY =
            new AutoScalingEventExecutorChooserFactory(SCALING_MIN_THREADS, SCALING_MAX_THREADS,
                                                       SCALING_WINDOW_UNIT * SCALING_WINDOW_SECONDS,
                                                       SCALEDOWN_THRESHOLD, SCALEUP_THRESHOLD,
                                                       SCALING_MAX_THREADS, SCALING_MAX_THREADS, SCALING_PATIENCE_CYCLES
    );

    [Fact(Timeout = 5000)]
    public async Task TestChannelsRegistered()
    {
        IEventLoopGroup group = NewEventLoopGroup();
        SingleThreadEventLoop loop = (SingleThreadEventLoop) group.Next();

        try
        {
            IChannel ch1 = NewChannel();
            IChannel ch2 = NewChannel();

            int rc = await RegisteredChannels(loop);
            bool channelCountSupported = rc != -1;

            if (channelCountSupported)
            {
                Assert.Equal(0, await RegisteredChannels(loop));
            }

            // CLR: syncUninterruptibly() rethrows a failure and returns the future; awaiting the Task (bounded by the
            // test timeout's token) does the same, and isSuccess() is IsCompletedSuccessfully.
            Task future = loop.RegisterAsync(ch1);
            await future.WaitAsync(TestContext.Current.CancellationToken);
            Assert.True(future.IsCompletedSuccessfully);
            future = loop.RegisterAsync(ch2);
            await future.WaitAsync(TestContext.Current.CancellationToken);
            Assert.True(future.IsCompletedSuccessfully);
            if (channelCountSupported)
            {
                await CheckNumRegisteredChannels(loop, 2);
            }

            future = ch1.DeregisterAsync();
            await future.WaitAsync(TestContext.Current.CancellationToken);
            Assert.True(future.IsCompletedSuccessfully);
            if (channelCountSupported)
            {
                await CheckNumRegisteredChannels(loop, 1);
            }
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private static async Task CheckNumRegisteredChannels(SingleThreadEventLoop loop, int numChannels)
    {
        // We need to loop as some EventLoop implementations may need some time to update the counter correctly.
        while (await RegisteredChannels(loop) != numChannels)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    // Only reliable if run from event loop
    private static Task<int> RegisteredChannels(SingleThreadEventLoop loop)
    {
        // CLR: submit(Callable).get(1, SECONDS) is SubmitAsync bounded by WaitAsync, which throws TimeoutException
        // where Future.get throws java.util.concurrent.TimeoutException.
        return loop.SubmitAsync(() => loop.RegisteredChannels()).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ShutdownBeforeStart()
    {
        IEventLoopGroup group = NewEventLoopGroup();
        Assert.False(group.AwaitTermination(TimeSpan.FromMilliseconds(2)));
        group.Shutdown();
        Assert.True(group.AwaitTermination(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task ShutdownGracefullyZeroQuietBeforeStart()
    {
        IEventLoopGroup group = NewEventLoopGroup();
        // CLR: Future.await(200) reports whether the future completed, successfully or not, within 200 ms;
        // Task.WhenAny returns the shutdown Task only when it completed before the 200 ms delay.
        Task future = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.Same(future, await Task.WhenAny(future, Task.Delay(200, TestContext.Current.CancellationToken)));
    }

    // Copied from AbstractEventLoopTest
    [Fact(Timeout = 5000)]
    public async Task TestShutdownGracefullyNoQuietPeriod()
    {
        IEventLoopGroup loop = NewEventLoopGroup();
        ServerBootstrap b = new ServerBootstrap();
        b.Group(loop)
        .Channel(ServerChannelClass())
        .ChildHandler(new ChannelInboundHandlerAdapter());

        // Not close the Channel to ensure the EventLoop is still shutdown in time.
        Task<IChannel> cf = ServerChannelClass() == typeof(LocalServerChannel)
                ? b.BindAsync(new LocalAddress(typeof(AbstractSingleThreadEventLoopTest))) : b.BindAsync(0);
        await cf.WaitAsync(TestContext.Current.CancellationToken);

        Task f = loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromMinutes(1));
        Assert.True(loop.AwaitTermination(TimeSpan.FromMilliseconds(600)));
        await f;
        Assert.True(f.IsCompletedSuccessfully);
        Assert.True(loop.IsShutdown());
        Assert.True(loop.IsTerminated());
    }

    [Fact]
    public async Task ShutdownGracefullyBeforeStart()
    {
        IEventLoopGroup group = NewEventLoopGroup();
        // CLR: Future.await(500) as in ShutdownGracefullyZeroQuietBeforeStart.
        Task future = group.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(1000));
        Assert.Same(future, await Task.WhenAny(future, Task.Delay(500, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void GracefulShutdownAfterStart()
    {
        IEventLoopGroup group = NewEventLoopGroup();
        IEventLoop loop = group.Next();
        try
        {
            // CLR: CountdownEvent replaces CountDownLatch.
            using CountdownEvent latch = new CountdownEvent(1);
            loop.Execute(() => latch.Signal());

            // Wait for the event loop thread to start.
            latch.Wait(TestContext.Current.CancellationToken);

            // Request the event loop thread to stop.
            _ = loop.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(3000));

            // Wait until the event loop is terminated.
            Assert.True(loop.AwaitTermination(TimeSpan.FromMilliseconds(500)));

            AssertRejection(loop);
        }
        finally
        {
            // CLR: the source leaves the group's other children open. They never start a thread, but each I/O child
            // holds its NioIoHandler selector sockets, so the port shuts the group down after the assertions.
            _ = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        }
    }

    [Fact(Timeout = 3000)]
    public async Task TestChannelsIteratorEmpty()
    {
        // CLR: assumeTrue aborts the test as skipped; xUnit v3 Assert.SkipUnless does the same.
        Assert.SkipUnless(SupportsChannelIteration(), "Assumption failed: supportsChannelIteration()");
        IEventLoopGroup group = NewEventLoopGroup();
        SingleThreadEventLoop loop = (SingleThreadEventLoop) group.Next();
        try
        {
            await RunBlockingOn(loop, () =>
            {
                // CLR: registeredChannelsIterator() returns IEnumerable<IChannel>, as IEventExecutorGroup.Iterator()
                // does for EventExecutorGroup.iterator(); its IEnumerator's MoveNext()/Current carry hasNext()/next().
                using IEnumerator<IChannel> iterator = loop.RegisteredChannelsIterator().GetEnumerator();

                Assert.False(iterator.MoveNext());
                // CLR: next() past the end throws NoSuchElementException. IEnumerator<T>.Current is undefined there,
                // and the documented end-of-sequence contract is that MoveNext keeps returning false
                // (https://learn.microsoft.com/en-us/dotnet/api/system.collections.ienumerator.movenext).
                Assert.False(iterator.MoveNext());
            }).WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact(Timeout = 3000)]
    public async Task TestChannelsIterator()
    {
        Assert.SkipUnless(SupportsChannelIteration(), "Assumption failed: supportsChannelIteration()");
        IEventLoopGroup group = NewEventLoopGroup();
        SingleThreadEventLoop loop = (SingleThreadEventLoop) group.Next();
        try
        {
            IChannel ch1 = NewChannel();
            IChannel ch2 = NewChannel();
            await loop.RegisterAsync(ch1);
            await loop.RegisterAsync(ch2);
            Assert.Equal(2, await RegisteredChannels(loop));

            await RunBlockingOn(loop, () =>
            {
                // CLR: IEnumerator over the IEnumerable<IChannel>, as in TestChannelsIteratorEmpty.
                using IEnumerator<IChannel> iterator = loop.RegisteredChannelsIterator().GetEnumerator();

                Assert.True(iterator.MoveNext());
                IChannel actualCh1 = iterator.Current;
                Assert.NotNull(actualCh1);

                Assert.True(iterator.MoveNext());
                IChannel actualCh2 = iterator.Current;
                Assert.NotNull(actualCh2);

                HashSet<IChannel> expected = new HashSet<IChannel>(4);
                expected.Add(ch1);
                expected.Add(ch2);
                expected.Remove(actualCh1);
                expected.Remove(actualCh2);
                Assert.True(expected.Count == 0);

                Assert.False(iterator.MoveNext());
                // CLR: next() past the end, as in TestChannelsIteratorEmpty.
                Assert.False(iterator.MoveNext());
            }).WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    // CLR: testChannelsIteratorRemoveThrows is not ported. It asserts Iterator.remove() throws
    // UnsupportedOperationException; IEnumerator<T> has no remove member, so the returned sequence is read-only by
    // its type. The disposition is recorded in the Transport manifest.

    [Fact]
    public async Task SchedulingAndCancellingTasks()
    {
        Action runnable = () =>
        {
        };
        // CLR: a ScheduledFuture is cancelled through its own cancel(false); a scheduled Task is cancelled through the
        // CancellationToken passed to the scheduling call, so each entry keeps its CancellationTokenSource.
        List<(Task Future, CancellationTokenSource Cancellation)> tasks = new List<(Task, CancellationTokenSource)>();
        IEventLoopGroup group = NewEventLoopGroup();
        try
        {
            IEventLoop eventLoop = group.Next();
            for (int i = 0; i < 5000; i++)
            {
                CancellationTokenSource cancellation = new CancellationTokenSource();
                tasks.Add((eventLoop.ScheduleAtFixedRateAsync(runnable, TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(1), cancellation.Token), cancellation));
                if (tasks.Count > 500)
                {
                    // CLR: Random.Shared replaces PlatformDependent.threadLocalRandom().
                    tasks[Random.Shared.Next(tasks.Count)].Cancellation.Cancel();
                }
            }
            foreach (var task in tasks)
            {
                task.Cancellation.Cancel();
            }
            foreach (var task in tasks)
            {
                // CLR: Future.await() waits without rethrowing; Task.WhenAny completes when the Task does.
                await Task.WhenAny(task.Future);
            }
            foreach (var task in tasks)
            {
                if (!task.Future.IsCanceled)
                {
                    await task.Future;
                }
            }
        }
        finally
        {
            foreach (var task in tasks)
            {
                task.Cancellation.Dispose();
            }
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestAutoScalingEventLoopGroupCanScaleDownAndBeUsed()
    {
        IEventLoopGroup group = NewAutoScalingEventLoopGroup();
        if (group == null)
        {
            return;
        }
        try
        {
            StartAllExecutors(group);
            AssertEquals(SCALING_MAX_THREADS, CountActiveExecutors(group),
                         "Group should start with max threads active.");

            // CLR: Stopwatch timestamps replace System.nanoTime(); TimeUnit.SECONDS.toNanos(5) is five seconds of
            // Stopwatch.Frequency ticks.
            long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
            while (CountActiveExecutors(group) > SCALING_MIN_THREADS && Stopwatch.GetTimestamp() < deadline)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            AssertEquals(SCALING_MIN_THREADS, CountActiveExecutors(group),
                         "Group did not scale down to min threads in time.");
        }
        finally
        {
            await group.ShutdownGracefullyAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task TestSubmittingTaskWakesUpSuspendedExecutor()
    {
        IEventLoopGroup group = NewAutoScalingEventLoopGroup();
        if (group == null)
        {
            return;
        }
        try
        {
            StartAllExecutors(group);
            AssertEquals(SCALING_MAX_THREADS, CountActiveExecutors(group),
                         "Group should start with max threads.");

            // CLR: TimeUnit.SECONDS.toNanos(10) as Stopwatch ticks, as in the test above.
            long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
            while (CountActiveExecutors(group) > SCALING_MIN_THREADS && Stopwatch.GetTimestamp() < deadline)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            AssertEquals(SCALING_MIN_THREADS, CountActiveExecutors(group),
                         "Group did not scale down to min threads in time.");

            IEventLoop suspendedLoop = null;
            foreach (IEventExecutor exec in group.Iterator())
            {
                if (exec.IsSuspended())
                {
                    suspendedLoop = (IEventLoop) exec;
                    break;
                }
            }
            Assert.True(suspendedLoop != null, "Could not find a suspended executor to test.");

            // Submit a task directly to the suspended loop, this should trigger the wake-up mechanism.
            int i = 0;
            bool isSuspended;
            do
            {
                Task future = suspendedLoop.SubmitAsync(() => { });
                // CLR: future.sync() blocks the test thread, which resumes as soon as the task completes. An awaited
                // continuation is queued to the thread pool instead, and under full-suite load it can start more than
                // the 100 ms scaling window later, when the monitor has already suspended the other idle executor.
                // Blocking keeps the source's check right after the task.
                future.Wait(TestContext.Current.CancellationToken);
            } while ((isSuspended = suspendedLoop.IsSuspended()) && i++ < 10);

            Assert.False(isSuspended, "Executor should wake up after task submission.");
            AssertEquals(SCALING_MAX_THREADS, CountActiveExecutors(group),
                         "Active executor count should increase after wake-up.");
        }
        finally
        {
            await group.ShutdownGracefullyAsync();
        }
    }

    // CLR: xUnit's Assert.Equal has no message overload for integers; this keeps the source's
    // assertEquals(expected, actual, message) diagnostic.
    private static void AssertEquals(int expected, int actual, string message)
    {
        Assert.True(expected == actual, message + " ==> expected: <" + expected.ToString(CultureInfo.InvariantCulture) +
                "> but was: <" + actual.ToString(CultureInfo.InvariantCulture) + ">");
    }

    private static int CountActiveExecutors(IEventLoopGroup group)
    {
        int activeCount = 0;
        // CLR: EventExecutorGroup is Iterable<EventExecutor>; IEventExecutorGroup.Iterator() is that sequence.
        foreach (IEventExecutor executor in group.Iterator())
        {
            if (!executor.IsSuspended())
            {
                activeCount++;
            }
        }
        return activeCount;
    }

    private static void StartAllExecutors(IEventLoopGroup group)
    {
        // CLR: CountdownEvent replaces CountDownLatch.
        using CountdownEvent startLatch = new CountdownEvent(SCALING_MAX_THREADS);
        foreach (IEventExecutor executor in group.Iterator())
        {
            executor.Execute(() => startLatch.Signal());
        }
        startLatch.Wait(TestContext.Current.CancellationToken);
    }

    private static async Task RunBlockingOn(IEventLoop eventLoop, Action action)
    {
        // CLR: a TaskCompletionSource replaces eventLoop.newPromise(); its continuations leave the event loop thread.
        TaskCompletionSource promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            eventLoop.Execute(() =>
            {
                try
                {
                    action();
                    promise.SetResult();
                }
                catch (Exception t)
                {
                    promise.TrySetException(t);
                }
            });
        // CLR: awaiting the promise rethrows its cause. C# has no checked exceptions, so the cause is rethrown as is
        // where the source wraps a non-RuntimeException cause in RuntimeException; there is no thread interruption.
        await promise.Task;
    }

    private static readonly Action NOOP = () => { };

    private static void AssertRejection(IEventExecutor loop)
    {
        Assert.Throws<RejectedExecutionException>(() => loop.Execute(NOOP));
    }

    protected virtual bool SupportsChannelIteration()
    {
        return false;
    }
    protected abstract IEventLoopGroup NewEventLoopGroup();
    protected abstract IEventLoopGroup NewAutoScalingEventLoopGroup();
    protected abstract IChannel NewChannel();
    // CLR: Class<? extends ServerChannel> is a Type; the annotation lets ServerBootstrap.Channel(Type) keep the
    // parameterless constructor under trimming.
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    protected abstract Type ServerChannelClass();
}
