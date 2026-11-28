using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.ManualIoEventLoopSourceTest;

namespace Netty.NET.Transport.Tests;

// Supplemental tests: OP_CONNECT and OP_WRITE readiness of the select()-based NioIoHandler is handled by the I/O
// phase of a ManualIoEventLoop, so outbound operations settle while only I/O is pumped.
#pragma warning disable xUnit1031 // The same physical thread must drive every pump.
public class ManualOutboundIoContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingConnectFinishesDuringIoOnlyPumping(bool refused)
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread, NioIoHandler.NewFactory());
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        if (!refused)
        {
            listener.Listen(1);
        }

        var target = listener.LocalEndPoint;

        if (refused)
        {
            listener.Close();
        }

        var channel = new NioSocketChannel(AddressFamily.InterNetwork);

        try
        {
            loop.RegisterAsync(channel).GetAwaiter().GetResult();
            Task connect = channel.ConnectAsync(target);
            bool ordinary = false;
            loop.Execute(() => ordinary = true);
            PumpUntil(loop, () => connect.IsCompleted, TimeSpan.FromSeconds(10));
            Assert.False(ordinary);

            if (refused)
            {
                var error = Assert.Throws<SocketException>(() => connect.GetAwaiter().GetResult());
                Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
            }
            else
            {
                Assert.True(connect.IsCompletedSuccessfully);
                Assert.True(channel.IsActive);
            }

            Assert.Equal(1, loop.RunNonBlockingTasks(0));
            Assert.True(ordinary);
        }
        finally
        {
            Terminate(loop);
        }
    }

    [Fact]
    public void ASecondConnectWhileOneIsPendingFailsAsAlreadyInProgress()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread, NioIoHandler.NewFactory());
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var channel = new NeverConnecting(AddressFamily.InterNetwork);

        try
        {
            loop.RegisterAsync(channel).GetAwaiter().GetResult();
            Task first = channel.ConnectAsync(listener.LocalEndPoint);
            Task second = channel.ConnectAsync(listener.LocalEndPoint);
            var pending = Assert.Throws<SocketException>(() => second.GetAwaiter().GetResult());
            Assert.Equal(SocketError.AlreadyInProgress, pending.SocketErrorCode);
            Assert.False(first.IsCompleted);
            var close = channel.CloseAsync();
            PumpUntil(loop, () => first.IsCompleted && close.IsCompleted, TimeSpan.FromSeconds(5));
            Assert.ThrowsAny<ClosedChannelException>(() => first.GetAwaiter().GetResult());
        }
        finally
        {
            Terminate(loop);
        }
    }

    [Fact]
    public void CancelingAPendingConnectClosesTheSocket()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread, NioIoHandler.NewFactory());
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var channel = new NeverConnecting(AddressFamily.InterNetwork);
        using var cancellation = new CancellationTokenSource();

        try
        {
            loop.RegisterAsync(channel).GetAwaiter().GetResult();
            Task connect = channel.ConnectAsync(listener.LocalEndPoint, cancellationToken: cancellation.Token);
            cancellation.Cancel();
            // The cancellation listener runs on the event loop, as a ChannelFutureListener of the connect promise.
            PumpUntil(loop, () => connect.IsCompleted && !channel.IsOpen, TimeSpan.FromSeconds(5), tasks: true);
            Assert.True(connect.IsCanceled);
        }
        finally
        {
            Terminate(loop);
        }
    }

    [Fact]
    public void ABackpressuredStreamWriteContinuesOnWriteReadinessDuringIoOnlyPumping()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread, NioIoHandler.NewFactory());
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        peer.Connect(listener.LocalEndPoint);
        var channel = new NioSocketChannel(listener.Accept());
        channel.Config.SetAutoRead(false);
        const int Length = 8 * 1024 * 1024;

        try
        {
            loop.RegisterAsync(channel).GetAwaiter().GetResult();
            channel.Config.SetOption(ChannelOption.SO_SNDBUF, 8192);
            // Winsock may buffer more than SO_SNDBUF; whatever it does not accept continues on OP_WRITE readiness.
            Task write = channel.WriteAndFlushAsync(Unpooled.Buffer(Length).WriteZero(Length));
            var buffer = new byte[64 * 1024];
            long received = 0;
            var clock = Stopwatch.StartNew();

            while (received < Length)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "The backpressured write did not progress.");
                // Only the I/O phase runs: OP_WRITE readiness resumes the flush through forceFlush().
                loop.RunNow(-1);

                if (peer.Poll(1000, SelectMode.SelectRead))
                    received += peer.Receive(buffer);
            }

            PumpUntil(loop, () => write.IsCompleted, TimeSpan.FromSeconds(5));
            Assert.True(write.IsCompletedSuccessfully);
            Assert.Equal(Length, received);
        }
        finally
        {
            Terminate(loop);
        }
    }

    private static void PumpUntil(ManualIoEventLoop loop, Func<bool> condition, TimeSpan timeout, bool tasks = false)
    {
        var clock = Stopwatch.StartNew();

        while (!condition())
        {
            Assert.True(clock.Elapsed < timeout, "The pending socket operation did not settle during the I/O phase.");
            loop.Run(10_000_000, tasks ? 0 : -1);
        }
    }

    // Keeps the non-blocking connect pending, as a SocketChannel.connect that returned false for a peer that never
    // answers.
    private sealed class NeverConnecting(AddressFamily family) : NioSocketChannel(family)
    {
        internal override bool Connect(Socket socket, EndPoint remoteAddress)
        {
            return false;
        }
    }
}
