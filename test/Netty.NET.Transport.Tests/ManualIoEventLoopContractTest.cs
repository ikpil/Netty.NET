using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.ManualIoEventLoopSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1031 // The synchronous checks pump the loop on its owning thread.
public class ManualIoEventLoopContractTest
{
    [Fact(Timeout = 10000)]
    public async Task ACallerPumpedLoopOwnsTcpCompletionsAndClosesBothChannels()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(null);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new Thread(() =>
        {
            try
            {
                loop.SetOwningThread(Thread.CurrentThread);
                while (!loop.IsTerminated()) loop.Run(0);
                finished.SetResult();
            }
            catch (Exception error) { finished.SetException(error); }
        }) { IsBackground = true };
        owner.Start();

        var echo = new EchoByte(loop);
        try
        {
            var listener = await new ServerBootstrap().Group(loop).Channel<TcpServerSocketChannel>()
                .ChildHandler(echo).BindAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellation);
            using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await peer.ConnectAsync(listener.LocalAddress, cancellation);
            Assert.Equal(1, await peer.SendAsync(new byte[] { 42 }, SocketFlags.None, cancellation));
            var data = new byte[1];
            Assert.Equal(1, await peer.ReceiveAsync(data, SocketFlags.None, cancellation));
            Assert.Equal(42, data[0]);
            var observed = await echo.Observed.Task.WaitAsync(cancellation);
            Assert.Same(loop, observed.Executor);
            Assert.Same(owner, observed.Thread);
            Assert.Same(listener, observed.Channel.Parent);

            await loop.StopAsync().WaitAsync(cancellation);
            Assert.False(listener.IsOpen);
            Assert.False(observed.Channel.IsOpen);
        }
        finally
        {
            _ = loop.StopAsync();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellation);
        }
    }

    [Fact]
    public void GracefulShutdownDrainsWorkCancelsFutureTimersAndClosesAdmission()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var ticker = Ticker.NewMockTicker();
        var loop = new ManualIoEventLoop(Thread.CurrentThread, ticker: ticker);
        try
        {
            var scheduled = loop.ScheduleAsync(() => Assert.Fail("Future work ran during shutdown."),
                TimeSpan.FromMinutes(1), cancellation);
            var termination = loop.ShutdownGracefullyAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
            bool ran = false;
            loop.Execute(() => ran = true);
            Assert.Equal(1, loop.RunNow());
            Assert.True(ran);
            Assert.True(scheduled.IsCanceled);
            Assert.False(termination.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => loop.AwaitTermination(TimeSpan.Zero));
            ticker.Advance(2_000_000_001);
            Assert.Equal(0, loop.RunNow());
            Assert.True(termination.IsCompletedSuccessfully);
            Assert.Throws<RejectedExecutionException>(() => loop.Execute(() => { }));
            var rejected = loop.ScheduleAsync(() => { }, TimeSpan.Zero, cancellation);
            Assert.True(rejected.IsFaulted);
            Assert.Throws<RejectedExecutionException>(() => rejected.GetAwaiter().GetResult());
        }
        finally { Terminate(loop); }
    }

    [Fact]
    public void BlockingRunReturnsAtTheScheduledDeadline()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        try
        {
            var watch = Stopwatch.StartNew();
            var scheduled = loop.ScheduleAsync(() => 42, TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
            Assert.Equal(1, loop.Run(3_000_000_000));
            Assert.Equal(42, scheduled.GetAwaiter().GetResult());
            Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(2));
        }
        finally { Terminate(loop); }
    }

    [Fact]
    public void RegistrationMovesToTheNewOwnerBeforeTheFirstLoopTerminates()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var first = new ManualIoEventLoop(Thread.CurrentThread);
        var second = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new LocalChannel();
        try
        {
            first.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            channel.DeregisterAsync(cancellation).GetAwaiter().GetResult();
            second.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            Terminate(first);
            Assert.True(channel.IsRegistered);
            Assert.True(channel.IsOpen);
            Assert.Same(second, channel.EventLoop);
            Terminate(second);
            Assert.False(channel.IsOpen);
        }
        finally
        {
            Terminate(first);
            Terminate(second);
        }
    }

    private sealed class EchoByte(ManualIoEventLoop loop) : SimpleChannelInboundHandler<ByteBuf>
    {
        internal readonly TaskCompletionSource<(IChannel Channel, IEventExecutor Executor, Thread Thread)> Observed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message)
        {
            Observed.TrySetResult((context.Channel, ThreadExecutorMap.CurrentExecutor(), Thread.CurrentThread));
            Assert.True(loop.InEventLoop());
            _ = context.WriteAndFlushAsync(Unpooled.WrappedBuffer(new[] { message.ReadByte() }));
        }
    }
}
