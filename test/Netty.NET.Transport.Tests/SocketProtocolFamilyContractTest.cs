using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental CLR cases for SocketProtocolFamily/InternetProtocolFamily and the NIO family constructors.
#pragma warning disable CS0618 // Deprecated InternetProtocolFamily overloads are part of the source contract.
#pragma warning disable xUnit1051 // Independent channel/socket cleanup.
public class SocketProtocolFamilyContractTest
{
    [Theory]
    [InlineData(SocketProtocolFamily.Inet, AddressFamily.InterNetwork)]
    [InlineData(SocketProtocolFamily.Inet6, AddressFamily.InterNetworkV6)]
    [InlineData(SocketProtocolFamily.Unix, AddressFamily.Unix)]
    public void SocketProtocolFamilyRoundTripsTheRuntimeFamily(SocketProtocolFamily family, AddressFamily expected)
    {
        Assert.Equal(expected, family.ToJdkFamily());
        Assert.Equal(family, SocketProtocolFamily.Of(expected));
    }

    [Fact]
    public void UnknownFamiliesAreUnsupported()
    {
        Assert.Throws<NotSupportedException>(() => SocketProtocolFamily.Of(AddressFamily.AppleTalk));
        Assert.Throws<NotSupportedException>(() => ((SocketProtocolFamily)42).ToJdkFamily());
        Assert.Throws<InvalidOperationException>(() => ((InternetProtocolFamily)42).ToSocketProtocolFamily());
        Assert.Throws<InvalidOperationException>(() => ((InternetProtocolFamily)42).Localhost());
    }

    [Fact]
    public void InternetProtocolFamilyKeepsAddressTypeAndIanaNumber()
    {
        Assert.Equal(AddressFamily.InterNetwork, InternetProtocolFamily.IPv4.AddressType());
        Assert.Equal(AddressFamily.InterNetworkV6, InternetProtocolFamily.IPv6.AddressType());
        // IANA Address Family Numbers: 1 = IP (IP version 4), 2 = IP6 (IP version 6).
        Assert.Equal(1, InternetProtocolFamily.IPv4.AddressNumber());
        Assert.Equal(2, InternetProtocolFamily.IPv6.AddressNumber());
        Assert.Equal(SocketProtocolFamily.Inet, InternetProtocolFamily.IPv4.ToSocketProtocolFamily());
        Assert.Equal(SocketProtocolFamily.Inet6, InternetProtocolFamily.IPv6.ToSocketProtocolFamily());
    }

    [Fact]
    public void OfSelectsTheFamilyOfTheAddress()
    {
        Assert.Equal(InternetProtocolFamily.IPv4, InternetProtocolFamily.Of(IPAddress.Parse("192.0.2.1")));
        Assert.Equal(InternetProtocolFamily.IPv6, InternetProtocolFamily.Of(IPAddress.Parse("2001:db8::1")));
        // Java converts an IPv4-mapped IPv6 address to Inet4Address.
        Assert.Equal(InternetProtocolFamily.IPv4, InternetProtocolFamily.Of(IPAddress.Parse("::ffff:192.0.2.1")));
        Assert.Throws<ArgumentException>(() => InternetProtocolFamily.Of(null));
    }

    [Fact]
    public void StreamChannelsOpenTheRequestedFamily()
    {
        using (var inet = new StreamProbe(SocketProtocolFamily.Inet))
        {
            Assert.Equal(AddressFamily.InterNetwork, inet.NativeSocket.AddressFamily);
        }
        using (var deprecated = new StreamProbe(InternetProtocolFamily.IPv4))
        {
            Assert.Equal(AddressFamily.InterNetwork, deprecated.NativeSocket.AddressFamily);
        }
        using (var osDefault = new StreamProbe((SocketProtocolFamily?)null))
        {
            Assert.Equal(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork,
                osDefault.NativeSocket.AddressFamily);
        }
        if (Socket.OSSupportsIPv6)
        {
            using var inet6 = new StreamProbe(SocketProtocolFamily.Inet6);
            Assert.Equal(AddressFamily.InterNetworkV6, inet6.NativeSocket.AddressFamily);
            // OpenJDK clears IPV6_V6ONLY when IPv4 is available.
            Assert.Equal(Socket.OSSupportsIPv4, inet6.NativeSocket.DualMode);
        }
        Assert.Throws<NotSupportedException>(() => new NioSocketChannel(SocketProtocolFamily.Unix));
    }

    [Fact]
    public void ServerChannelsOpenTheRequestedFamily()
    {
        using (var inet = new ServerProbe(SocketProtocolFamily.Inet))
        {
            Assert.Equal(AddressFamily.InterNetwork, inet.NativeSocket.AddressFamily);
        }
        using (var deprecated = new ServerProbe(InternetProtocolFamily.IPv4))
        {
            Assert.Equal(AddressFamily.InterNetwork, deprecated.NativeSocket.AddressFamily);
        }
        if (Socket.OSSupportsIPv6)
        {
            using var inet6 = new ServerProbe(SocketProtocolFamily.Inet6);
            Assert.Equal(AddressFamily.InterNetworkV6, inet6.NativeSocket.AddressFamily);
            Assert.Equal(Socket.OSSupportsIPv4, inet6.NativeSocket.DualMode);
        }
        Assert.Throws<NotSupportedException>(() => new NioServerSocketChannel(SocketProtocolFamily.Unix));
    }

    [Fact]
    public void DatagramChannelRejectsUnixDomainFamily()
    {
        // DatagramChannel.open(ProtocolFamily): "Unix domain sockets are not supported."
        Assert.Throws<NotSupportedException>(() => new NioDatagramChannel(SocketProtocolFamily.Unix));
    }

    [Fact(Timeout = 10000)]
    public async Task Inet6DatagramChannelReceivesIPv4MappedTraffic()
    {
        if (!Socket.OSSupportsIPv6 || !Socket.OSSupportsIPv4) Assert.Skip("A dual-stack host is required.");
        var cancellation = TestContext.Current.CancellationToken;
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var channel = new NioDatagramChannel(SocketProtocolFamily.Inet6);
        try
        {
            channel.Pipeline.AddLast(new Receiver(received));
            await group.RegisterAsync(channel, cancellation);
            await channel.BindAsync(new IPEndPoint(IPAddress.IPv6Any, 0), cancellation);
            int port = ((IPEndPoint)channel.LocalAddress).Port;
            using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            await sender.SendToAsync(Encoding.ASCII.GetBytes("mapped"), new IPEndPoint(IPAddress.Loopback, port), cancellation);
            Assert.Equal("mapped", await received.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation));
        }
        finally
        {
            await channel.CloseAsync();
            await group.StopAsync();
        }
    }

    private sealed class Receiver(TaskCompletionSource<string> received) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket msg)
            => received.TrySetResult(msg.Content.ToString(Encoding.ASCII));
    }

    private sealed class StreamProbe : NioSocketChannel, IDisposable
    {
        internal StreamProbe(SocketProtocolFamily? family) : base(family) { }
        internal StreamProbe(InternetProtocolFamily? family) : base(family) { }
        internal Socket NativeSocket => JavaChannel();
        public void Dispose() => JavaChannel().Dispose();
    }

    private sealed class ServerProbe : NioServerSocketChannel, IDisposable
    {
        internal ServerProbe(SocketProtocolFamily? family) : base(family) { }
        internal ServerProbe(InternetProtocolFamily? family) : base(family) { }
        internal Socket NativeSocket => JavaChannel();
        public void Dispose() => JavaChannel().Dispose();
    }
}
