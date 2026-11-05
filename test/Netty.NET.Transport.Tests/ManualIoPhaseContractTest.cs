using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.ManualIoEventLoopSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1031 // The owner synchronously drives the pump.
public class ManualIoPhaseContractTest
{
    [Fact]
    public void ACombinedPumpDispatchesIoFirstAndCountsEachChannelOnce()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new LocalChannel();
        var order = new List<string>();
        var ambient = new AsyncLocal<string>();
        try
        {
            loop.Execute(() => order.Add("task"));
            ambient.Value = "submitted";
            _ = loop.SubmitIoAsync(channel, () =>
            {
                order.Add(ambient.Value);
                ambient.Value = "callback";
                _ = loop.SubmitIoAsync(channel, () =>
                {
                    order.Add("next");
                    return (Task.CompletedTask, true);
                });
                return (Task.CompletedTask, true);
            });
            _ = loop.SubmitIoAsync(channel, () =>
            {
                order.Add("read");
                return (Task.CompletedTask, true);
            });
            _ = loop.SubmitIoAsync(channel, () => (Task.CompletedTask, false));

            ambient.Value = "owner";
            Assert.Equal(2, loop.RunNow());
            Assert.Equal(new[] { "submitted", "read", "task" }, order);
            Assert.Equal("owner", ambient.Value);
            Assert.Equal(1, loop.RunNow(-1));
            Assert.Equal(new[] { "submitted", "read", "task", "next" }, order);
        }
        finally { Terminate(loop); }
    }

    [Fact]
    public void UdpReceiveIsCountedOnlyByTheIoPhase()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var receiver = new DatagramChannel(AddressFamily.InterNetwork);
        var reads = new ReadBytes();
        receiver.Pipeline.AddLast(reads);
        try
        {
            loop.RegisterAsync(receiver, cancellation).GetAwaiter().GetResult();
            receiver.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellation).GetAwaiter().GetResult();
            Assert.Equal(0, loop.RunNow(-1)); // Starts the receive without reporting an I/O event.
            using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            Assert.Equal(1, peer.SendTo(new byte[] { 42 }, receiver.LocalAddress));
            Assert.Equal(0, loop.RunNonBlockingTasks(0));
            Assert.Empty(reads.Values);
            bool ran = false;
            loop.Execute(() => ran = true);

            Assert.Equal(1, PumpUntil(loop, () => reads.Values.Count == 1));
            Assert.Equal(42, Assert.Single(reads.Values));
            Assert.False(ran);
            Assert.Equal(1, loop.RunNonBlockingTasks(0));
            Assert.True(ran);
        }
        finally { Terminate(loop); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConnectedStreamCanReadWithoutPumpingOrdinaryWork(bool domain)
    {
        if (domain) DomainSocketSourceTest.RequireDomainSockets();
        var cancellation = TestContext.Current.CancellationToken;
        var family = domain ? AddressFamily.Unix : AddressFamily.InterNetwork;
        var protocol = domain ? ProtocolType.Unspecified : ProtocolType.Tcp;
        using var rawListener = new Socket(family, SocketType.Stream, protocol);
        rawListener.Bind(domain ? DomainSocketSourceTest.NewAddress() : new IPEndPoint(IPAddress.Loopback, 0));
        rawListener.Listen(1);
        using var peer = new Socket(family, SocketType.Stream, protocol);
        peer.Connect(rawListener.LocalEndPoint);
        using var rawAccepted = rawListener.Accept();
        SocketStreamChannel channel = domain ? new DomainSocketChannel(rawAccepted) : new TcpSocketChannel(rawAccepted);
        var reads = new ReadBytes();
        channel.Pipeline.AddLast(reads);
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        try
        {
            loop.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            Assert.Equal(0, loop.RunNow(-1));
            Assert.Equal(1, peer.Send(new byte[] { 73 }));
            bool ran = false;
            loop.Execute(() => ran = true);

            Assert.Equal(1, PumpUntil(loop, () => reads.Values.Count == 1));
            Assert.Equal(73, Assert.Single(reads.Values));
            Assert.False(ran);
            Assert.Same(loop, reads.Owner);
            Assert.Equal(1, loop.RunNonBlockingTasks(0));
            Assert.True(ran);
        }
        finally { Terminate(loop); }
    }

    [Fact]
    public void AnAlreadyQueuedReadFollowsRegistrationToTheNewOwner()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var original = new ManualIoEventLoop(Thread.CurrentThread);
        var current = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new CompletionProbe();
        try
        {
            original.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            var completion = channel.CompleteRead();
            channel.DeregisterAsync(cancellation).GetAwaiter().GetResult();
            current.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();

            Assert.Equal(0, original.RunNow(-1));
            Assert.Null(channel.Owner);
            Assert.Equal(1, current.RunNow(-1));
            Assert.Same(current, channel.Owner);
            completion.WaitAsync(TimeSpan.FromSeconds(2), cancellation).GetAwaiter().GetResult();
        }
        finally
        {
            Terminate(original);
            Terminate(current);
        }
    }

    [Fact]
    public void ShutdownSettlesQueuedCompletionsWithoutDeliveringClosedReads()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new CompletionProbe();
        try
        {
            loop.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            var completion = channel.CompleteRead();

            _ = loop.StopAsync();
            Assert.Equal(0, loop.RunNow());
            Assert.False(channel.IsOpen);
            Assert.Null(channel.Owner);
            completion.WaitAsync(TimeSpan.FromSeconds(2), cancellation).GetAwaiter().GetResult();
        }
        finally { Terminate(loop); }
    }

    private static int PumpUntil(ManualIoEventLoop loop, Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        int count = 0;
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            count += loop.Run(10_000_000, -1);
            Thread.Yield();
        }
        Assert.True(done(), "The socket completion did not arrive on the I/O phase.");
        return count;
    }

    private sealed class ReadBytes : ChannelInboundHandlerAdapter
    {
        internal readonly List<int> Values = new();
        internal object Owner;

        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            try
            {
                Owner = ThreadExecutorMap.CurrentExecutor();
                ByteBuf data = message is DatagramPacket packet ? packet.Content : (ByteBuf)message;
                Values.Add(data.ReadByte());
            }
            finally { ReferenceCountUtil.Release(message); }
        }
    }

    private sealed class CompletionProbe : LocalChannel
    {
        internal object Owner;
        internal Task CompleteRead() => DispatchReadAsync(() => true, () => Owner = ThreadExecutorMap.CurrentExecutor());
    }
}
