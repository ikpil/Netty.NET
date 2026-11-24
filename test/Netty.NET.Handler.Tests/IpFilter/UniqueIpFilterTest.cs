/*
 * Copyright 2018 The Netty Project
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

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Handler.IpFilter;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Tests.IpFilter;

// CLR: CyclicBarrier(2) is Barrier(2): its participants signal "and implicitly wait for all others to arrive", and "The
// same Barrier can be used for multiple phases" (https://learn.microsoft.com/en-us/dotnet/api/system.threading.barrier?view=net-10.0),
// as the CyclicBarrier "can be re-used after the waiting threads are released"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/CyclicBarrier.html); barrier.reset()
// starts a new generation while no party waits, which Barrier does by itself after each phase, so it has no
// counterpart. Executors.newFixedThreadPool(2), where "at most nThreads threads will be active processing tasks"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/Executors.html#newFixedThreadPool(int)),
// is the concurrent scheduler of a ConcurrentExclusiveSchedulerPair with "The maximum number of tasks to run
// concurrently" 2 on the thread pool
// (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.concurrentexclusiveschedulerpair.-ctor?view=net-10.0);
// submit(Callable) is Task.Factory.StartNew on it, Future.get() is an await of the Task (which rethrows a failure, as
// ExecutionException carries it), close().await() is an await of the close Task and shutdown() is Complete(). The
// anonymous EmbeddedChannel is the named nested class InetEmbeddedChannel; SocketUtils.socketAddress of a literal is
// new IPEndPoint(IPAddress.Parse(...), port) (docs/codec-dns-porting.md).
public class UniqueIpFilterTest
{

    [Fact]
    public async Task TestUniqueIpFilterHandler()
    {
        Barrier barrier = new Barrier(2);
        ConcurrentExclusiveSchedulerPair executorService = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 2);
        try
        {
            for (int round = 0; round < 10000; round++)
            {
                UniqueIpFilter ipFilter = new UniqueIpFilter();
                Task<EmbeddedChannel> future1 = NewChannelAsync(barrier, executorService, ipFilter);
                Task<EmbeddedChannel> future2 = NewChannelAsync(barrier, executorService, ipFilter);
                EmbeddedChannel ch1 = await future1;
                EmbeddedChannel ch2 = await future2;
                Assert.True(ch1.IsActive || ch2.IsActive);
                Assert.False(ch1.IsActive && ch2.IsActive);

                await ch1.CloseAsync(TestContext.Current.CancellationToken);
                await ch2.CloseAsync(TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            executorService.Complete();
        }
    }

    private static Task<EmbeddedChannel> NewChannelAsync(Barrier barrier,
            ConcurrentExclusiveSchedulerPair executorService,
            params IChannelHandler[] handler)
    {
        return Task.Factory.StartNew<EmbeddedChannel>(() =>
        {
            barrier.SignalAndWait();
            return new InetEmbeddedChannel(handler);
        }, CancellationToken.None, TaskCreationOptions.None, executorService.ConcurrentScheduler);
    }

    private sealed class InetEmbeddedChannel : EmbeddedChannel
    {
        internal InetEmbeddedChannel(params IChannelHandler[] handlers) : base(handlers)
        {
        }

        protected override EndPoint RemoteAddress0()
        {
            return IsActive ? new IPEndPoint(IPAddress.Parse("91.92.93.1"), 5421) : null;
        }
    }

}
