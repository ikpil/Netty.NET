using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class TcpFastOpenContractTest
{
    [Fact(Timeout = 10000)]
    public async Task CachedCookieSendsInitialDataInTheSyn()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Requires Linux TCP_INFO.");
        int flags = int.Parse(File.ReadAllText("/proc/sys/net/ipv4/tcp_fastopen").Trim());
        if ((flags & 3) != 3) Assert.Skip("Requires kernel Fast Open client and server support.");
        var token = TestContext.Current.CancellationToken;
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.FastOpen, 5);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(2);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            for (int connection = 0; connection < 2; connection++)
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var channel = await NewClient(group, socket).Option(ChannelOption.TCP_FASTOPEN_CONNECT, true).RegisterAsync(token);
                var write = channel.WriteAsync(Unpooled.Buffer(8).WriteLong(42), token);
                await channel.ConnectAsync(listener.LocalEndPoint, cancellationToken: token);
                using var accepted = await listener.AcceptAsync(token);
                await write.WaitAsync(TimeSpan.FromSeconds(3), token);
                byte[] data = new byte[8];
                int offset = 0;
                while (offset < data.Length)
                {
                    int count = await accepted.ReceiveAsync(data.AsMemory(offset), SocketFlags.None, token);
                    Assert.True(count > 0);
                    offset += count;
                }
                if (connection == 1)
                {
                    const int tcpInfo = 11;
                    const int optionsOffset = 5;
                    const byte synData = 1 << 5;
                    byte[] info = new byte[256];
                    int length = socket.GetRawSocketOption((int)SocketOptionLevel.Tcp, tcpInfo, info);
                    Assert.True(length > optionsOffset);
                    Assert.True((info[optionsOffset] & synData) != 0, "TCP_INFO did not report data acknowledged in the SYN.");
                }
                await channel.CloseAsync(token);
            }
        }
        finally { await group.StopAsync(); }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialPrefixAndRemainderAreSentExactlyOnce(bool enabled)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Exercises the Linux native Fast Open path.");
        var token = TestContext.Current.CancellationToken;
        var local = new IPEndPoint(IPAddress.Loopback, 0);
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(local);
        listener.Listen(1);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var group = new DefaultEventLoopGroup(1);
        var payload = new byte[8192];
        Random.Shared.NextBytes(payload);
        var buffer = Unpooled.WrappedBuffer(payload);
        try
        {
            var channel = await NewClient(group, socket).Option(ChannelOption.TCP_FASTOPEN_CONNECT, enabled).RegisterAsync(token);
            var write = channel.WriteAsync(buffer, token);
            await channel.ConnectAsync(listener.LocalEndPoint, cancellationToken: token);
            using var accepted = await listener.AcceptAsync(token);
            Assert.Equal(enabled, (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.FastOpen) != 0);
            byte[] received = new byte[payload.Length];
            int offset = 0;
            if (enabled)
            {
                offset = await accepted.ReceiveAsync(received.AsMemory(), SocketFlags.None, token)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(3), token);
                Assert.InRange(offset, 1, payload.Length - 1);
                Assert.Equal(1, LinuxSocketOptions.GetInt(socket, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpFastOpenConnect));
            }
            Assert.False(write.IsCompleted);
            channel.Flush();
            await write.WaitAsync(TimeSpan.FromSeconds(3), token);
            while (offset < received.Length)
            {
                int count = await accepted.ReceiveAsync(received.AsMemory(offset), SocketFlags.None, token)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(3), token);
                Assert.True(count > 0);
                offset += count;
            }
            Assert.Equal(payload, received);
            Assert.Equal(0, buffer.ReferenceCount);
        }
        finally { await group.StopAsync(); }
    }

    [Fact(Timeout = 10000)]
    public async Task NoInitialDataUsesAnOrdinaryConnect()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Exercises the Linux Fast Open selection.");
        var token = TestContext.Current.CancellationToken;
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            await NewClient(group, socket).Option(ChannelOption.TCP_FASTOPEN_CONNECT, true)
                .ConnectAsync(listener.LocalEndPoint, cancellationToken: token);
            using var accepted = await listener.AcceptAsync(token);
            Assert.Equal(0, LinuxSocketOptions.GetInt(socket, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpFastOpenConnect));
        }
        finally { await group.StopAsync(); }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedConnectReleasesTheInitialData(bool cancel)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Exercises the Linux Fast Open connection path.");
        var token = TestContext.Current.CancellationToken;
        // Use an endpoint that the successful warm-up connections have not put in the cookie cache.
        var address = IPAddress.Parse(cancel ? "127.0.0.252" : "127.0.0.253");
        var group = new DefaultEventLoopGroup(1);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var server = await new ServerBootstrap().Group(group).ChannelFactory(static () => new TcpServerSocketChannel())
                .ChildHandler(new ChannelInboundHandlerAdapter()).BindAsync(new IPEndPoint(address, 0), token);
            LinuxTcpMd5SourceTest.SetServerKeys(server, LinuxTcpMd5SourceTest.Keys(IPAddress.Loopback, "abc"u8.ToArray()));
            var channel = await NewClient(group, socket)
                .Option(ChannelOption.TCP_MD5SIG, LinuxTcpMd5SourceTest.Keys(address, "def"u8.ToArray()))
                .Option(ChannelOption.TCP_FASTOPEN_CONNECT, true)
                .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 1000).RegisterAsync(token);
            var buffer = Unpooled.Buffer(16).WriteLong(42);
            var write = channel.WriteAsync(buffer, token);
            var connecting = channel.ConnectAsync(server.LocalAddress, cancellationToken: cancellation.Token);
            await channel.EventLoop.SubmitAsync(() => { }, token);
            Assert.False(connecting.IsCompleted);
            Assert.Equal(1, LinuxSocketOptions.GetInt(socket, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpFastOpenConnect));
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
            }
            else
                await Assert.ThrowsAsync<ConnectTimeoutException>(() => connecting);
            await Assert.ThrowsAnyAsync<Exception>(() => write.WaitAsync(TimeSpan.FromSeconds(3), token));
            Assert.False(channel.IsOpen);
            Assert.Equal(0, buffer.ReferenceCount);
        }
        finally { await group.StopAsync(); }
    }

    private static Bootstrap NewClient(DefaultEventLoopGroup group, Socket socket) =>
        new Bootstrap().Group(group).ChannelFactory(() => new TcpSocketChannel(socket))
            .Handler(new ChannelInboundHandlerAdapter());
}
