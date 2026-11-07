using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class LinuxListenerOptionsContractTest
{
    [Fact]
    public async Task ListenerAppliesFastOpenQueueAndWaitsForDataWithDeferAccept()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux socket options require Linux.");
        var token = TestContext.Current.CancellationToken;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var group = new DefaultEventLoopGroup(2);
        var child = new AcceptedChild();
        try
        {
            var listener = await new ServerBootstrap().Group(group).ChannelFactory(() => new TcpServerSocketChannel(socket))
                .Option(ChannelOption.TCP_FASTOPEN, 10)
                .Option(ChannelOption.TCP_DEFER_ACCEPT, 10)
                .ChildHandler(child)
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), token);
            Assert.Equal(10, LinuxSocketOptions.GetInt(socket, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpFastOpen));

            using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await peer.ConnectAsync(listener.LocalAddress, token);
            await Assert.ThrowsAsync<TimeoutException>(() => child.Accepted.Task.WaitAsync(TimeSpan.FromMilliseconds(150), token));
            Assert.Equal(1, await peer.SendAsync(new byte[] { 1 }, SocketFlags.None, token));
            await child.Accepted.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        }
        finally { await group.StopAsync(); }
    }

    private sealed class AcceptedChild : ChannelInboundHandlerAdapter
    {
        internal readonly TaskCompletionSource Accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void ChannelActive(ChannelHandlerContext context)
        {
            Accepted.TrySetResult();
            base.ChannelActive(context);
        }
    }
}
