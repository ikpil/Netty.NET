using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Tests;

// Supplemental CLR lifetime tests for the original prepareToClose/close/doClose0 contract.
#pragma warning disable xUnit1031 // The manual loop must be pumped by its owning thread.
public class SocketLingerCloseContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PositiveLingerOffloadsOnlyNativeCloseAndRetainsQueuedOwnership(bool fail)
    {
        using var fixture = new Fixture();
        var buf = Unpooled.Buffer(1).WriteByte(1);
        Task write = fixture.Channel.WriteAsync(buf, TestContext.Current.CancellationToken);
        var cause = new IOException("Native close failed.");
        fixture.Channel.Failure = fail ? cause : null;
        Task promise = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        fixture.WaitForClose();
        Task repeat = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        Assert.False(promise.IsCompleted);
        Assert.False(repeat.IsCompleted);
        Assert.False(fixture.Channel.CloseFuture.IsCompleted);
        Assert.False(write.IsCompleted);
        Assert.Equal(1, buf.RefCnt);
        Assert.Empty(fixture.Events);
        Assert.NotSame(Thread.CurrentThread, fixture.Channel.CloseThread);
        var rejected = Unpooled.Buffer(1).WriteByte(2);
        Assert.ThrowsAny<ClosedChannelException>(() => fixture.Channel.WriteAndFlushAsync(rejected, TestContext.Current.CancellationToken).GetAwaiter().GetResult());
        Assert.Equal(0, rejected.RefCnt);
        bool ran = false;
        fixture.Loop.Execute(() => ran = true);
        fixture.Loop.RunNow();
        Assert.True(ran);

        fixture.Channel.AllowClose.Set();
        // doClose0 completes the Tasks on the close executor; the owner then fails queued writes and runs the
        // deferred deregistration (AbstractUnsafe.close/deregister).
        fixture.PumpUntil(() => promise.IsCompleted && repeat.IsCompleted && write.IsCompleted && fixture.Events.Contains("removed"));
        if (fail)
        {
            Assert.Same(cause, Assert.Throws<IOException>(() => promise.GetAwaiter().GetResult()));
        }
        else
        {
            Assert.True(promise.IsCompletedSuccessfully);
        }
        Assert.True(repeat.IsCompletedSuccessfully);
        Assert.True(fixture.Channel.CloseFuture.IsCompletedSuccessfully);
        Assert.ThrowsAny<ClosedChannelException>(() => write.GetAwaiter().GetResult());
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(1, fixture.Channel.CloseCalls);
        Assert.Equal(new[] { "inactive", "unregistered", "removed" }, fixture.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualShutdownWaitsForTheReservedCloseWithoutBlockingItsOwner(bool immediate)
    {
        using var fixture = new Fixture();
        Task termination = immediate ? fixture.Loop.StopAsync()
            : fixture.Loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        fixture.Loop.RunNow();
        fixture.WaitForClose();
        Assert.False(termination.IsCompleted);
        Assert.False(fixture.Channel.CloseFuture.IsCompleted);
        Assert.Empty(fixture.Events);
        if (immediate)
        {
            Assert.Throws<Netty.NET.Common.Concurrent.RejectedExecutionException>(() => fixture.Loop.Execute(() =>
            {
            }));
        }
        fixture.Channel.AllowClose.Set();
        fixture.PumpUntil(() => termination.IsCompleted);
        Assert.True(termination.IsCompletedSuccessfully);
        Assert.True(fixture.Channel.CloseFuture.IsCompletedSuccessfully);
        Assert.Equal(new[] { "inactive", "unregistered", "removed" }, fixture.Events);
        Assert.Equal(1, fixture.Channel.CloseCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitDeregistrationDefersRemovalUntilNativeCloseCompletes(bool beforeClose)
    {
        using var fixture = new Fixture();
        // AbstractUnsafe.deregister runs later on the owner (#4435), so pump until its Task completes.
        if (beforeClose)
        {
            Deregister(fixture);
        }
        Task promise = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        fixture.WaitForClose();
        if (!beforeClose)
        {
            Deregister(fixture);
        }
        Assert.Equal(new[] { "unregistered" }, fixture.Events);
        fixture.Channel.AllowClose.Set();
        fixture.PumpUntil(() => promise.IsCompleted);
        Assert.True(promise.IsCompletedSuccessfully);
        // fireChannelInactiveAndDeregister only reaches channelInactive through a registered channel's deregister,
        // so an already deregistered channel fires no inactive event. CLR: its pipeline is still removed.
        fixture.PumpUntil(() => fixture.Events.Contains("removed"));
        Assert.Equal(new[] { "unregistered", "removed" }, fixture.Events);
        Assert.Equal(1, fixture.Channel.CloseCalls);
    }

    private static void Deregister(Fixture fixture)
    {
        Task deregister = fixture.Channel.DeregisterAsync(TestContext.Current.CancellationToken);
        fixture.PumpUntil(() => deregister.IsCompleted);
        deregister.GetAwaiter().GetResult();
    }

    [Fact]
    public void AForcedCloseDoesNotStealAnAlreadyRunningNativeClose()
    {
        using var fixture = new Fixture();
        Task promise = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        fixture.WaitForClose();
        fixture.Channel.CloseForcibly();
        Assert.Equal(1, fixture.Channel.CloseCalls);
        Assert.False(promise.IsCompleted);
        Assert.False(fixture.Channel.CloseFuture.IsCompleted);
        fixture.Channel.AllowClose.Set();
        fixture.PumpUntil(() => promise.IsCompleted);
        Assert.True(promise.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void DisabledOrAbortiveLingerClosesOnTheOwner(int soLinger)
    {
        using var fixture = new Fixture(soLinger);
        fixture.Channel.AllowClose.Set();
        Task promise = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        Assert.Same(Thread.CurrentThread, fixture.Channel.CloseThread);
        fixture.PumpUntil(() => promise.IsCompleted);
        Assert.True(promise.IsCompletedSuccessfully);
        Assert.Equal(1, fixture.Channel.CloseCalls);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(0, true, true)]
    public async Task DefaultLoopKeepsOwnerCallbacksAliveAcrossShutdown(int shutdownMode, bool closeFirst, bool deregister)
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await peer.ConnectAsync(listener.LocalEndPoint, cancellation);
        var channel = new Probe(await listener.AcceptAsync(cancellation));
        channel.Config.SetAutoRead(false).SetOption(ChannelOption.SO_LINGER, 1);
        // NioIoHandler supports changing threads, so the loop supports suspension.
        var loop = new SingleThreadIoEventLoop(null, new DefaultThreadFactory(typeof(SingleThreadIoEventLoop)),
            NioIoHandler.NewFactory());
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.Pipeline.AddLast(new Observe(loop, new List<string>(), removed));

        Task Shutdown()
        {
            return shutdownMode == 2 ? loop.StopAsync()
                : loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        }

        try
        {
            await channel.RegisterAsync(loop, cancellation);
            Thread owner = await loop.SubmitAsync(() => Thread.CurrentThread, cancellation);
            if (deregister)
            {
                await channel.DeregisterAsync(cancellation);
            }
            Task promise = closeFirst ? channel.CloseAsync(cancellation) : channel.CloseFuture;
            Task termination = shutdownMode == 0 ? Task.CompletedTask : !closeFirst ? Shutdown() : null;
            Assert.True(channel.CloseEntered.Wait(TimeSpan.FromSeconds(3), cancellation));
            Assert.False(promise.IsCompleted);
            Assert.NotSame(owner, channel.CloseThread);
            if (closeFirst)
            {
                Assert.Same(owner, await loop.SubmitAsync(() => Thread.CurrentThread, cancellation));
                if (deregister)
                {
                    // TrySuspend accepts a request; CanSuspend decides whether the worker may actually exit.
                    Assert.True(await loop.SubmitAsync(loop.TrySuspend, cancellation));
                    Assert.False(owner.Join(TimeSpan.FromMilliseconds(100)));
                }
                if (shutdownMode != 0)
                {
                    termination = Shutdown();
                }
            }
            if (shutdownMode != 0)
            {
                Assert.False(termination.IsCompleted);
            }
            channel.AllowClose.Set();
            await promise.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            await termination.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            // The close Task completes in doClose0; handler removal follows the deferred deregistration.
            await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            Assert.Equal(1, channel.CloseCalls);
        }
        finally
        {
            channel.AllowClose.Set();
            await loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5), cancellation);
        }
    }

    [Fact]
    public void ARejectedCloseSchedulerStillClosesAndReleasesTheOwnerReservation()
    {
        using var fixture = new Fixture();
        fixture.Channel.CloseExecutor = new RejectScheduler();
        fixture.Channel.AllowClose.Set();
        Task promise = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        fixture.PumpUntil(() => promise.IsCompleted && fixture.Events.Contains("removed"));
        Assert.Throws<TaskSchedulerException>(() => promise.GetAwaiter().GetResult());
        Assert.Same(Thread.CurrentThread, fixture.Channel.CloseThread);
        Assert.True(fixture.Channel.CloseFuture.IsCompletedSuccessfully);
        Assert.Equal(1, fixture.Channel.CloseCalls);
        Assert.Equal(new[] { "inactive", "unregistered", "removed" }, fixture.Events);
    }

    [Fact]
    public void AnAlreadyDisposedNativeSocketFallsBackToOwnerClose()
    {
        using var fixture = new Fixture();
        fixture.Channel.DisposeSocket();
        fixture.Channel.AllowClose.Set();
        Task promise = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        fixture.PumpUntil(() => promise.IsCompleted);
        Assert.True(promise.IsCompletedSuccessfully);
        Assert.Same(Thread.CurrentThread, fixture.Channel.CloseThread);
        Assert.Equal(1, fixture.Channel.CloseCalls);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Socket _peer;
        internal readonly Probe Channel;
        internal readonly ManualIoEventLoop Loop = new(Thread.CurrentThread, NioIoHandler.NewFactory());
        internal readonly List<string> Events = new();
        internal Fixture(int soLinger = 1)
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            _peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _peer.Connect(listener.LocalEndPoint);
            Channel = new Probe(listener.Accept());
            Channel.Config.SetAutoRead(false).SetOption(ChannelOption.SO_LINGER, soLinger);
            Channel.Pipeline.AddLast(new Observe(Loop, Events));
            Loop.RegisterAsync(Channel, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }

        internal void WaitForClose()
        {
            Assert.True(Channel.CloseEntered.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        }

        internal void PumpUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "Native close completion did not reach its owner.");
                Loop.RunNow();
                Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            Channel.AllowClose.Set();
            ManualIoEventLoopSourceTest.Terminate(Loop);
            _peer.Dispose();
        }
    }

    private sealed class Probe(Socket socket) : Netty.NET.Transport.Channels.Sockets.NioSocketChannel(socket)
    {
        internal readonly ManualResetEventSlim CloseEntered = new();
        internal readonly ManualResetEventSlim AllowClose = new();
        internal Thread CloseThread;
        internal int CloseCalls;
        internal Exception Failure;
        internal TaskScheduler CloseExecutor;
        internal void DisposeSocket()
        {
            JavaChannel().Dispose();
        }

        protected override TaskScheduler PrepareToClose()
        {
            return CloseExecutor ?? base.PrepareToClose();
        }

        protected override void DoClose()
        {
            Interlocked.Increment(ref CloseCalls);
            CloseThread = Thread.CurrentThread;
            CloseEntered.Set();
            try
            {
                if (!AllowClose.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Test did not release native close.");
                }
                if (Failure != null)
                {
                    throw Failure;
                }
            }
            finally
            {
                base.DoClose();
            }
        }
    }

    private sealed class RejectScheduler : TaskScheduler
    {
        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return Array.Empty<Task>();
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }

        protected override void QueueTask(Task task)
        {
            throw new RejectedExecutionException("Close worker rejected.");
        }
    }

    private sealed class Observe(IEventLoop loop, List<string> events, TaskCompletionSource removed = null) : ChannelInboundHandlerAdapter
    {
        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            Assert.True(loop.InEventLoop());
            events.Add("inactive");
            ctx.FireChannelInactive();
        }

        public override void ChannelUnregistered(IChannelHandlerContext ctx)
        {
            Assert.True(loop.InEventLoop());
            events.Add("unregistered");
            ctx.FireChannelUnregistered();
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            Assert.True(loop.InEventLoop());
            events.Add("removed");
            removed?.SetResult();
        }
    }
}
