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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // The source's bind/connect/close calls and latch waits do not observe the test cancellation.
public class SocketBufReleaseSourceTest : AbstractSocketTest
{

    // CLR: the source's static executor, new DefaultEventExecutorGroup(1, new DefaultThreadFactory(
    // SocketBufReleaseTest.class, true)).next(), only notifies the listeners of BufWriterHandler's
    // DefaultPromise<Channel>. That promise is a TaskCompletionSource<IChannel> here, which completes without an
    // executor, so the executor field is not created.

    [Fact]
    public async Task TestBufRelease()
    {
        await Run(TestContext.Current, TestBufRelease0);
    }

    // CLR: xUnit1024 forbids overloading the TestBufRelease test name.
    private async Task TestBufRelease0(ServerBootstrap sb, Bootstrap cb)
    {
        BufWriterHandler serverHandler = new BufWriterHandler();
        BufWriterHandler clientHandler = new BufWriterHandler();

        sb.ChildHandler(serverHandler);
        cb.Handler(clientHandler);

        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);

        // Ensure the server socket accepted the client connection *and* initialized pipeline successfully.
        await serverHandler.channelFuture.Task;

        // and then close all sockets.
        await sc.CloseAsync();
        await cc.CloseAsync();

        await serverHandler.Check();
        await clientHandler.Check();

        serverHandler.Release();
        clientHandler.Release();
    }

    private class BufWriterHandler : SimpleChannelInboundHandler<object>
    {

        // CLR: CountDownLatch(1) is a TaskCompletionSource: countDown() is TrySetResult(), which returns false
        // instead of failing once the Task is complete, as countDown() does nothing at a zero count
        // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetresult,
        // https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/concurrent/CountDownLatch.html), and
        // await() awaits its Task.
        private readonly TaskCompletionSource latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private ByteBuf buf;
        internal readonly TaskCompletionSource<IChannel> channelFuture =
                new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            channelFuture.SetResult(ctx.Channel);
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            byte[] data = new byte[1024];
            // CLR: ThreadLocalRandom.current() is Random.Shared, as in TestsuitePermutation.RandomBufferType;
            // NextBytes "Fills the elements of a specified array of bytes with random numbers"
            // (https://learn.microsoft.com/en-us/dotnet/api/system.random.nextbytes).
            Random.Shared.NextBytes(data);

            buf = ctx.Alloc().Buffer();
            // call retain on it so it can't be put back on the pool
            buf.WriteBytes(data).Retain();

            // CLR: addListener(future -> latch.countDown()) is a ContinueWith continuation, which "executes
            // asynchronously when the target Task completes" whatever its final state, as the listener runs for
            // success and failure alike (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.continuewith).
            _ = ctx.Channel.WriteAndFlushAsync(buf).ContinueWith(future => latch.TrySetResult(), TaskScheduler.Default);
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            // discard
        }

        public async Task Check()
        {
            await latch.Task;
            Assert.Equal(1, buf.RefCnt);
        }

        internal void Release()
        {
            buf.Release();
        }
    }
}
#pragma warning restore xUnit1051
