/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls do not observe the test cancellation.
public class SocketExceptionHandlingSourceTest : AbstractSocketTest
{
    [Fact]
    public async Task TestReadPendingIsResetAfterEachRead()
    {
        await Run(TestContext.Current, TestReadPendingIsResetAfterEachRead0);
    }

    // CLR: xUnit1024 forbids overloading the TestReadPendingIsResetAfterEachRead test name.
    private async Task TestReadPendingIsResetAfterEachRead0(ServerBootstrap sb, Bootstrap cb)
    {
        IChannel serverChannel = null;
        IChannel clientChannel = null;

        try
        {
            MyInitializer serverInitializer = new MyInitializer();
            sb.Option(ChannelOption.SO_BACKLOG, 1024);
            sb.ChildHandler(serverInitializer);

            serverChannel = await sb.BindAsync();

            cb.Handler(new MyInitializer());
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);

            // CLR: the source does not wait for this write; its returned Task is not observed either.
            _ = clientChannel.WriteAndFlushAsync(RandomBufferType(clientChannel.Alloc(), new byte[1024], 0, 1024));

            // We expect to get 2 exceptions (1 from BuggyChannelHandler and 1 from ExceptionHandler).
            Assert.True(await Await(serverInitializer.exceptionHandler.latch1, TimeSpan.FromSeconds(5)));

            // After we get the first exception, we should get no more, this is expected to timeout.
            Assert.False(await Await(serverInitializer.exceptionHandler.latch2, TimeSpan.FromSeconds(1)),
                "Encountered " + serverInitializer.exceptionHandler.count.Get() +
                                        " exceptions when 1 was expected");
        }
        finally
        {
            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
            }

            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }
        }
    }

    // CLR: a CountDownLatch(1) is a TaskCompletionSource here. countDown() is TrySetResult(), which returns false
    // instead of failing once the Task is complete, as countDown() does nothing at a zero count
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetresult,
    // https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/concurrent/CountDownLatch.html).
    // await(timeout, unit) "Returns: true if the count reached zero and false if the waiting time elapsed", so the
    // latch Task races Task.Delay; Task.WhenAny's "Result is the task that completed first"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenany).
    private static async Task<bool> Await(TaskCompletionSource latch, TimeSpan timeout)
    {
        return await Task.WhenAny(latch.Task, Task.Delay(timeout)) == latch.Task;
    }

    private class MyInitializer : ChannelInitializer<IChannel>
    {
        internal readonly ExceptionHandler exceptionHandler = new ExceptionHandler();
        protected override void InitChannel(IChannel ch)
        {
            IChannelPipeline pipeline = ch.Pipeline;

            pipeline.AddLast(new BuggyChannelHandler());
            pipeline.AddLast(exceptionHandler);
        }
    }

    private class BuggyChannelHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            // CLR: java.lang.NullPointerException is System.NullReferenceException, "The exception that is thrown
            // when there is an attempt to dereference a null object reference"
            // (https://learn.microsoft.com/en-us/dotnet/api/system.nullreferenceexception).
            throw new NullReferenceException("I am a bug!");
        }
    }

    private class ExceptionHandler : ChannelInboundHandlerAdapter
    {
        internal readonly AtomicLong count = new AtomicLong();
        /**
         * We expect to get 1 call to {@link #exceptionCaught(ChannelHandlerContext, Throwable)}.
         */
        internal readonly TaskCompletionSource latch1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource latch2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (count.IncrementAndGet() <= 2)
            {
                latch1.TrySetResult();
            }
            else
            {
                latch2.TrySetResult();
            }

            // This should not throw any exception.
            _ = ctx.CloseAsync();
        }
    }
}
#pragma warning restore xUnit1051
