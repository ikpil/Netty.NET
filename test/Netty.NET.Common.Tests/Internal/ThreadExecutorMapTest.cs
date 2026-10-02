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
using System.Threading.Tasks;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

public class ThreadExecutorMapTest
{
    class TestEventExecutor : AbstractEventExecutor
    {
        public override void shutdown()
        {
            throw new NotSupportedException();
        }

        public override bool inEventLoop(Thread thread)
        {
            return false;
        }

        public override bool isShuttingDown()
        {
            return false;
        }

        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
        {
            throw new NotSupportedException();
        }

        public override Task Termination => throw new NotSupportedException();

        public override bool isShutdown()
        {
            return false;
        }

        public override bool isTerminated()
        {
            return false;
        }

        public override bool awaitTermination(TimeSpan timeout)
        {
            return false;
        }

        public override void execute(IRunnable command)
        {
            throw new NotSupportedException();
        }
    }

    private static readonly IEventExecutor EVENT_EXECUTOR = new TestEventExecutor();

    [Fact]
    public void testOldExecutorIsRestored()
    {
        IExecutor executor = ThreadExecutorMap.apply(ImmediateExecutor.INSTANCE, ImmediateEventExecutor.INSTANCE);
        IExecutor executor2 = ThreadExecutorMap.apply(ImmediateExecutor.INSTANCE, EVENT_EXECUTOR);
        executor.execute(Runnables.Create(() =>
        {
            executor2.execute(Runnables.Create(() =>
            {
                Assert.Same(EVENT_EXECUTOR, ThreadExecutorMap.currentExecutor());
            }));

            Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.currentExecutor());
        }));
    }

    [Fact]
    public void testDecorateExecutor()
    {
        IExecutor executor = ThreadExecutorMap.apply(ImmediateExecutor.INSTANCE, ImmediateEventExecutor.INSTANCE);
        executor.execute(Runnables.Create(() =>
        {
            Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.currentExecutor());
        }));
    }

    [Fact]
    public void testDecorateRunnable()
    {
        ThreadExecutorMap.apply(Runnables.Create(() =>
        {
            Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.currentExecutor());
        }), ImmediateEventExecutor.INSTANCE).run();
    }

    [Fact]
    public void testDecorateThreadFactory()
    {
        IThreadFactory threadFactory = ThreadExecutorMap.apply(new DefaultThreadFactory("thread-executor-map-test"),
            ImmediateEventExecutor.INSTANCE);
        Exception failure = null;
        Thread thread = threadFactory.newThread(Runnables.Create(() =>
        {
            try { Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.currentExecutor()); }
            catch (Exception exception) { failure = exception; }
        }));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }
}
