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
        Assert.False(fixture.Channel.CloseCompletion.IsCompleted);
        Assert.False(write.IsCompleted);
        Assert.Equal(1, buf.ReferenceCount);
        Assert.Empty(fixture.Events);
        Assert.NotSame(Thread.CurrentThread, fixture.Channel.CloseThread);
        var rejected = Unpooled.Buffer(1).WriteByte(2);
        Assert.Throws<IOException>(() => fixture.Channel.WriteAndFlushAsync(rejected, TestContext.Current.CancellationToken).GetAwaiter().GetResult());
        Assert.Equal(0, rejected.ReferenceCount);
        bool ran = false;
        fixture.Loop.Execute(() => ran = true);
        fixture.Loop.RunNow();
        Assert.True(ran);

        fixture.Channel.AllowClose.Set();
        fixture.PumpUntil(() => promise.IsCompleted && repeat.IsCompleted);
        if (fail) Assert.Same(cause, Assert.Throws<IOException>(() => promise.GetAwaiter().GetResult()));
        else Assert.True(promise.IsCompletedSuccessfully);
        Assert.True(repeat.IsCompletedSuccessfully);
        Assert.True(fixture.Channel.CloseCompletion.IsCompletedSuccessfully);
        Assert.Throws<IOException>(() => write.GetAwaiter().GetResult());
        Assert.Equal(0, buf.ReferenceCount);
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
        Assert.False(fixture.Channel.CloseCompletion.IsCompleted);
        Assert.Empty(fixture.Events);
        if (immediate) Assert.Throws<Netty.NET.Common.Concurrent.RejectedExecutionException>(() => fixture.Loop.Execute(() => { }));
        fixture.Channel.AllowClose.Set();
        fixture.PumpUntil(() => termination.IsCompleted);
        Assert.True(termination.IsCompletedSuccessfully);
        Assert.True(fixture.Channel.CloseCompletion.IsCompletedSuccessfully);
        Assert.Equal(new[] { "inactive", "unregistered", "removed" }, fixture.Events);
        Assert.Equal(1, fixture.Channel.CloseCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitDeregistrationDefersRemovalUntilNativeCloseCompletes(bool beforeClose)
    {
        using var fixture = new Fixture();
        if (beforeClose) fixture.Channel.DeregisterAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        Task promise = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        fixture.WaitForClose();
        if (!beforeClose) fixture.Channel.DeregisterAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        Assert.Equal(new[] { "unregistered" }, fixture.Events);
        fixture.Channel.AllowClose.Set();
        fixture.PumpUntil(() => promise.IsCompleted);
        Assert.True(promise.IsCompletedSuccessfully);
        Assert.Equal(new[] { "unregistered", "inactive", "removed" }, fixture.Events);
        Assert.Equal(1, fixture.Channel.CloseCalls);
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
        Assert.False(fixture.Channel.CloseCompletion.IsCompleted);
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
        var loop = deregister
            ? new DefaultEventLoop(null, new ThreadPerTaskExecutor(new DefaultThreadFactory(typeof(DefaultEventLoop))).Execute, true)
            : new DefaultEventLoop();
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.Pipeline.AddLast(new Observe(loop, new List<string>(), removed));
        Task Shutdown() => shutdownMode == 2 ? loop.StopAsync()
            : loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        try
        {
            await channel.RegisterAsync(loop, cancellation);
            Thread owner = await loop.SubmitAsync(() => Thread.CurrentThread, cancellation);
            if (deregister) await channel.DeregisterAsync(cancellation);
            Task promise = closeFirst ? channel.CloseAsync(cancellation) : channel.CloseCompletion;
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
                if (shutdownMode != 0) termination = Shutdown();
            }
            if (shutdownMode != 0) Assert.False(termination.IsCompleted);
            channel.AllowClose.Set();
            await promise.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            await termination.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            Assert.True(removed.Task.IsCompletedSuccessfully);
            Assert.Equal(1, channel.CloseCalls);
        }
        finally
        {
            channel.AllowClose.Set();
            await loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5), cancellation);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AProducerBorrowOutlivesOffloadedCloseAndOptionalLoopTermination(bool stop)
    {
        using var fixture = new Fixture();
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Channel.PendingWrite = completion.Task;
        var buf = Unpooled.Buffer(1).WriteByte(1);
        Task write = fixture.Channel.WriteAndFlushAsync(buf, TestContext.Current.CancellationToken);
        Assert.Equal(2, buf.ReferenceCount);
        Task promise = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        fixture.WaitForClose();
        Assert.Equal(2, buf.ReferenceCount);
        fixture.Channel.AllowClose.Set();
        fixture.PumpUntil(() => promise.IsCompleted);
        Assert.True(promise.IsCompletedSuccessfully);
        Assert.Throws<IOException>(() => write.GetAwaiter().GetResult());
        Assert.Equal(1, buf.ReferenceCount);
        if (stop)
        {
            Task termination = fixture.Loop.StopAsync();
            fixture.PumpUntil(() => termination.IsCompleted);
        }
        completion.SetResult(1);
        Assert.True(SpinWait.SpinUntil(() => buf.ReferenceCount == 0, TimeSpan.FromSeconds(3)));
        Assert.Equal(1, fixture.Channel.CloseCalls);
    }

    [Fact]
    public void ARejectedCloseSchedulerStillClosesAndReleasesTheOwnerReservation()
    {
        using var fixture = new Fixture();
        fixture.Channel.CloseExecutor = new RejectScheduler();
        fixture.Channel.AllowClose.Set();
        Task promise = fixture.Channel.CloseAsync(TestContext.Current.CancellationToken);
        fixture.PumpUntil(() => promise.IsCompleted);
        Assert.Throws<TaskSchedulerException>(() => promise.GetAwaiter().GetResult());
        Assert.Same(Thread.CurrentThread, fixture.Channel.CloseThread);
        Assert.True(fixture.Channel.CloseCompletion.IsCompletedSuccessfully);
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
        internal readonly ManualIoEventLoop Loop = new(Thread.CurrentThread);
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
        internal void WaitForClose() => Assert.True(Channel.CloseEntered.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
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

    private sealed class Probe(Socket socket) : Netty.NET.Transport.Channels.Sockets.TcpSocketChannel(socket)
    {
        internal readonly ManualResetEventSlim CloseEntered = new();
        internal readonly ManualResetEventSlim AllowClose = new();
        internal Thread CloseThread;
        internal int CloseCalls;
        internal Exception Failure;
        internal TaskScheduler CloseExecutor;
        internal Task<int> PendingWrite;
        internal override bool SupportsGatheringWrites => true;
        internal void DisposeSocket() => Socket.Dispose();
        protected override TaskScheduler PrepareToClose() => CloseExecutor ?? base.PrepareToClose();
        protected override Task<int> DoWriteBytes(ByteBuf buf) => PendingWrite ?? base.DoWriteBytes(buf);
        protected override void DoClose()
        {
            Interlocked.Increment(ref CloseCalls);
            CloseThread = Thread.CurrentThread;
            CloseEntered.Set();
            try
            {
                if (!AllowClose.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test did not release native close.");
                if (Failure != null) throw Failure;
            }
            finally { base.DoClose(); }
        }
    }

    private sealed class RejectScheduler : TaskScheduler
    {
        protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
        protected override void QueueTask(Task task) => throw new RejectedExecutionException("Close worker rejected.");
    }

    private sealed class Observe(IEventLoop loop, List<string> events, TaskCompletionSource removed = null) : ChannelInboundHandlerAdapter
    {
        public override void ChannelInactive(ChannelHandlerContext ctx)
        { Assert.True(loop.InEventLoop()); events.Add("inactive"); ctx.FireChannelInactive(); }
        public override void ChannelUnregistered(ChannelHandlerContext ctx)
        { Assert.True(loop.InEventLoop()); events.Add("unregistered"); ctx.FireChannelUnregistered(); }
        public override void HandlerRemoved(ChannelHandlerContext ctx)
        { Assert.True(loop.InEventLoop()); events.Add("removed"); removed?.SetResult(); }
    }
}
