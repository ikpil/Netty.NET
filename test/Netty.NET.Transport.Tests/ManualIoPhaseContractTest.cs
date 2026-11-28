using Netty.NET.Transport.Channels.Nio;
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
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.ManualIoEventLoopSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1031 // The owner synchronously drives the pump.
public class ManualIoPhaseContractTest
{
    [Fact]
    public void UdpReceiveIsCountedOnlyByTheIoPhase()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(Thread.CurrentThread, NioIoHandler.NewFactory());
        var receiver = new NioDatagramChannel(AddressFamily.InterNetwork);
        var allocator = new ObservedDatagramAllocator();
        receiver.Config.SetRecvByteBufAllocator(allocator);
        var reads = new ReadBytes();
        receiver.Pipeline.AddLast(reads);

        try
        {
            loop.RegisterAsync(receiver, cancellation).GetAwaiter().GetResult();
            receiver.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellation).GetAwaiter().GetResult();
            // AbstractUnsafe.bind invokes channelActive later; HeadContext then issues the auto-read.
            Assert.Equal(1, loop.RunNonBlockingTasks(0));
            Assert.Equal(0, loop.RunNow(-1)); // Starts the receive without reporting an I/O event.
            using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            Assert.Equal(1, peer.SendTo(new byte[] { 42 }, receiver.LocalAddress));
            Assert.Equal(0, loop.RunNonBlockingTasks(0));
            Assert.Empty(reads.Values);
            bool ran = false;
            loop.Execute(() => ran = true);

            Assert.Equal(1, PumpUntil(loop, () => allocator.Prepared));
            Assert.Equal(42, Assert.Single(reads.Values));
            Assert.False(ran);
            Assert.Equal(1, loop.RunNonBlockingTasks(0));
            Assert.True(ran);
        }
        finally
        {
            Terminate(loop);
        }
    }

    private sealed class ObservedDatagramAllocator : DefaultMaxMessagesRecvByteBufAllocator
    {
        internal bool Prepared { get; private set; }
        public override IRecvByteBufAllocator.IHandle NewHandle()
        {
            return new ObservedHandle(this);
        }

        private sealed class ObservedHandle(ObservedDatagramAllocator owner) : MaxMessageHandle(owner)
        {
            public override int Guess()
            {
                return 32;
            }

            public override void LastBytesRead(int bytes)
            {
                base.LastBytesRead(bytes);
                owner.Prepared = true;
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConnectedStreamCanReadWithoutPumpingOrdinaryWork(bool domain)
    {
        if (domain)
        {
            DomainSocketSourceTest.RequireDomainSockets();
        }

        var cancellation = TestContext.Current.CancellationToken;
        var family = domain ? AddressFamily.Unix : AddressFamily.InterNetwork;
        var protocol = domain ? ProtocolType.Unspecified : ProtocolType.Tcp;
        using var rawListener = new Socket(family, SocketType.Stream, protocol);
        rawListener.Bind(domain ? DomainSocketSourceTest.NewAddress() : new IPEndPoint(IPAddress.Loopback, 0));
        rawListener.Listen(1);
        using var peer = new Socket(family, SocketType.Stream, protocol);
        peer.Connect(rawListener.LocalEndPoint);
        using var rawAccepted = rawListener.Accept();
        AbstractNioByteChannel channel = domain ? new NioDomainSocketChannel(rawAccepted) : new NioSocketChannel(rawAccepted);
        var reads = new ReadBytes();
        channel.Pipeline.AddLast(reads);
        var loop = new ManualIoEventLoop(Thread.CurrentThread, NioIoHandler.NewFactory());

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
        finally
        {
            Terminate(loop);
        }
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

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            try
            {
                Owner = ThreadExecutorMap.CurrentExecutor();
                ByteBuf data = msg is DatagramPacket packet ? packet.Content : (ByteBuf)msg;
                Values.Add(data.ReadByte());
            }
            finally
            {
                ReferenceCountUtil.Release(msg);
            }
        }
    }
}
