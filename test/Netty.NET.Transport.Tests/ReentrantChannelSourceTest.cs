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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Teardown is independent of the test deadline.
public class ReentrantChannelSourceTest
{
    [Fact(Timeout = 10000)]
    public async Task TestWritabilityChanged()
    {
        await using var fixture = new Fixture(writability: true);
        var client = await fixture.ConnectAsync();
        SetWatermarks(client);

        // What is supposed to happen from this point:
        //
        // 1. Because this write attempt has been made from a non-I/O thread,
        //    ChannelOutboundBuffer.pendingWriteBytes will be increased before
        //    write() event is really evaluated.
        //    -> channelWritabilityChanged() will be triggered,
        //       because the Channel became unwritable.
        //
        // 2. The write() event is handled by the pipeline in an I/O thread.
        //    -> write() will be triggered.
        //
        // 3. Once the write() event is handled, ChannelOutboundBuffer.pendingWriteBytes
        //    will be decreased.
        //    -> channelWritabilityChanged() will be triggered,
        //       because the Channel became writable again.
        //
        // 4. The message is added to the ChannelOutboundBuffer and thus
        //    pendingWriteBytes will be increased again.
        //    -> channelWritabilityChanged() will be triggered.
        //
        // 5. The flush() event causes the write request in theChannelOutboundBuffer
        //    to be removed.
        //    -> flush() and channelWritabilityChanged() will be triggered.
        //
        // Note that the channelWritabilityChanged() in the step 4 can occur between
        // the flush() and the channelWritabilityChanged() in the step 5, because
        // the flush() is invoked from a non-I/O thread while the other are from
        // an I/O thread.
        Task write = client.WriteAsync(CreateTestBuffer(2000));
        client.Flush();
        await write.WaitAsync(TestContext.Current.CancellationToken);
        await client.CloseAsync();
        fixture.AssertLog(
            // Case 1:
            "WRITABILITY: writable=false\nWRITE\nWRITABILITY: writable=false\nWRITABILITY: writable=false\nFLUSH\nWRITABILITY: writable=true\n",
            // Case 2:
            "WRITABILITY: writable=false\nWRITE\nWRITABILITY: writable=false\nFLUSH\nWRITABILITY: writable=true\nWRITABILITY: writable=true\n");
    }

    /**
     * Similar to {@link #testWritabilityChanged()} with slight variation.
     */
    [Fact(Timeout = 10000)]
    public async Task TestFlushInWritabilityChanged()
    {
        await using var fixture = new Fixture(writability: true);
        var client = await fixture.ConnectAsync();
        SetWatermarks(client);
        client.Pipeline.AddLast(new FlushWhenUnwritable());
        Assert.True(client.IsWritable);

        await client.WriteAsync(CreateTestBuffer(2000)).WaitAsync(TestContext.Current.CancellationToken);
        await client.CloseAsync();
        fixture.AssertLog(
            // Case 1:
            "WRITABILITY: writable=false\nFLUSH\nWRITE\nWRITABILITY: writable=false\nWRITABILITY: writable=false\nFLUSH\nWRITABILITY: writable=true\n",
            // Case 2:
            "WRITABILITY: writable=false\nFLUSH\nWRITE\nWRITABILITY: writable=false\nFLUSH\nWRITABILITY: writable=true\nWRITABILITY: writable=true\n");
    }

    [Fact(Timeout = 10000)]
    public async Task TestWriteFlushPingPong()
    {
        await using var fixture = new Fixture(writability: false);
        var client = await fixture.ConnectAsync();
        client.Pipeline.AddLast(new PingPong());
        _ = client.WriteAndFlushAsync(CreateTestBuffer(2000));
        await client.CloseAsync(TestContext.Current.CancellationToken);
        fixture.AssertLog("WRITE\nFLUSH\nWRITE\nFLUSH\nWRITE\nFLUSH\nWRITE\nFLUSH\nWRITE\nFLUSH\nWRITE\nFLUSH\nCLOSE\n");
    }

    [Fact(Timeout = 10000)]
    public async Task TestCloseInFlush()
    {
        await using var fixture = new Fixture(writability: false);
        var client = await fixture.ConnectAsync();
        client.Pipeline.AddLast(new CloseInFlush());
        await client.WriteAsync(CreateTestBuffer(2000)).WaitAsync(TestContext.Current.CancellationToken);
        await client.CloseCompletion.WaitAsync(TestContext.Current.CancellationToken);
        fixture.AssertLog("WRITE\nFLUSH\nCLOSE\n");
    }

    [Fact(Timeout = 10000)]
    public async Task TestFlushFailure()
    {
        await using var fixture = new Fixture(writability: false);
        var client = await fixture.ConnectAsync();
        client.Pipeline.AddLast(new FailFlush());
        var error = await Record.ExceptionAsync(() =>
            client.WriteAndFlushAsync(CreateTestBuffer(2000)).WaitAsync(TestContext.Current.CancellationToken));
        // FIXME:  shouldn't this contain the "intentional failure" exception?
        Assert.IsType<IOException>(error);
        Assert.Equal("Channel is closed.", error.Message);
        await client.CloseCompletion.WaitAsync(TestContext.Current.CancellationToken);
        fixture.AssertLog("WRITE\nCLOSE\n");
    }

    [Fact(Timeout = 10000)]
    public async Task NestReentrancy()
    {
        var group = new DefaultEventLoopGroup(1);
        var address = new LocalAddress(typeof(ReentrantChannelSourceTest).Name);
        var received = Channel.CreateUnbounded<string>();
        var token = TestContext.Current.CancellationToken;
        try
        {
            var server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(channel =>
                {
                    channel.Config.SetAutoRead(false);
                    // first handler splits input by \n and into strings
                    channel.Pipeline.AddLast(new SplitInput());
                    // second handler buffers messages, sends them on in channelReadComplete, and acts as flow
                    // control
                    channel.Pipeline.AddLast(new DemandBuffer());
                    // third handler saves incoming packets so that we can test their order
                    channel.Pipeline.AddLast(new SaveInput(received.Writer));
                    // final handler relieves backpressure, triggering reentrant channelReads
                    channel.Pipeline.AddLast(new RequestInput());
                })).BindAsync(address, token);
            var client = await new Bootstrap().Group(group).Channel<LocalChannel>()
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(address, cancellationToken: token);
            await client.WriteAndFlushAsync(Unpooled.WrappedBuffer("A\nB\nC"u8.ToArray()), token);

            // order should be unchanged
            Assert.Equal("A", await received.Reader.ReadAsync(token));
            Assert.Equal("B", await received.Reader.ReadAsync(token));
            Assert.Equal("C", await received.Reader.ReadAsync(token));

            await client.CloseAsync();
            await server.CloseAsync();
        }
        finally { await group.StopAsync(); }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedWriteReleasesItsReservationAndBuffer(bool reject)
    {
        await using var fixture = new Fixture(writability: false);
        var client = (LocalChannel)await fixture.ConnectAsync();
        SetWatermarks(client);
        var group = new DefaultEventLoopGroup(1);
        var executor = group.Next();
        var token = TestContext.Current.CancellationToken;
        using var cancelWrite = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var resumeExecutor = new ManualResetEventSlim();
        try
        {
            client.Pipeline.AddLast(group, "external-writer", new PassWrite());
            await executor.SubmitAsync(() => { }, token);
            if (reject)
            {
                await group.StopAsync();
            }
            else
            {
                var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                executor.Execute(() => { blocked.SetResult(); resumeExecutor.Wait(token); });
                await blocked.Task.WaitAsync(token);
            }

            var buffer = CreateTestBuffer(2000);
            var write = client.WriteAndFlushAsync(buffer, cancelWrite.Token);
            if (reject)
            {
                await Assert.ThrowsAsync<RejectedExecutionException>(() => write.WaitAsync(token));
            }
            else
            {
                Assert.Equal(2032, client.OutboundBuffer.TotalPendingWriteBytes);
                Assert.False(client.IsWritable);
                cancelWrite.Cancel();
                resumeExecutor.Set();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write.WaitAsync(token));
            }
            Assert.Equal(0, client.OutboundBuffer.TotalPendingWriteBytes);
            Assert.Equal(0, buffer.ReferenceCount);
            Assert.True(client.IsWritable);
        }
        finally
        {
            resumeExecutor.Set();
            await group.StopAsync();
        }
    }

    private static void SetWatermarks(IChannel channel)
    {
        channel.Config.SetWriteBufferLowWaterMark(512);
        channel.Config.SetWriteBufferHighWaterMark(1024);
    }

    private static ByteBuf CreateTestBuffer(int length) => Unpooled.Buffer(length, length).SetIndex(0, length);

    private sealed class Fixture(bool writability) : IAsyncDisposable
    {
        private readonly DefaultEventLoopGroup _servers = new(1);
        private readonly DefaultEventLoopGroup _clients = new(1);
        private readonly EventLog _log = new(writability);

        internal async Task<IChannel> ConnectAsync()
        {
            var address = new LocalAddress(typeof(ReentrantChannelSourceTest).Name);
            var token = TestContext.Current.CancellationToken;
            await new ServerBootstrap().Group(_servers).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<LocalChannel>(_ => { })).BindAsync(address, token);
            return await new Bootstrap().Group(_clients).Channel<LocalChannel>()
                .Handler(_log).ConnectAsync(address, cancellationToken: token);
        }

        internal void AssertLog(string firstExpected, params string[] otherExpected)
        {
            string actual = _log.Text;
            if (firstExpected == actual) return;
            foreach (string expected in otherExpected)
                if (expected == actual) return;

            // Let the comparison fail with the first expectation.
            Assert.Equal(firstExpected, actual);
        }

        public async ValueTask DisposeAsync() => await Task.WhenAll(_clients.StopAsync(), _servers.StopAsync());
    }

    private sealed class EventLog(bool writability) : ChannelDuplexHandler
    {
        private readonly StringBuilder _log = new();
        internal string Text => _log.ToString();
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token)
        { _log.Append("WRITE\n"); return context.WriteAsync(message, token); }
        public override void Flush(ChannelHandlerContext context)
        { _log.Append("FLUSH\n"); context.Flush(); }
        public override Task CloseAsync(ChannelHandlerContext context, CancellationToken token)
        { if (!writability) _log.Append("CLOSE\n"); return context.CloseAsync(token); }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error)
        { if (!writability) _log.Append("EXCEPTION: ").Append(error).Append('\n'); }
        public override void ChannelWritabilityChanged(ChannelHandlerContext context)
        {
            if (writability) _log.Append(context.Channel.IsWritable ? "WRITABILITY: writable=true\n" : "WRITABILITY: writable=false\n");
            context.FireChannelWritabilityChanged();
        }
    }

    private sealed class FlushWhenUnwritable : ChannelInboundHandlerAdapter
    {
        public override void ChannelWritabilityChanged(ChannelHandlerContext context)
        {
            if (!context.Channel.IsWritable) context.Channel.Flush();
            context.FireChannelWritabilityChanged();
        }
    }

    private sealed class PassWrite : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token) =>
            context.WriteAsync(message, token);
    }

    private sealed class PingPong : ChannelOutboundHandlerAdapter
    {
        private int _writes;
        private int _flushes;
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token)
        {
            if (_writes++ < 5) context.Channel.Flush();
            return context.WriteAsync(message, token);
        }
        public override void Flush(ChannelHandlerContext context)
        {
            if (_flushes++ < 5) _ = context.Channel.WriteAsync(CreateTestBuffer(2000));
            context.Flush();
        }
    }

    private sealed class CloseInFlush : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token)
        {
            Task write = context.WriteAsync(message, token);
            _ = CloseOnCompletionAsync(context.Channel, write);
            context.Channel.Flush();
            return write;
        }

        private static async Task CloseOnCompletionAsync(IChannel channel, Task write)
        {
            try { await write.ConfigureAwait(false); }
            finally { await channel.CloseAsync().ConfigureAwait(false); }
        }
    }

    private sealed class FailFlush : ChannelOutboundHandlerAdapter
    {
        public override void Flush(ChannelHandlerContext context) => throw new InvalidOperationException("intentional failure");
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => _ = context.CloseAsync();
    }

    private sealed class SplitInput : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            var buffer = (ByteBuf)message;
            string text;
            try { text = buffer.GetString(Encoding.UTF8); }
            finally { buffer.Release(); }
            foreach (string line in text.Split('\n')) context.FireChannelRead(line);
        }
    }

    private sealed class DemandBuffer : ChannelDuplexHandler
    {
        private readonly Queue<object> _queue = new();
        private bool _demand = true;
        public override void HandlerAdded(ChannelHandlerContext context) => context.Read();
        public override void ChannelRead(ChannelHandlerContext context, object message) => _queue.Enqueue(message);
        public override void ChannelReadComplete(ChannelHandlerContext context)
        {
            while (_demand && _queue.TryDequeue(out var item))
            {
                _demand = false;
                context.FireChannelRead(item);
            }
            context.FireChannelReadComplete();
        }
        public override void Read(ChannelHandlerContext context)
        {
            if (_queue.TryDequeue(out var item)) context.FireChannelRead(item);
            else
            {
                _demand = true;
                context.Read();
            }
        }
    }

    private sealed class SaveInput(ChannelWriter<string> received) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { received.TryWrite((string)message); context.FireChannelRead(message); }
    }

    private sealed class RequestInput : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(ChannelHandlerContext context, object message) => context.Read();
        public override void ChannelReadComplete(ChannelHandlerContext context) => context.Read();
    }
}
