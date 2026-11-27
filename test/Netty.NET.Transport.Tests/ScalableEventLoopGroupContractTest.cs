using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.MultiThreadIoEventLoopGroupSourceTest;

namespace Netty.NET.Transport.Tests;

public class ScalableEventLoopGroupContractTest
{
    [Fact(Timeout = 10000)]
    public async Task SustainedOwnerWorkResumesAnotherWorker()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var workload = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var group = NewGroup(minimum: 1);
        try
        {
            StartAllExecutors(group);
            WaitForSuspension(group, 1, 2000);
            var active = FindActiveExecutor(group);
            Assert.NotNull(active);

            StartWorkload(active, workload.Token);
            await WaitUntil(() => group.ActiveExecutorCount() == 2 && CountActiveExecutors(group) == 2, cancellation);
            Assert.Contains(group.ExecutorUtilizations(), metric => ReferenceEquals(active, metric.Executor()));

            workload.Cancel();
            WaitForSuspension(group, 1, 2000);
        }
        finally
        {
            workload.Cancel();
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task TailSubmissionRestartsAStoppedWorkerAndItCanSuspendAgain()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var group = NewGroup();
        try
        {
            StartAllExecutors(group);
            var loop = Assert.IsType<SingleThreadIoEventLoop>(group.Iterator().First());
            var originalThread = await loop.SubmitAsync(() => Thread.CurrentThread, cancellation);
            WaitForSuspension(group, 2, 2000);
            await WaitUntil(() => !originalThread.IsAlive, cancellation);

            var tailThread = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
            loop.ExecuteAfterEventLoopIteration(() => tailThread.SetResult(Thread.CurrentThread));
            var resumedThread = await tailThread.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellation);
            Assert.NotSame(originalThread, resumedThread);

            WaitForSuspension(group, 2, 2000);
            await WaitUntil(() => !resumedThread.IsAlive, cancellation);
        }
        finally
        {
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task BootstrapResumesFromZeroAndShutdownClosesTheRegisteredListener()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var group = NewGroup();
        try
        {
            StartAllExecutors(group);
            WaitForSuspension(group, 2, 2000);
            var child = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
            var listener = await new ServerBootstrap().Group(group).Channel<NioServerSocketChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(channel => child.SetResult(channel)))
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellation);
            var owner = Assert.IsType<SingleThreadIoEventLoop>(listener.EventLoop);
            // SingleThreadEventExecutor.trySuspend() only requests suspension; canSuspend() keeps an owner with a
            // registration running, so the listener below still accepts.
            Assert.True(owner.TrySuspend());

            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(listener.LocalAddress, cancellation);
            var accepted = await child.Task.WaitAsync(cancellation);
            Assert.Same(listener, accepted.Parent);

            await group.StopAsync();
            Assert.True(group.IsTerminated());
            Assert.False(listener.IsOpen);
            Assert.False(accepted.IsOpen);
        }
        finally
        {
            await group.StopAsync();
        }
    }

    private static async Task WaitUntil(Func<bool> condition, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(20, deadline.Token);
    }
}
