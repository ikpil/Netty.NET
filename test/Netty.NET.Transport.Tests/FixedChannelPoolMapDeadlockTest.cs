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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Pool;

namespace Netty.NET.Transport.Tests;

/**
 * This is a test case for the deadlock scenario described in https://github.com/netty/netty/issues/8238.
 */
#pragma warning disable xUnit1051 // Bounded source barriers and independent cleanup preserve the original worker ordering.
public class FixedChannelPoolMapDeadlockTest
{
    private static readonly NoopHandler Noop = new();

    [Fact]
    public async Task TestDeadlockOnAcquire()
    {
        // CLR: each one-child group supplies the independent DefaultEventLoop used by the source.
        // Native IEventLoop does not implement the source EventLoopGroup facade.
        var groups = Enumerable.Range(0, 4).Select(_ => new DefaultEventLoopGroup(1)).ToArray();
        var threadA1 = groups[0].Next();
        var threadA2 = groups[1].Next();
        var threadB1 = groups[2].Next();
        var threadB2 = groups[3].Next();
        var poolA1 = new FixedChannelPool(BootstrapFor(groups[0]), Noop, 1);
        var poolA2 = new FixedChannelPool(BootstrapFor(groups[3]), Noop, 1);
        var poolB1 = new FixedChannelPool(BootstrapFor(groups[2]), Noop, 1);
        var poolB2 = new FixedChannelPool(BootstrapFor(groups[1]), Noop, 1);

        // Synchronize threads on these barriers to ensure order of execution, first wait until each thread is inside
        // the newPool callback, then hold the two threads that should lose the match until the first two returns, then
        // release them to test if they deadlock when trying to release their pools on each other's threads.
        using var arrivalBarrier = new Barrier(4);
        using var releaseBarrier = new Barrier(3);
        var map = new PoolMap(key =>
        {
            // Thread A1 gets a new pool on eventexecutor thread A1 (anywhere but A2 or B2)
            // Thread B1 gets a new pool on eventexecutor thread B1 (anywhere but A2 or B2)
            // Thread A2 gets a new pool on eventexecutor thread B2
            // Thread B2 gets a new pool on eventexecutor thread A2
            if (key == "A")
            {
                if (threadA1.InEventLoop())
                {
                    // Thread A1 gets pool A with thread A1
                    Await(arrivalBarrier);
                    return poolA1;
                }
                if (threadA2.InEventLoop())
                {
                    // Thread A2 gets pool A with thread B2, but only after A1 won
                    Await(arrivalBarrier);
                    Await(releaseBarrier);
                    return poolA2;
                }
            }
            else if (key == "B")
            {
                if (threadB1.InEventLoop())
                {
                    // Thread B1 gets pool with thread B1
                    Await(arrivalBarrier);
                    return poolB1;
                }
                if (threadB2.InEventLoop())
                {
                    // Thread B2 gets pool with thread A2
                    Await(arrivalBarrier);
                    Await(releaseBarrier);
                    return poolB2;
                }
            }
            throw new InvalidOperationException("Unexpected key=" + key + " or thread=" + Thread.CurrentThread.Name);
        });
        try
        {
            // Thread A1 calls ChannelPoolMap.get(A)
            // Thread A2 calls ChannelPoolMap.get(A)
            // Thread B1 calls ChannelPoolMap.get(B)
            // Thread B2 calls ChannelPoolMap.get(B)
            var futureA1 = threadA1.SubmitAsync(() => map.Get("A"));
            var futureA2 = threadA2.SubmitAsync(() => map.Get("A"));
            var futureB1 = threadB1.SubmitAsync(() => map.Get("B"));
            var futureB2 = threadB2.SubmitAsync(() => map.Get("B"));

            // Thread A1 succeeds on updating the map and moves on
            // Thread B1 succeeds on updating the map and moves on
            // These should always succeed and return with new pools
            Assert.Same(poolA1, await futureA1.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Same(poolB1, await futureB1.WaitAsync(TimeSpan.FromSeconds(1)));

            // Now release the other two threads which at this point lost the race and will try to clean up the acquired
            // pools. The expected scenario is that both pools close, in case of a deadlock they will hang.
            Await(releaseBarrier);

            // Thread A2 fails to update the map and submits close to thread B2
            // Thread B2 fails to update the map and submits close to thread A2
            // If the close is blocking, then these calls will time out as the threads are waiting for each other
            // If the close is not blocking, then the previously created pools will be returned
            // Fail the test on timeout to distinguish from other errors
            Assert.Same(poolA1, await futureA2.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Same(poolB1, await futureB2.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            try
            {
                await Task.WhenAll(poolA1.CloseAsync(), poolA2.CloseAsync(), poolB1.CloseAsync(), poolB2.CloseAsync())
                    .WaitAsync(TimeSpan.FromSeconds(5));
                await map.CloseAsync();
            }
            finally { await Shutdown(groups); }
        }
    }

    [Fact]
    public async Task TestDeadlockOnRemove()
    {
        var groups = new[] { new DefaultEventLoopGroup(1), new DefaultEventLoopGroup(1) };
        var thread1 = groups[0].Next();
        var thread2 = groups[1].Next();
        // pool1 runs on thread2, pool2 runs on thread1
        var pool1 = new FixedChannelPool(BootstrapFor(groups[1]), Noop, 1);
        var pool2 = new FixedChannelPool(BootstrapFor(groups[0]), Noop, 1);
        var map = new PoolMap(key => key switch
        {
            "#1" => pool1,
            "#2" => pool2,
            _ => throw new InvalidOperationException("Unexpected key=" + key)
        });
        try
        {
            Assert.Same(pool1, map.Get("#1"));
            Assert.Same(pool2, map.Get("#2"));
            // thread1 tries to remove pool1 which is running on thread2
            // thread2 tries to remove pool2 which is running on thread1
            using var barrier = new Barrier(2);
            var future1 = thread1.SubmitAsync(() => { Await(barrier); map.Remove("#1"); });
            var future2 = thread2.SubmitAsync(() => { Await(barrier); map.Remove("#2"); });
            // A blocking close on remove will cause a deadlock here and the test will time out
            // Fail the test on timeout to distinguish from other errors
            await future1.WaitAsync(TimeSpan.FromSeconds(1));
            await future2.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            try
            {
                await Task.WhenAll(pool1.CloseAsync(), pool2.CloseAsync()).WaitAsync(TimeSpan.FromSeconds(5));
                await map.CloseAsync();
            }
            finally { await Shutdown(groups); }
        }
    }

    private static Bootstrap BootstrapFor(DefaultEventLoopGroup group) => new Bootstrap().Channel<LocalChannel>().Group(group)
        .LocalAddress(new LocalAddress(typeof(FixedChannelPoolMapDeadlockTest)));
    private static void Await(Barrier barrier)
    {
        if (!barrier.SignalAndWait(TimeSpan.FromSeconds(1)))
            throw new TimeoutException("Timed out waiting at the original worker barrier.");
    }
    private static Task Shutdown(params DefaultEventLoopGroup[] groups) => Task.WhenAll(groups.Select(group => group.StopAsync()))
        .WaitAsync(TimeSpan.FromSeconds(5));
    private sealed class PoolMap(Func<string, FixedChannelPool> factory) : AbstractChannelPoolMap<string, FixedChannelPool>
    {
        protected override FixedChannelPool NewPool(string key) => factory(key);
    }
    private sealed class NoopHandler : AbstractChannelPoolHandler
    {
        public override void ChannelCreated(IChannel channel)
        {
            // noop
        }
    }
}
