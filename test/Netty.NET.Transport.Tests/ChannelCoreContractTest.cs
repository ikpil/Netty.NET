using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Explicit default-token calls and separately owned cancellation are under test.
public class ChannelCoreContractTest
{
    [Fact]
    public void InboundAndOutboundHandlersFollowOppositeOrder()
    {
        var calls = new List<string>();
        var channel = new EmbeddedChannel(new Recorder("a", calls), new Recorder("b", calls), new Recorder("c", calls));
        calls.Clear();
        Assert.True(channel.WriteInbound("in"));
        Assert.Equal(new[] { "a.in", "b.in", "c.in" }, calls);
        Assert.Equal("in", channel.ReadInbound<string>());
        calls.Clear();
        Assert.True(channel.WriteOutbound("out"));
        Assert.Equal(new[] { "c.out", "b.out", "a.out" }, calls);
        Assert.Equal("out", channel.ReadOutbound<string>());
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public async Task WriteDoesNotCompleteOrProduceOutputUntilFlush()
    {
        var channel = new EmbeddedChannel();
        var buffer = Unpooled.Buffer(1).WriteByte(42);
        Task write = channel.WriteAsync(buffer);
        Assert.False(write.IsCompleted);
        Assert.Null(channel.ReadOutbound());
        channel.Flush();
        await write;
        Assert.Same(buffer, channel.ReadOutbound());
        Assert.Equal(1, buffer.RefCnt);
        Assert.True(buffer.Release());
        Assert.False(channel.Finish());
    }

    [Fact]
    public async Task CanceledUnflushedWriteIsReleasedAndNotSent()
    {
        var channel = new EmbeddedChannel();
        var buffer = Unpooled.Buffer(1);
        using var cancellation = new CancellationTokenSource();
        Task write = channel.WriteAsync(buffer, cancellation.Token);
        cancellation.Cancel();
        channel.Flush();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.Equal(0, buffer.RefCnt);
        // The source keeps a cancelled entry as an empty buffer until the flushed batch drains.
        var placeholder = Assert.IsAssignableFrom<ByteBuf>(channel.ReadOutbound());
        Assert.Equal(0, placeholder.ReadableBytes);
        placeholder.Release();
        Assert.Null(channel.ReadOutbound());
        Assert.Equal(0, channel.OutboundBuffer.TotalPendingWriteBytes);
        channel.Finish();
    }

    [Fact]
    public async Task CloseReleasesUnflushedWritesAndSignalsLifecycle()
    {
        var calls = new List<string>();
        var channel = new EmbeddedChannel(new Recorder("a", calls));
        calls.Clear();
        var buffer = Unpooled.Buffer(1);
        Task write = channel.WriteAsync(buffer);
        calls.Clear();
        await channel.CloseAsync();
        await Assert.ThrowsAnyAsync<ClosedChannelException>(() => write);
        Assert.Equal(0, buffer.RefCnt);
        Assert.Equal(new[] { "a.inactive", "a.unregistered", "a.removed" }, calls);
        Assert.True(channel.CloseFuture.IsCompletedSuccessfully);
        Assert.False(channel.IsRegistered);
        Assert.False(channel.IsOpen);
    }

    [Fact]
    public void WaterMarksUseStrictCrossingAndUserBits()
    {
        var calls = new List<string>();
        var channel = new EmbeddedChannel(new Recorder("a", calls));
        channel.Config.SetWriteBufferWaterMark(new WriteBufferWaterMark(100, 200));
        calls.Clear();
        channel.OutboundBuffer.IncrementPendingOutboundBytes(200);
        Assert.True(channel.IsWritable);
        Assert.Equal(1, channel.BytesBeforeUnwritable);
        channel.OutboundBuffer.IncrementPendingOutboundBytes(1);
        Assert.False(channel.IsWritable);
        channel.OutboundBuffer.SetUserDefinedWritability(1, false);
        channel.OutboundBuffer.DecrementPendingOutboundBytes(102);
        Assert.False(channel.IsWritable);
        channel.OutboundBuffer.SetUserDefinedWritability(1, true);
        Assert.True(channel.IsWritable);
        // Tracker and user-defined writability changes notify later on the event loop, as in the source.
        Assert.Empty(calls);
        channel.RunPendingTasks();
        Assert.Equal(new[] { "a.writable", "a.writable" }, calls);
        channel.Finish();
    }

    [Fact]
    public void ConfigurationDefaultsValidationAndUnknownOptions()
    {
        var channel = new EmbeddedChannel();
        var config = (DefaultChannelConfig)channel.Config;
        Assert.Equal(30000, config.GetConnectTimeoutMillis());
        Assert.Equal(16, config.GetWriteSpinCount());
        Assert.Equal(16, ((IMaxMessagesRecvByteBufAllocator)config.GetRecvByteBufAllocator()).MaxMessagesPerRead());
        Assert.True(config.TryGetOption(ChannelOption.AUTO_READ, out bool autoRead));
        Assert.True(autoRead);
        Assert.False(config.TryGetOption(ChannelOption<int>.ValueOf(Guid.NewGuid().ToString()), out _));
        config.SetWriteSpinCount(int.MaxValue);
        Assert.Equal(int.MaxValue - 1, config.GetWriteSpinCount());
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetWriteBufferHighWaterMark(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetWriteBufferLowWaterMark(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetConnectTimeoutMillis(-1));
        Assert.Throws<ArgumentException>(() => config.SetOptions(new[] { new KeyValuePair<ChannelOption, object>(ChannelOption.AUTO_READ, 1) }));
        channel.Finish();
    }

    [Fact]
    public void NonSharableReuseAndDuplicateNamesAreRejected()
    {
        var handler = new ChannelInboundHandlerAdapter();
        var channel = new EmbeddedChannel(handler);
        Assert.Throws<ChannelPipelineException>(() => channel.Pipeline.AddLast("again", handler));
        channel.Pipeline.AddLast("name", new ChannelInboundHandlerAdapter());
        Assert.Throws<ArgumentException>(() => channel.Pipeline.AddLast("name", new ChannelInboundHandlerAdapter()));
        channel.Finish();
    }

    [Fact]
    public void DeferredHandlerAddedAndNestedInitializersPreserveOrder()
    {
        var calls = new List<string>();
        var channel = new EmbeddedChannel(false, false, new Initializer(ch =>
        {
            ch.Pipeline.AddLast(new Recorder("a", calls));
            ch.Pipeline.AddLast(new Initializer(inner =>
            {
                inner.Pipeline.AddLast(new Recorder("b", calls));
                inner.Pipeline.AddLast(new Recorder("c", calls));
            }));
            ch.Pipeline.AddLast(new Recorder("d", calls));
        }));
        Assert.Empty(calls);
        channel.Register();
        Assert.Equal(new[] { "a.added", "b.added", "c.added", "d.added" }, calls.Take(4));
        Assert.Equal(new[] { "a", "b", "c", "d" }, channel.Pipeline.Select(p => ((Recorder)p.Value).Name));
        channel.Finish();
    }

    [Fact]
    public void SimpleInboundHandlerReleasesOnlyAcceptedMessages()
    {
        var accepted = Unpooled.Buffer(1);
        var channel = new EmbeddedChannel(new BufferConsumer());
        Assert.False(channel.WriteInbound(accepted));
        Assert.Equal(0, accepted.RefCnt);
        Assert.True(channel.WriteInbound("pass"));
        Assert.Equal("pass", channel.ReadInbound());
        channel.Finish();
    }

    [Fact]
    public async Task RealEventLoopOwnsRegistrationCallbacksAndShutdownClosesChannel()
    {
        var group = new DefaultEventLoopGroup(1);
        var calls = new List<string>();
        // A non-I/O SingleThreadEventLoop closes only channels that register a shutdown hook, as LocalChannel does.
        var channel = new LocalChannel();
        channel.Pipeline.AddLast(new Recorder("a", calls));
        var loop = group.Next();
        await group.RegisterAsync(channel);
        await loop.SubmitAsync(() => Assert.True(channel.IsRegistered));
        await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await channel.CloseFuture.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(channel.IsOpen);
        Assert.Contains("a.registered", calls);
        Assert.Contains("a.removed", calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteFailureClosesBeforeReleasingQueuedOwnershipAndPreservesAllCauses(bool socketFailure)
    {
        Exception failure = socketFailure ? new SocketException((int)SocketError.ConnectionReset) : new IOException("original");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loop = new DefaultEventLoop();
        var channel = new FailureChannel(pending.Task);
        var closedAtRelease = new List<bool>();
        var first = new ReleaseObserver(Unpooled.Buffer(1).WriteByte(1), () => closedAtRelease.Add(!channel.IsOpen && !channel.IsActive && !channel.IsWritable));
        var second = Unpooled.Buffer(1).WriteByte(2);
        var third = Unpooled.Buffer(1).WriteByte(3);
        Task firstWrite = null, secondWrite = null, unflushed = null;
        try
        {
            await channel.RegisterAsync(loop);
            await loop.SubmitAsync(() =>
            {
                firstWrite = channel.WriteAsync(first);
                secondWrite = channel.WriteAsync(second);
                channel.Flush();
                unflushed = channel.WriteAsync(third);
            });
            pending.SetException(failure);
            Assert.Same(failure, await Record.ExceptionAsync(() => firstWrite.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Same(failure, await Record.ExceptionAsync(() => secondWrite.WaitAsync(TimeSpan.FromSeconds(5))));
            var unflushedCause = await Assert.ThrowsAnyAsync<ClosedChannelException>(() => unflushed.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(failure, unflushedCause.InnerException);
            Assert.True(Assert.Single(closedAtRelease));
            Assert.Equal(0, first.RefCnt);
            Assert.Equal(0, second.RefCnt);
            Assert.Equal(0, third.RefCnt);
            var late = await Assert.ThrowsAnyAsync<ClosedChannelException>(() => channel.WriteAsync(new object()));
            Assert.Same(failure, late.InnerException);
            await channel.CloseFuture.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            pending.TrySetException(failure);
            await channel.CloseAsync();
            await loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task InactiveFlushDoesNotRecursivelyDrainWritesAddedDuringRelease()
    {
        var loop = new Mock<IEventLoop>();
        loop.Setup(value => value.InEventLoop()).Returns(true);
        loop.Setup(value => value.Execute(It.IsAny<Action>())).Callback<Action>(action => action());
        var channel = new FailureChannel(Task.CompletedTask, false);
        var writes = new List<Task>();
        int remaining = 8, depth = 0, maximumDepth = 0;
        void Enqueue()
        {
            var message = new ReleaseObserver(Unpooled.Buffer(1).WriteByte(1), () =>
            {
                maximumDepth = Math.Max(maximumDepth, ++depth);
                if (--remaining != 0)
                {
                    Enqueue();
                }
                depth--;
            });
            writes.Add(channel.WriteAndFlushAsync(message));
        }
        try
        {
            await channel.RegisterAsync(loop.Object);
            Enqueue();
            Assert.Equal(8, writes.Count);
            foreach (var write in writes)
                Assert.Equal(SocketError.NotConnected, (await Assert.ThrowsAsync<SocketException>(() => write)).SocketErrorCode);
            Assert.Equal(1, maximumDepth);
            Assert.Equal(0, channel.OutboundBuffer.TotalPendingWriteBytes);
            Assert.Equal(0, channel.OutboundBuffer.Size);
        }
        finally
        {
            await channel.CloseAsync();
        }
    }
    private sealed class FailureChannel : AbstractChannel
    {
        private readonly Task _write;
        internal FailureChannel(Task write, bool active = true) : base(null, new ChannelMetadata(false))
        {
            _write = write;
            SetActive(active);
        }

        protected override EndPoint LocalAddress0()
        {
            return null;
        }

        protected override EndPoint RemoteAddress0()
        {
            return null;
        }

        protected override Task DoBindAsync(EndPoint address, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        protected override void DoClose()
        {
        }

        protected override void DoBeginRead()
        {
        }

        protected override Task DoWrite(object msg)
        {
            return _write;
        }
    }
    private sealed class ReleaseObserver(ByteBuf content, Action released) : DefaultByteBufHolder(content)
    {
        public override bool Release()
        {
            bool result = base.Release();
            if (result)
            {
                released();
            }
            return result;
        }
    }
    private sealed class Initializer(Action<EmbeddedChannel> initialize) : ChannelInitializer<EmbeddedChannel>
    {
        protected override void InitChannel(EmbeddedChannel channel)
        {
            initialize(channel);
        }
    }
    private sealed class BufferConsumer : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
        }
    }
    private sealed class LoopChannel() : AbstractChannel(null, new ChannelMetadata(false))
    {
        protected override System.Net.EndPoint LocalAddress0()
        {
            return null;
        }

        protected override System.Net.EndPoint RemoteAddress0()
        {
            return null;
        }

        protected override void DoRegister()
        {
            SetActive(true);
        }

        protected override Task DoBindAsync(System.Net.EndPoint address, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        protected override Task DoConnectAsync(System.Net.EndPoint remote, System.Net.EndPoint local, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        protected override void DoClose()
        {
        }

        protected override void DoBeginRead()
        {
        }

        protected override Task DoWrite(object msg)
        {
            return Task.CompletedTask;
        }
    }
    private sealed class Recorder(string name, List<string> calls) : ChannelDuplexHandler
    {
        internal string Name => name;
        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            calls.Add(name + ".added");
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            calls.Add(name + ".removed");
        }

        public override void ChannelRegistered(IChannelHandlerContext ctx)
        {
            calls.Add(name + ".registered");
            ctx.FireChannelRegistered();
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            calls.Add(name + ".inactive");
            ctx.FireChannelInactive();
        }

        public override void ChannelUnregistered(IChannelHandlerContext ctx)
        {
            calls.Add(name + ".unregistered");
            ctx.FireChannelUnregistered();
        }

        public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
        {
            calls.Add(name + ".writable");
            ctx.FireChannelWritabilityChanged();
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            calls.Add(name + ".in");
            ctx.FireChannelRead(msg);
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            calls.Add(name + ".out");
            return ctx.WriteAsync(msg, cancellationToken);
        }
    }
}
