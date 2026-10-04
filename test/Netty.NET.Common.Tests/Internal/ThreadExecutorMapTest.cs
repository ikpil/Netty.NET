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
        public override void Shutdown()
        {
            throw new NotSupportedException();
        }

        public override bool InEventLoop(Thread thread)
        {
            return false;
        }

        public override bool IsShuttingDown()
        {
            return false;
        }

        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
        {
            throw new NotSupportedException();
        }

        public override Task Termination => throw new NotSupportedException();

        public override bool IsShutdown()
        {
            return false;
        }

        public override bool IsTerminated()
        {
            return false;
        }

        public override bool AwaitTermination(TimeSpan timeout)
        {
            return false;
        }

        public override void Execute(IRunnable command)
        {
            throw new NotSupportedException();
        }
    }

    private static readonly IEventExecutor EVENT_EXECUTOR = new TestEventExecutor();

    [Fact]
    public void TestOldExecutorIsRestored()
    {
        IExecutor executor = ThreadExecutorMap.Apply(ImmediateExecutor.INSTANCE, ImmediateEventExecutor.INSTANCE);
        IExecutor executor2 = ThreadExecutorMap.Apply(ImmediateExecutor.INSTANCE, EVENT_EXECUTOR);
        executor.Execute(Runnables.Create(() =>
        {
            executor2.Execute(Runnables.Create(() =>
            {
                Assert.Same(EVENT_EXECUTOR, ThreadExecutorMap.CurrentExecutor());
            }));

            Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.CurrentExecutor());
        }));
    }

    [Fact]
    public void TestDecorateExecutor()
    {
        IExecutor executor = ThreadExecutorMap.Apply(ImmediateExecutor.INSTANCE, ImmediateEventExecutor.INSTANCE);
        executor.Execute(Runnables.Create(() =>
        {
            Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.CurrentExecutor());
        }));
    }

    [Fact]
    public void TestDecorateRunnable()
    {
        ThreadExecutorMap.Apply(Runnables.Create(() =>
        {
            Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.CurrentExecutor());
        }), ImmediateEventExecutor.INSTANCE).Run();
    }

    [Fact]
    public void TestDecorateThreadFactory()
    {
        IThreadFactory threadFactory = ThreadExecutorMap.Apply(new DefaultThreadFactory("thread-executor-map-test"),
            ImmediateEventExecutor.INSTANCE);
        Exception failure = null;
        Thread thread = threadFactory.NewThread(() =>
        {
            try { Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.CurrentExecutor()); }
            catch (Exception exception) { failure = exception; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }
}
