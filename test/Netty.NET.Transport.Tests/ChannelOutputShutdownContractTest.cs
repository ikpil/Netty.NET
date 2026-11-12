using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental contracts from AbstractUnsafe.handleWriteError/shutdownOutput and the native read adapter.
#pragma warning disable xUnit1051 // The queued producers and cleanup are independently owned.
public class ChannelOutputShutdownContractTest
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    public async Task GenericWriteFailureHonorsAutoCloseAndTheOutputShutdownOverride(bool autoClose, bool halfClose, int kind)
    {
        var loop = new DefaultEventLoop();
        var channel = new ProbeChannel(halfClose);
        var events = new OutputEvents();
        channel.Pipeline.AddLast(events);
        channel.Config.SetAutoClose(autoClose);
        Exception cause = kind switch { 0 => new IOException("original"), 1 => new SocketException((int)SocketError.ConnectionReset), _ => new InvalidOperationException("original") };
        var buffers = new[] { Unpooled.Buffer(1).WriteByte(1), Unpooled.Buffer(1).WriteByte(2), Unpooled.Buffer(1).WriteByte(3) };
        Task first = null, second = null, unflushed = null;
        try
        {
            await channel.RegisterAsync(loop);
            await loop.SubmitAsync(() =>
            {
                first = channel.WriteAsync(buffers[0]);
                second = channel.WriteAsync(buffers[1]);
                channel.Flush();
                unflushed = channel.WriteAsync(buffers[2]);
            });
            channel.Write.SetException(cause);
            Exception firstCause = await Record.ExceptionAsync(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
            Exception secondCause = await Record.ExceptionAsync(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
            Exception thirdCause = await Record.ExceptionAsync(() => unflushed.WaitAsync(TimeSpan.FromSeconds(5)));
            await loop.SubmitAsync(() => { });
            bool autoClosed = autoClose && kind != 2;
            if (autoClosed)
            {
                Assert.Same(cause, firstCause); Assert.Same(cause, secondCause);
                Assert.Same(cause, Assert.IsType<IOException>(thirdCause).InnerException);
                Assert.Equal(0, channel.OutputAttempts);
                Assert.Equal(0, events.Count);
            }
            else
            {
                foreach (Exception error in new[] { firstCause, secondCause, thirdCause })
                    Assert.Same(cause, Assert.IsType<ChannelOutputShutdownException>(error).InnerException);
                Assert.Equal(1, channel.OutputAttempts);
                Assert.Equal(1, events.Count);
            }
            Assert.Equal(!autoClosed && halfClose, channel.IsOpen);
            Assert.Equal(!autoClosed && halfClose, channel.IsActive);
            Assert.False(channel.IsWritable);
            Assert.True(channel.AllOnOwner);
            foreach (var buffer in buffers) Assert.Equal(0, buffer.ReferenceCount);
            Assert.Equal(0, channel.OutboundBuffer.TotalPendingWriteBytes);
            Assert.Equal(autoClosed || !halfClose ? 1 : 0, channel.NativeCloses);
        }
        finally { channel.Write.TrySetResult(); await channel.CloseAsync(); await loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task OutputShutdownRejectsReentrantWritesBeforeCallingTheNativePrimitive()
    {
        var loop = new DefaultEventLoop();
        var channel = new ProbeChannel(true);
        var message = Unpooled.Buffer(1).WriteByte(1);
        Task reentrant = null;
        channel.DuringShutdown = () => reentrant = channel.WriteAndFlushAsync(message);
        try
        {
            await channel.RegisterAsync(loop);
            await channel.ShutdownAsync();
            await Assert.ThrowsAsync<IOException>(() => reentrant);
            Assert.Equal(0, message.ReferenceCount);
            Assert.True(channel.IsActive);
            Assert.False(channel.IsWritable);
        }
        finally { await channel.CloseAsync(); await loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task NativeShutdownFailureStillReleasesTheQueueAndFiresTheOutputEvent()
    {
        var loop = new DefaultEventLoop();
        var failure = new IOException("shutdown");
        var channel = new ProbeChannel(true) { ShutdownFailure = failure };
        var events = new OutputEvents();
        channel.Pipeline.AddLast(events);
        var buffer = Unpooled.Buffer(1).WriteByte(1);
        try
        {
            await channel.RegisterAsync(loop);
            Task write = channel.WriteAsync(buffer);
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => channel.ShutdownAsync()));
            await Assert.ThrowsAsync<ChannelOutputShutdownException>(() => write);
            Assert.Equal(0, buffer.ReferenceCount);
            Assert.Equal(1, events.Count);
            Assert.True(channel.IsOpen);
            Assert.False(channel.IsWritable);
        }
        finally { await channel.CloseAsync(); await loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5)); }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public Task AReadRequestedBeforeConnectIsHonoredWithAutoReadDisabled(bool adaptive) =>
        SocketChannelNotYetConnectedSourceTest.ReadBeforeConnect(adaptive, false).WaitAsync(TestContext.Current.CancellationToken);

    private sealed class ProbeChannel : AbstractChannel
    {
        private readonly bool _halfClose;
        internal readonly TaskCompletionSource Write = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int OutputAttempts, NativeCloses;
        internal bool AllOnOwner = true;
        internal Action DuringShutdown;
        internal Exception ShutdownFailure;
        internal ProbeChannel(bool halfClose) : base(null, new ChannelMetadata(false)) { _halfClose = halfClose; SetActive(true); }
        internal Task ShutdownAsync() => OnLoopAsync(() => ShutdownOutput(null));
        public override EndPoint LocalAddress => null;
        public override EndPoint RemoteAddress => null;
        protected override Task DoBindAsync(EndPoint address, CancellationToken token) => Task.CompletedTask;
        protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken token) => Task.CompletedTask;
        protected override void DoClose() { AllOnOwner &= EventLoop.InEventLoop(); NativeCloses++; }
        protected override void DoBeginRead() { }
        protected override Task DoWrite(object message) => Write.Task;
        protected override void DoShutdownOutput()
        {
            AllOnOwner &= EventLoop.InEventLoop();
            OutputAttempts++;
            DuringShutdown?.Invoke();
            if (ShutdownFailure != null) throw ShutdownFailure;
            if (!_halfClose) base.DoShutdownOutput();
        }
    }
    private sealed class OutputEvents : ChannelInboundHandlerAdapter
    {
        internal int Count;
        public override void UserEventTriggered(ChannelHandlerContext context, object evt)
        { if (evt is ChannelOutputShutdownEvent) Count++; }
    }
}
#pragma warning restore xUnit1051
