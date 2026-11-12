using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental contracts derived from Default{Server,}SocketChannelConfig and
// the JDK Socket traffic-class contract; these are not original JUnit methods.
#pragma warning disable xUnit1051 // Cleanup must not inherit an expired test token.
public class SocketConfigurationContractTest
{
    [Theory]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public void ListenerOptionsRespectTheAcceptingRole(AddressFamily family)
    {
        using var socket = NewSocket(family);
        var channel = new TcpServerSocketChannel(socket);
        var config = channel.Config;
        var options = config.GetOptions();
        Assert.Equal(NetUtil.DefaultListenBacklog, Get(config, ChannelOption.SO_BACKLOG));
        Assert.Contains(ChannelOption.SO_RCVBUF, options.Keys);
        Assert.Contains(ChannelOption.SO_REUSEADDR, options.Keys);
        Assert.Contains(ChannelOption.SO_BACKLOG, options.Keys);
        Assert.Contains(ChannelOption.AUTO_READ, options.Keys);
        AssertUnsupported(config, ChannelOption.SO_SNDBUF, 8192);
        AssertUnsupported(config, ChannelOption.TCP_NODELAY, true);
        AssertUnsupported(config, ChannelOption.SO_KEEPALIVE, true);
        AssertUnsupported(config, ChannelOption.SO_LINGER, 0);
        AssertUnsupported(config, ChannelOption.IP_TOS, 40);
        AssertUnsupported(config, ChannelOption.ALLOW_HALF_CLOSURE, true);
        AssertUnsupported(config, ChannelOption.TCP_FASTOPEN_CONNECT, true);
    }

    [Theory]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public void StreamOptionsRespectTheConnectingRole(AddressFamily family)
    {
        using var socket = NewSocket(family);
        var channel = new TcpSocketChannel(socket);
        var config = channel.Config;
        var options = config.GetOptions();
        Assert.True(Get(config, ChannelOption.TCP_NODELAY));
        Assert.False(Get(config, ChannelOption.ALLOW_HALF_CLOSURE));
        Assert.Contains(ChannelOption.SO_SNDBUF, options.Keys);
        Assert.Contains(ChannelOption.SO_RCVBUF, options.Keys);
        Assert.Contains(ChannelOption.IP_TOS, options.Keys);
        Assert.Contains(ChannelOption.AUTO_READ, options.Keys);
        AssertUnsupported(config, ChannelOption.SO_BACKLOG, 16);
        AssertUnsupported(config, ChannelOption.TCP_FASTOPEN, 16);
        AssertUnsupported(config, ChannelOption.TCP_DEFER_ACCEPT, 16);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DomainOptionsRetainTheirSeparateNativeSurface(bool listener)
    {
        DomainSocketSourceTest.RequireDomainSockets();
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        IChannel channel = listener ? new DomainServerSocketChannel(socket) : new DomainSocketChannel(socket);
        var config = channel.Config;
        Assert.Contains(ChannelOption.SO_RCVBUF, config.GetOptions().Keys);
        AssertUnsupported(config, ChannelOption.TCP_NODELAY, true);
        AssertUnsupported(config, ChannelOption.SO_REUSEADDR, true);
        AssertUnsupported(config, ChannelOption.SO_KEEPALIVE, true);
        AssertUnsupported(config, ChannelOption.SO_LINGER, 0);
        AssertUnsupported(config, ChannelOption.IP_TOS, 40);
        if (listener)
        {
            Assert.Equal(NetUtil.DefaultListenBacklog, Get(config, ChannelOption.SO_BACKLOG));
            AssertUnsupported(config, ChannelOption.SO_SNDBUF, 8192);
            AssertUnsupported(config, ChannelOption.ALLOW_HALF_CLOSURE, true);
        }
        else
        {
            Assert.Contains(ChannelOption.SO_SNDBUF, config.GetOptions().Keys);
            Assert.True(config.SetOption(ChannelOption.ALLOW_HALF_CLOSURE, true));
            Assert.True(Get(config, ChannelOption.ALLOW_HALF_CLOSURE));
            AssertUnsupported(config, ChannelOption.SO_BACKLOG, 16);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BacklogUsesThePlatformDefaultAndPreservesTheValidatedValue(bool domain)
    {
        if (domain) DomainSocketSourceTest.RequireDomainSockets();
        using var socket = domain ? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            : NewSocket(AddressFamily.InterNetwork);
        IChannel channel = domain ? new DomainServerSocketChannel(socket) : new TcpServerSocketChannel(socket);
        var config = channel.Config;
        Assert.Equal(NetUtil.DefaultListenBacklog, Get(config, ChannelOption.SO_BACKLOG));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetOption(ChannelOption.SO_BACKLOG, -1));
        Assert.Equal(NetUtil.DefaultListenBacklog, Get(config, ChannelOption.SO_BACKLOG));
        foreach (int value in new[] { 0, 1, 65536, int.MaxValue })
        {
            Assert.True(config.SetOption(ChannelOption.SO_BACKLOG, value));
            Assert.Equal(value, Get(config, ChannelOption.SO_BACKLOG));
            Assert.Equal(value, config.GetOptions()[ChannelOption.SO_BACKLOG]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedOptionsDoNotTouchADisposedSocket(bool listener)
    {
        using var socket = NewSocket(AddressFamily.InterNetwork);
        IChannel channel = listener ? new TcpServerSocketChannel(socket) : new TcpSocketChannel(socket);
        var config = channel.Config;
        socket.Dispose();
        if (listener)
        {
            Assert.False(config.TryGetOption(ChannelOption.TCP_NODELAY, out _));
            Assert.False(config.SetOption(ChannelOption.TCP_NODELAY, true));
            Assert.True(config.SetOption(ChannelOption.SO_BACKLOG, 32));
            Assert.Equal(32, Get(config, ChannelOption.SO_BACKLOG));
        }
        else
        {
            Assert.False(config.TryGetOption(ChannelOption.SO_BACKLOG, out _));
            Assert.False(config.SetOption(ChannelOption.SO_BACKLOG, 32));
            Assert.True(config.SetOption(ChannelOption.ALLOW_HALF_CLOSURE, true));
            Assert.True(Get(config, ChannelOption.ALLOW_HALF_CLOSURE));
        }
        Assert.Throws<ObjectDisposedException>(() => config.TryGetOption(ChannelOption.SO_RCVBUF, out _));
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedSocketConfigurationDoesNotUndoAnExplicitNagleChoice(bool noDelay)
    {
        var group = new DefaultEventLoopGroup(1);
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        try
        {
            var server = await new ServerBootstrap().Group(group).ChannelFactory(() => new TcpServerSocketChannel(AddressFamily.InterNetwork))
                .ChildOption(ChannelOption.TCP_NODELAY, noDelay).ChildHandler(new ActiveHandler(accepted))
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), token);
            using var client = NewSocket(AddressFamily.InterNetwork);
            await client.ConnectAsync(server.LocalAddress, token);
            var child = await accepted.Task.WaitAsync(token);
            Assert.Equal(noDelay, Get(child.Config, ChannelOption.TCP_NODELAY));
            Assert.Equal(noDelay, child.Config.GetOptions()[ChannelOption.TCP_NODELAY]);
            AssertUnsupported(child.Config, ChannelOption.SO_BACKLOG, 16);
        }
        finally { await group.StopAsync(); }
    }

    [Theory(Timeout = 10000)]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public async Task ConfiguredListenerOptionsSurviveBindAndAccept(AddressFamily family)
    {
        var group = new DefaultEventLoopGroup(1);
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        try
        {
            var server = await new ServerBootstrap().Group(group).ChannelFactory(() => new TcpServerSocketChannel(family))
                .Option(ChannelOption.SO_BACKLOG, 16).Option(ChannelOption.SO_REUSEADDR, true)
                .ChildHandler(new ActiveHandler(accepted))
                .BindAsync(new IPEndPoint(family == AddressFamily.InterNetwork ? IPAddress.Loopback : IPAddress.IPv6Loopback, 0), token);
            Assert.Equal(16, Get(server.Config, ChannelOption.SO_BACKLOG));
            Assert.True(Get(server.Config, ChannelOption.SO_REUSEADDR));
            using var client = NewSocket(family);
            await client.ConnectAsync(server.LocalAddress, token);
            var child = await accepted.Task.WaitAsync(token);
            Assert.Same(server, child.Parent);
            Assert.True(Get(child.Config, ChannelOption.TCP_NODELAY));
        }
        finally { await group.StopAsync(); }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LazyConfigAccessPreservesNativeOptionsSetAfterChildConstruction(bool noDelay)
    {
        var group = new DefaultEventLoopGroup(1);
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        try
        {
            var server = await new ServerBootstrap().Group(group).ChannelFactory(() => new NativeOptionListener(noDelay))
                .ChildHandler(new ActiveHandler(accepted)).BindAsync(new IPEndPoint(IPAddress.Loopback, 0), token);
            using var client = NewSocket(AddressFamily.InterNetwork);
            await client.ConnectAsync(server.LocalAddress, token);
            var child = await accepted.Task.WaitAsync(token);
            Assert.Equal(noDelay, Get(child.Config, ChannelOption.TCP_NODELAY));
        }
        finally { await group.StopAsync(); }
    }

    [Theory]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public void LingerDisablesNegativeValuesAndClampsEnabledSeconds(AddressFamily family)
    {
        using var socket = NewSocket(family);
        var config = new TcpSocketChannel(socket).Config;
        foreach (int value in new[] { 0, 2, 65535, 65536, int.MaxValue, -1, int.MinValue })
        {
            Assert.True(config.SetOption(ChannelOption.SO_LINGER, value));
            var actual = socket.LingerState;
            Assert.Equal(value >= 0, actual.Enabled);
            Assert.Equal(value < 0 ? -1 : Math.Min(value, ushort.MaxValue), Get(config, ChannelOption.SO_LINGER));
        }
    }

    [Theory]
    [InlineData(AddressFamily.InterNetwork, false)]
    [InlineData(AddressFamily.InterNetworkV6, false)]
    [InlineData(AddressFamily.InterNetwork, true)]
    [InlineData(AddressFamily.InterNetworkV6, true)]
    public void BufferSizesRejectNonPositiveValuesBeforeChangingTheSocket(AddressFamily family, bool listener)
    {
        using var socket = NewSocket(family);
        IChannel channel = listener ? new TcpServerSocketChannel(socket) : new TcpSocketChannel(socket);
        var config = channel.Config;
        foreach (var option in listener ? new[] { ChannelOption.SO_RCVBUF } : new[] { ChannelOption.SO_RCVBUF, ChannelOption.SO_SNDBUF })
        {
            int before = Get(config, option);
            foreach (int value in new[] { 0, -1, int.MinValue })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => config.SetOption(option, value));
                Assert.Equal(before, Get(config, option));
            }
            Assert.True(config.SetOption(option, 16384));
            Assert.Equal(ReferenceEquals(option, ChannelOption.SO_RCVBUF) ? socket.ReceiveBufferSize : socket.SendBufferSize, Get(config, option));
        }
    }

    [Theory]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public void InvalidTrafficClassesFailBeforeChangingTheNativeSocket(AddressFamily family)
    {
        using var socket = NewSocket(family);
        var config = new TcpSocketChannel(socket).Config;
        int before = Get(config, ChannelOption.IP_TOS);
        foreach (int value in new[] { int.MinValue, -1, 256, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => config.SetOption(ChannelOption.IP_TOS, value));
            Assert.Equal(before, Get(config, ChannelOption.IP_TOS));
        }
    }

    [Theory]
    [InlineData(AddressFamily.InterNetwork, false)]
    [InlineData(AddressFamily.InterNetworkV6, false)]
    [InlineData(AddressFamily.InterNetworkV6, true)]
    public void LinuxTrafficClassTargetsTheActualProtocolFamily(AddressFamily family, bool dualMode)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux native IPv6 traffic-class verification requires Linux.");
        using var socket = NewSocket(family);
        if (family == AddressFamily.InterNetworkV6) socket.DualMode = dualMode;
        var config = new TcpSocketChannel(socket).Config;
        foreach (int value in new[] { 0x28, 0xb8, 0 })
        {
            Assert.True(config.SetOption(ChannelOption.IP_TOS, value));
            int actual;
            if (family == AddressFamily.InterNetworkV6)
            {
                byte[] native = new byte[sizeof(int)];
                Assert.Equal(sizeof(int), socket.GetRawSocketOption((int)SocketOptionLevel.IPv6, 67, native));
                actual = BitConverter.ToInt32(native);
            }
            else actual = (int)socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService);
            Assert.Equal(value, actual);
            Assert.Equal(actual, Get(config, ChannelOption.IP_TOS));
            Assert.Equal(actual, config.GetOptions()[ChannelOption.IP_TOS]);
            if (dualMode) Assert.Equal(value, (int)socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService));
        }
    }

    private static Socket NewSocket(AddressFamily family) => new(family, SocketType.Stream, ProtocolType.Tcp);
    private static T Get<T>(IChannelConfig config, ChannelOption<T> option)
    { Assert.True(config.TryGetOption(option, out T value)); return value; }
    private static void AssertUnsupported<T>(IChannelConfig config, ChannelOption<T> option, T value)
    {
        Assert.DoesNotContain(option, config.GetOptions().Keys);
        Assert.False(config.TryGetOption(option, out _));
        Assert.False(config.SetOption(option, value));
    }
    private sealed class ActiveHandler(TaskCompletionSource<IChannel> accepted) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(ChannelHandlerContext context)
        { accepted.TrySetResult(context.Channel); context.FireChannelActive(); }
    }
    private sealed class NativeOptionListener(bool noDelay) : TcpServerSocketChannel(AddressFamily.InterNetwork)
    {
        protected override IChannel CreateChild(Socket socket)
        {
            var child = base.CreateChild(socket);
            socket.NoDelay = noDelay;
            return child;
        }
    }
}
