using System;
using Netty.NET.Transport.Channels.Nio;
using System.Reflection;
using System.Collections.Generic;
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
        var channel = new NioServerSocketChannel(socket);
        var config = channel.Config;
        var options = config.GetOptions();
        Assert.Equal(NetUtil.SOMAXCONN, Get(config, ChannelOption.SO_BACKLOG));
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
        var channel = new NioSocketChannel(socket);
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
        IChannel channel = listener ? new NioServerDomainSocketChannel(socket) : new NioDomainSocketChannel(socket);
        var config = channel.Config;
        Assert.Contains(ChannelOption.SO_RCVBUF, config.GetOptions().Keys);
        AssertUnsupported(config, ChannelOption.TCP_NODELAY, true);
        AssertUnsupported(config, ChannelOption.SO_REUSEADDR, true);
        AssertUnsupported(config, ChannelOption.SO_KEEPALIVE, true);
        AssertUnsupported(config, ChannelOption.SO_LINGER, 0);
        AssertUnsupported(config, ChannelOption.IP_TOS, 40);
        if (listener)
        {
            Assert.Equal(NetUtil.SOMAXCONN, Get(config, ChannelOption.SO_BACKLOG));
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
        if (domain)
        {
            DomainSocketSourceTest.RequireDomainSockets();
        }
        using var socket = domain ? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            : NewSocket(AddressFamily.InterNetwork);
        IChannel channel = domain ? new NioServerDomainSocketChannel(socket) : new NioServerSocketChannel(socket);
        var config = channel.Config;
        Assert.Equal(NetUtil.SOMAXCONN, Get(config, ChannelOption.SO_BACKLOG));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetOption(ChannelOption.SO_BACKLOG, -1));
        Assert.Equal(NetUtil.SOMAXCONN, Get(config, ChannelOption.SO_BACKLOG));
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
        IChannel channel = listener ? new NioServerSocketChannel(socket) : new NioSocketChannel(socket);
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
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        try
        {
            var server = await new ServerBootstrap().Group(group).ChannelFactory(() => new NioServerSocketChannel(AddressFamily.InterNetwork))
                .ChildOption(ChannelOption.TCP_NODELAY, noDelay).ChildHandler(new ActiveHandler(accepted))
                .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), token);
            using var client = NewSocket(AddressFamily.InterNetwork);
            await client.ConnectAsync(server.LocalAddress, token);
            var child = await accepted.Task.WaitAsync(token);
            Assert.Equal(noDelay, Get(child.Config, ChannelOption.TCP_NODELAY));
            Assert.Equal(noDelay, child.Config.GetOptions()[ChannelOption.TCP_NODELAY]);
            AssertUnsupported(child.Config, ChannelOption.SO_BACKLOG, 16);
        }
        finally
        {
            await group.StopAsync();
        }
    }

    [Theory(Timeout = 10000)]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public async Task ConfiguredListenerOptionsSurviveBindAndAccept(AddressFamily family)
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        var accepted = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        try
        {
            var server = await new ServerBootstrap().Group(group).ChannelFactory(() => new NioServerSocketChannel(family))
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
        finally
        {
            await group.StopAsync();
        }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LazyConfigAccessPreservesNativeOptionsSetAfterChildConstruction(bool noDelay)
    {
        var group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
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
        finally
        {
            await group.StopAsync();
        }
    }

    [Theory]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public void LingerDisablesNegativeValuesAndClampsEnabledSeconds(AddressFamily family)
    {
        using var socket = NewSocket(family);
        var config = new NioSocketChannel(socket).Config;
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
        IChannel channel = listener ? new NioServerSocketChannel(socket) : new NioSocketChannel(socket);
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
        var config = new NioSocketChannel(socket).Config;
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
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Linux native IPv6 traffic-class verification requires Linux.");
        }
        using var socket = NewSocket(family);
        if (family == AddressFamily.InterNetworkV6)
        {
            socket.DualMode = dualMode;
        }
        var config = new NioSocketChannel(socket).Config;
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
            else
            {
                actual = (int)socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService);
            }
            Assert.Equal(value, actual);
            Assert.Equal(actual, Get(config, ChannelOption.IP_TOS));
            Assert.Equal(actual, config.GetOptions()[ChannelOption.IP_TOS]);
            if (dualMode)
            {
                Assert.Equal(value, (int)socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService));
            }
        }
    }

    // DefaultSocketChannelConfig accessors read and write the native socket; getOption/setOption
    // dispatch the standard options to the same accessors.
    [Theory]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public void StreamAccessorsRoundTripThroughTheNativeSocket(AddressFamily family)
    {
        using var socket = NewSocket(family);
        var channel = new NioSocketChannel(socket);
        var config = Assert.IsType<DefaultSocketChannelConfig>(channel.Config);
        Assert.IsAssignableFrom<IDuplexChannelConfig>(channel.Config);
        Assert.False(channel.Config is IServerSocketChannelConfig);

        Assert.Same(config, config.SetTcpNoDelay(false));
        Assert.False(config.IsTcpNoDelay());
        Assert.False(socket.NoDelay);
        Assert.False(Get(config, ChannelOption.TCP_NODELAY));
        Assert.True(config.SetOption(ChannelOption.TCP_NODELAY, true));
        Assert.True(config.IsTcpNoDelay());
        Assert.True(socket.NoDelay);

        Assert.Same(config, config.SetKeepAlive(true));
        Assert.True(config.IsKeepAlive());
        Assert.NotEqual(0, (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive));
        Assert.True(Get(config, ChannelOption.SO_KEEPALIVE));
        Assert.True(config.SetOption(ChannelOption.SO_KEEPALIVE, false));
        Assert.False(config.IsKeepAlive());
        Assert.Equal(0, (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive));

        Assert.Same(config, config.SetReuseAddress(true));
        Assert.True(config.IsReuseAddress());
        Assert.NotEqual(0, (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress));
        Assert.True(Get(config, ChannelOption.SO_REUSEADDR));
        Assert.True(config.SetOption(ChannelOption.SO_REUSEADDR, false));
        Assert.False(config.IsReuseAddress());

        Assert.Same(config, config.SetSoLinger(5));
        Assert.Equal(5, config.GetSoLinger());
        Assert.True(socket.LingerState.Enabled);
        Assert.Equal(5, socket.LingerState.LingerTime);
        Assert.Equal(5, Get(config, ChannelOption.SO_LINGER));
        Assert.True(config.SetOption(ChannelOption.SO_LINGER, -1));
        Assert.Equal(-1, config.GetSoLinger());
        Assert.False(socket.LingerState.Enabled);

        Assert.Same(config, config.SetReceiveBufferSize(32768));
        Assert.Equal(socket.ReceiveBufferSize, config.GetReceiveBufferSize());
        Assert.Equal(config.GetReceiveBufferSize(), Get(config, ChannelOption.SO_RCVBUF));
        Assert.Same(config, config.SetSendBufferSize(32768));
        Assert.Equal(socket.SendBufferSize, config.GetSendBufferSize());
        Assert.Equal(config.GetSendBufferSize(), Get(config, ChannelOption.SO_SNDBUF));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetReceiveBufferSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetSendBufferSize(0));

        Assert.Equal(config.GetTrafficClass(), Get(config, ChannelOption.IP_TOS));
        // Non-Linux IPv6 traffic-class policy is not reviewed; Winsock rejects IP_TOS on an IPv6 stream.
        if (family == AddressFamily.InterNetwork || OperatingSystem.IsLinux())
        {
            Assert.Same(config, config.SetTrafficClass(0x28));
            Assert.Equal(0x28, config.GetTrafficClass());
            Assert.Equal(0x28, Get(config, ChannelOption.IP_TOS));
            if (family == AddressFamily.InterNetwork)
                Assert.Equal(0x28, (int)socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetTrafficClass(256));

        Assert.False(config.IsAllowHalfClosure());
        Assert.Same(config, config.SetAllowHalfClosure(true));
        Assert.True(config.IsAllowHalfClosure());
        Assert.True(Get(config, ChannelOption.ALLOW_HALF_CLOSURE));
        Assert.True(config.SetOption(ChannelOption.ALLOW_HALF_CLOSURE, false));
        Assert.False(config.IsAllowHalfClosure());

        Assert.Same(config, config.SetPerformancePreferences(0, 1, 2));
    }

    // DefaultServerSocketChannelConfig accessors read and write the listening socket and the
    // backlog; getOption/setOption dispatch the standard options to the same accessors.
    [Theory]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public void ListenerAccessorsRoundTripThroughTheNativeSocket(AddressFamily family)
    {
        using var socket = NewSocket(family);
        var channel = new NioServerSocketChannel(socket);
        var config = Assert.IsType<DefaultServerSocketChannelConfig>(channel.Config);
        Assert.False(channel.Config is ISocketChannelConfig);

        Assert.Equal(NetUtil.SOMAXCONN, config.GetBacklog());
        Assert.Same(config, config.SetBacklog(32));
        Assert.Equal(32, config.GetBacklog());
        Assert.Equal(32, Get(config, ChannelOption.SO_BACKLOG));
        Assert.True(config.SetOption(ChannelOption.SO_BACKLOG, 64));
        Assert.Equal(64, config.GetBacklog());
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetBacklog(-1));
        Assert.Equal(64, config.GetBacklog());

        Assert.Same(config, config.SetReuseAddress(true));
        Assert.True(config.IsReuseAddress());
        Assert.NotEqual(0, (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress));
        Assert.True(Get(config, ChannelOption.SO_REUSEADDR));
        Assert.True(config.SetOption(ChannelOption.SO_REUSEADDR, false));
        Assert.False(config.IsReuseAddress());
        Assert.Equal(0, (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress));

        Assert.Same(config, config.SetReceiveBufferSize(32768));
        Assert.Equal(socket.ReceiveBufferSize, config.GetReceiveBufferSize());
        Assert.Equal(config.GetReceiveBufferSize(), Get(config, ChannelOption.SO_RCVBUF));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.SetReceiveBufferSize(0));

        Assert.Same(config, config.SetPerformancePreferences(0, 1, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DomainChannelsUseTheMatchingConfigRole(bool listener)
    {
        DomainSocketSourceTest.RequireDomainSockets();
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        if (listener)
        {
            var config = Assert.IsType<DefaultServerSocketChannelConfig>(new NioServerDomainSocketChannel(socket).Config);
            Assert.Equal(NetUtil.SOMAXCONN, config.GetBacklog());
            Assert.Same(config, config.SetBacklog(8));
            Assert.Equal(8, Get(config, ChannelOption.SO_BACKLOG));
            Assert.Equal(socket.ReceiveBufferSize, config.GetReceiveBufferSize());
            Assert.Equal(config.GetReceiveBufferSize(), Get(config, ChannelOption.SO_RCVBUF));
        }
        else
        {
            var config = Assert.IsType<DefaultSocketChannelConfig>(new NioDomainSocketChannel(socket).Config);
            Assert.Same(config, config.SetAllowHalfClosure(true));
            Assert.True(Get(config, ChannelOption.ALLOW_HALF_CLOSURE));
            Assert.Equal(socket.SendBufferSize, config.GetSendBufferSize());
            Assert.Equal(config.GetSendBufferSize(), Get(config, ChannelOption.SO_SNDBUF));
        }
    }

    // DefaultSocketChannelConfig wraps java.net.SocketException in ChannelException. A TCP-level
    // option on a UDP socket fails natively; a disposed socket keeps ObjectDisposedException
    // (UnsupportedOptionsDoNotTouchADisposedSocket).
    [Fact]
    public void AccessorsWrapNativeSocketErrorsInChannelException()
    {
        using var tcp = NewSocket(AddressFamily.InterNetwork);
        using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var config = new DefaultSocketChannelConfig(new NioSocketChannel(tcp), udp);
        var error = Assert.Throws<ChannelException>(() => config.SetTcpNoDelay(true));
        Assert.IsType<SocketException>(error.InnerException);
        error = Assert.Throws<ChannelException>(() => config.SetOption(ChannelOption.TCP_NODELAY, true));
        Assert.IsType<SocketException>(error.InnerException);
    }

    private static Socket NewSocket(AddressFamily family)
    {
        return new(family, SocketType.Stream, ProtocolType.Tcp);
    }

    private static T Get<T>(IChannelConfig config, ChannelOption<T> option)
    {
        Assert.True(config.TryGetOption(option, out T value));
        return value;
    }

    private static void AssertUnsupported<T>(IChannelConfig config, ChannelOption<T> option, T value)
    {
        Assert.DoesNotContain(option, config.GetOptions().Keys);
        Assert.False(config.TryGetOption(option, out _));
        Assert.False(config.SetOption(option, value));
    }
    private sealed class ActiveHandler(TaskCompletionSource<IChannel> accepted) : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            accepted.TrySetResult(ctx.Channel);
            ctx.FireChannelActive();
        }
    }
    private sealed class NativeOptionListener(bool noDelay) : NioServerSocketChannel(AddressFamily.InterNetwork)
    {
        // Changes Nagle on each accepted socket after its child channel was constructed.
        protected override int DoReadMessages(List<object> buf)
        {
            int start = buf.Count;
            int read = base.DoReadMessages(buf);
            for (int i = start; i < buf.Count; i++)
                ((Socket)typeof(AbstractNioChannel).GetMethod("JavaChannel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(buf[i], null)).NoDelay = noDelay;
            return read;
        }
    }
}
