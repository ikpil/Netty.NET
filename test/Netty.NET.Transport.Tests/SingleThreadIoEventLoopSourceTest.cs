/*
 * Copyright 2024 The Netty Project
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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent teardown.
public class SingleThreadIoEventLoopSourceTest
{
    [Fact]
    public async Task TestDefaultIoRatio()
    {
        var loop = new DefaultEventLoop();
        try { Assert.Equal(DefaultEventLoop.DefaultIoRatio, loop.IoRatio); }
        finally { await loop.StopAsync(); }
    }

    [Fact]
    public async Task TestSetIoRatio()
    {
        var loop = new DefaultEventLoop();
        try
        {
            loop.IoRatio = 50;
            Assert.Equal(50, loop.IoRatio);
            Assert.Throws<ArgumentOutOfRangeException>(() => loop.IoRatio = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => loop.IoRatio = 101);
        }
        finally { await loop.StopAsync(); }
    }

    [Theory(Timeout = 5000)]
    [InlineData(50, true, 64)]
    [InlineData(100, true, 130)]
    [InlineData(50, false, 130)]
    public async Task IoBatchBoundsTheNextOrdinaryTaskPhase(int ratio, bool handled, int tasksAtNextIo)
    {
        var ticker = Ticker.NewMockTicker();
        var loop = new ClockedLoop(ticker) { IoRatio = ratio };
        var token = TestContext.Current.CancellationToken;
        var ownerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondIo = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        int ordinaryTasks = 0;
        try
        {
            loop.Execute(() => { ownerEntered.SetResult(); resume.Wait(token); });
            await ownerEntered.Task.WaitAsync(token);
            for (int i = 0; i < 130; i++)
                loop.Execute(() => { ordinaryTasks++; ticker.Advance(10); });

            Task firstIo = loop.SubmitIoAsync(() =>
            {
                Assert.True(loop.InEventLoop());
                Assert.Equal(0, ordinaryTasks);
                ticker.Advance(10);
                _ = loop.SubmitIoAsync(() =>
                {
                    secondIo.SetResult(ordinaryTasks);
                    return (Task.CompletedTask, true);
                });
                return (Task.CompletedTask, handled);
            });
            resume.Set();
            await firstIo.WaitAsync(token);
            Assert.Equal(tasksAtNextIo, await secondIo.Task.WaitAsync(token));
            await loop.SubmitAsync(() => Assert.Equal(130, ordinaryTasks), token);
        }
        finally
        {
            resume.Set();
            await loop.StopAsync();
        }
    }

    [Theory]
    [InlineData(-1L, 50, 1000L, 1000L)]
    [InlineData(0L, 50, 1000L, 1000L)]
    [InlineData(80L, 50, 1000L, 80L)]
    [InlineData(80L, 20, 100L, 100L)]
    [InlineData(1L, 99, 1000L, 1000L)]
    [InlineData(long.MaxValue, 1, 1000L, 1000L)]
    public void TaskBudgetClampsAndNeverOverflows(long active, int ratio, long maximum, long expected) =>
        Assert.Equal(expected, DefaultEventLoop.TaskBudgetNanos(active, ratio, maximum));

    [Fact(Timeout = 5000)]
    public async Task StopDrainsAcceptedIoOnTheOwnerAndRejectsLateCompletions()
    {
        var loop = new DefaultEventLoop();
        var token = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var context = new AsyncLocal<string>();
        try
        {
            loop.Execute(() => { entered.SetResult(); resume.Wait(token); });
            await entered.Task.WaitAsync(token);
            context.Value = "publisher";
            Task accepted = loop.SubmitIoAsync(() =>
            {
                Assert.True(loop.InEventLoop());
                Assert.Equal("publisher", context.Value);
                return (Task.CompletedTask, true);
            });
            context.Value = "after publication";
            Task stop = loop.StopAsync();
            Assert.Throws<RejectedExecutionException>(() => { _ = loop.SubmitIoAsync(() => (Task.CompletedTask, true)); });
            resume.Set();
            await accepted.WaitAsync(token);
            await stop.WaitAsync(token);
            Assert.Equal(0, loop.PendingTasks());
        }
        finally
        {
            resume.Set();
            await loop.StopAsync();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task RealTcpReadCompletionRunsBeforeTheQueuedOrdinaryTask()
    {
        var token = TestContext.Current.CancellationToken;
        var group = new DefaultEventLoopGroup(1);
        var inbound = new ReadProbe();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var channel = await new Bootstrap().Group(group).ChannelFactory(() => new TcpSocketChannel(AddressFamily.InterNetwork)).Handler(inbound)
                .ConnectAsync(listener.LocalEndpoint, cancellationToken: token);
            using var sender = await listener.AcceptSocketAsync(token);
            var loop = (DefaultEventLoop)channel.EventLoop;
            await loop.SubmitAsync(() => { }, token);
            loop.Execute(() => { entered.SetResult(); resume.Wait(token); });
            await entered.Task.WaitAsync(token);
            Task ordinary = loop.SubmitAsync(() => Assert.True(inbound.Received.Task.IsCompletedSuccessfully), token);
            Assert.Equal(1, await sender.SendAsync(new byte[] { 71 }, SocketFlags.None, token));
            while (loop.PendingTasks() <= 1) await Task.Delay(1, token);
            resume.Set();
            await ordinary.WaitAsync(token);
            Assert.Equal(71, await inbound.Received.Task.WaitAsync(token));
        }
        finally
        {
            resume.Set();
            await group.StopAsync();
        }
    }

    private sealed class ReadProbe : SimpleChannelInboundHandler<ByteBuf>
    {
        internal readonly TaskCompletionSource<int> Received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message) =>
            Received.TrySetResult(message.ReadByte());
    }

    private sealed class ClockedLoop(Ticker clock) : DefaultEventLoop
    {
        public override Ticker Ticker() => clock;
    }
}
