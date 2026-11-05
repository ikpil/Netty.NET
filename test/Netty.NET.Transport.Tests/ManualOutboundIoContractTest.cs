using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.ManualIoEventLoopSourceTest;
using static Netty.NET.Transport.Tests.SocketConnectionAttemptSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1031 // The same physical thread must drive every pump.
public class ManualOutboundIoContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingConnectFinishesDuringIoOnlyPumping(bool refused)
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var listener = BoundStreamSocket();
        if (!refused) listener.Listen(1);
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new PendingSocketConnect(AddressFamily.InterNetwork);
        channel.Config.SetAutoRead(false);
        var handler = new ActiveProbe();
        channel.Pipeline.AddLast(handler);
        try
        {
            loop.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            Task connect = channel.ConnectAsync(listener.LocalEndPoint, cancellationToken: cancellation);
            Assert.True(channel.Started.Task.IsCompletedSuccessfully);
            Assert.False(connect.IsCompleted);
            Task duplicate = channel.ConnectAsync(listener.LocalEndPoint, cancellationToken: cancellation);
            Assert.True(duplicate.IsFaulted);
            var pending = Assert.Throws<SocketException>(() => duplicate.GetAwaiter().GetResult());
            Assert.Equal(SocketError.AlreadyInProgress, pending.SocketErrorCode);
            Assert.True(channel.IsOpen);

            bool ordinary = false;
            loop.Execute(() => ordinary = true);
            channel.Release.SetResult();
            Assert.Equal(1, PumpUntil(loop, () => connect.IsCompleted));
            Assert.False(ordinary);

            if (refused)
            {
                var error = Assert.Throws<SocketException>(() => connect.GetAwaiter().GetResult());
                Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
                Assert.False(channel.IsOpen);
                Assert.Null(handler.Owner);
            }
            else
            {
                Assert.True(connect.IsCompletedSuccessfully);
                Assert.True(channel.IsActive);
                Assert.Same(Thread.CurrentThread, handler.Owner);
                using var accepted = listener.Accept();
            }
            loop.RunNonBlockingTasks(0);
            Assert.True(ordinary);
        }
        finally { Terminate(loop); }
    }

    [Fact]
    public void CancelingAPendingConnectClosesTheSocketWithoutRunningOrdinaryWork()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new PendingSocketConnect(AddressFamily.InterNetwork);
        try
        {
            loop.RegisterAsync(channel, cancellation.Token).GetAwaiter().GetResult();
            Task connect = channel.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 65535),
                cancellationToken: cancellation.Token);
            Assert.True(channel.Started.Task.IsCompletedSuccessfully);
            bool ordinary = false;
            loop.Execute(() => ordinary = true);
            cancellation.Cancel();
            Assert.Equal(1, PumpUntil(loop, () => connect.IsCompleted));
            Assert.True(connect.IsCanceled);
            Assert.False(channel.IsOpen);
            Assert.False(ordinary);
        }
        finally { Terminate(loop); }
    }

    [Fact]
    public void CancellationAfterNativeConnectStillFiresActiveAndClosesTheSocket()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var listener = BoundStreamSocket();
        listener.Listen(1);
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new PendingSocketConnect(AddressFamily.InterNetwork);
        channel.Config.SetAutoRead(false);
        var handler = new ActiveProbe();
        channel.Pipeline.AddLast(handler);
        try
        {
            loop.RegisterAsync(channel, cancellation.Token).GetAwaiter().GetResult();
            Task connect = channel.ConnectAsync(listener.LocalEndPoint, cancellationToken: cancellation.Token);
            channel.Release.SetResult();

            Assert.Equal(0, PumpUntil(loop, () => channel.IsActive));
            Assert.False(connect.IsCompleted);
            cancellation.Cancel();
            Assert.Equal(1, PumpUntil(loop, () => connect.IsCompleted));
            Assert.True(connect.IsCanceled);
            Assert.Same(Thread.CurrentThread, handler.Owner);
            Assert.False(channel.IsOpen);
        }
        finally { Terminate(loop); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingStreamWriteSettlesQueuedEntriesAndReleasesTheBorrow(bool failed)
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var listener = BoundStreamSocket();
        listener.Listen(1);
        using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        peer.Connect(listener.LocalEndPoint);
        using var accepted = listener.Accept();
        var channel = new HeldStream(accepted);
        channel.Config.SetAutoRead(false);
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        try
        {
            loop.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            ByteBuf a = Unpooled.WrappedBuffer(new byte[] { 42 });
            ByteBuf b = Unpooled.WrappedBuffer(new byte[] { 73 });
            Task first = channel.WriteAndFlushAsync(a, cancellation);
            Task second = channel.WriteAndFlushAsync(b, cancellation);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(2, a.ReferenceCount);
            bool ordinary = false;
            loop.Execute(() => ordinary = true);

            if (failed)
            {
                var failure = new IOException("socket send failed");
                channel.Release.SetException(failure);
                Assert.Equal(1, PumpUntil(loop, () => first.IsCompleted && second.IsCompleted));
                Assert.Same(failure, Assert.Throws<IOException>(() => first.GetAwaiter().GetResult()));
                Assert.Same(failure, Assert.Throws<IOException>(() => second.GetAwaiter().GetResult()));
                Assert.False(channel.IsOpen);
            }
            else
            {
                channel.Release.SetResult();
                Assert.Equal(1, PumpUntil(loop, () => first.IsCompleted));
                // The second send may complete synchronously when the writer is rearmed.
                PumpUntil(loop, () => second.IsCompleted);
                Assert.True(first.IsCompletedSuccessfully);
                Assert.True(second.IsCompletedSuccessfully);
                var data = new byte[2];
                for (int offset = 0; offset < data.Length;)
                {
                    int read = peer.ReceiveAsync(data.AsMemory(offset), SocketFlags.None, cancellation)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancellation).GetAwaiter().GetResult();
                    Assert.True(read > 0);
                    offset += read;
                }
                Assert.Equal(new byte[] { 42, 73 }, data);
            }
            Assert.Equal(0, a.ReferenceCount);
            Assert.Equal(0, b.ReferenceCount);
            Assert.False(ordinary);
        }
        finally
        {
            channel.Release.TrySetResult();
            Terminate(loop);
        }
    }

    [Fact]
    public void PendingDatagramWriteFinishesDuringIoOnlyPumping()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new HeldDatagram();
        channel.Config.SetAutoRead(false);
        using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        peer.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        try
        {
            loop.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            channel.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellation).GetAwaiter().GetResult();
            var data = Unpooled.WrappedBuffer(new byte[] { 42 });
            var packet = new DatagramPacket(data, (IPEndPoint)peer.LocalEndPoint);
            var send = channel.WriteAndFlushAsync(packet, cancellation);
            Assert.False(send.IsCompleted);
            bool ordinary = false;
            loop.Execute(() => ordinary = true);
            channel.Release.SetResult();

            Assert.Equal(1, PumpUntil(loop, () => send.IsCompleted));
            Assert.True(send.IsCompletedSuccessfully);
            Assert.Equal(0, data.ReferenceCount);
            var actual = new byte[1];
            Assert.Equal(1, peer.ReceiveAsync(actual.AsMemory(), SocketFlags.None, cancellation).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellation).GetAwaiter().GetResult());
            Assert.Equal(42, actual[0]);
            Assert.False(ordinary);
        }
        finally
        {
            channel.Release.TrySetResult();
            Terminate(loop);
        }
    }

    [Fact]
    public void StoppingTheOwnerReleasesQueueOwnershipBeforeTheNativeWriteReturns()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var listener = BoundStreamSocket();
        listener.Listen(1);
        using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        peer.Connect(listener.LocalEndPoint);
        using var accepted = listener.Accept();
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new HeldStream(accepted);
        channel.Config.SetAutoRead(false);
        try
        {
            loop.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            var buffer = Unpooled.WrappedBuffer(new byte[] { 42 });
            var write = channel.WriteAndFlushAsync(buffer, cancellation);
            Assert.Equal(2, buffer.ReferenceCount);

            Terminate(loop);
            Assert.Throws<IOException>(() => write.WaitAsync(TimeSpan.FromSeconds(2), cancellation).GetAwaiter().GetResult());
            Assert.Equal(1, buffer.ReferenceCount);

            channel.Release.SetResult();
            Assert.True(SpinWait.SpinUntil(() => buffer.ReferenceCount == 0, TimeSpan.FromSeconds(2)));
            Assert.Equal(0, loop.RunNow(-1));
        }
        finally
        {
            channel.Release.TrySetResult();
            Terminate(loop);
        }
    }

    private static int PumpUntil(ManualIoEventLoop loop, Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        int count = 0;
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            count += loop.Run(10_000_000, -1);
            Thread.Yield();
        }
        Assert.True(condition(), "The pending socket operation did not settle during the I/O phase.");
        return count;
    }

    private static Socket BoundStreamSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return socket;
    }

    private sealed class ActiveProbe : ChannelInboundHandlerAdapter
    {
        internal Thread Owner;
        public override void ChannelActive(ChannelHandlerContext context) => Owner = Thread.CurrentThread;
    }

    private sealed class HeldStream(Socket socket) : TcpSocketChannel(socket)
    {
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task DoWriteAsync(object message)
        {
            await Release.Task.ConfigureAwait(false);
            await base.DoWriteAsync(message).ConfigureAwait(false);
        }
    }

    private sealed class HeldDatagram : DatagramChannel
    {
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task DoWriteAsync(object message)
        {
            await Release.Task.ConfigureAwait(false);
            await base.DoWriteAsync(message).ConfigureAwait(false);
        }
    }
}
