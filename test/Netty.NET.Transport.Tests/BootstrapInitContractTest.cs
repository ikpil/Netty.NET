using System;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
namespace Netty.NET.Transport.Tests;

/// <summary>
/// CLR supplementary contracts for AbstractBootstrap/Bootstrap/ServerBootstrap initialization in the pinned Netty
/// 4.2.19.Final sources. The messages, the unknown-option outcome, the configured local address and the generated
/// acceptor name were observed by running the unmodified pinned sources on JDK 21
/// (docs/transport-porting-manifest.json, validation.bootstrapInitEvidence).
/// </summary>
#pragma warning disable xUnit1051 // Independently owned event loop groups.
public class BootstrapInitContractTest
{
    [Fact]
    public async Task ValidationAndBuilderMessagesFollowTheSource()
    {
        var group = new DefaultEventLoopGroup(1);
        try
        {
            Assert.Equal("group not set", Assert.Throws<InvalidOperationException>(() => new Bootstrap().Validate()).Message);
            Assert.Equal("channel or channelFactory not set",
                Assert.Throws<InvalidOperationException>(() => new Bootstrap().Group(group).Validate()).Message);
            Assert.Equal("handler not set", Assert.Throws<InvalidOperationException>(
                () => new Bootstrap().Group(group).Channel<LocalChannel>().Validate()).Message);
            Assert.Equal("childHandler not set", Assert.Throws<InvalidOperationException>(
                () => new ServerBootstrap().Group(group).Channel<LocalServerChannel>().Validate()).Message);
            Assert.Equal("group set already", Assert.Throws<InvalidOperationException>(
                () => new Bootstrap().Group(group).Group(group)).Message);
            Assert.Equal("group set already", Assert.Throws<InvalidOperationException>(
                () => new ServerBootstrap().Group(group).Group(group, group)).Message);
            Assert.Equal("channelFactory set already", Assert.Throws<InvalidOperationException>(
                () => new Bootstrap().Channel<LocalChannel>().Channel<LocalChannel>()).Message);
            Assert.Equal("localAddress not set", Assert.Throws<InvalidOperationException>(
                () => { _ = new Bootstrap().Group(group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter()).BindAsync(); }).Message);
            Assert.Equal("remoteAddress not set", Assert.Throws<InvalidOperationException>(
                () => { _ = new Bootstrap().Group(group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(); }).Message);
            Assert.Equal("handler", Assert.Throws<ArgumentNullException>(() => new Bootstrap().Handler(null)).ParamName);
            Assert.Equal("childHandler", Assert.Throws<ArgumentNullException>(() => new ServerBootstrap().ChildHandler(null)).ParamName);
            Assert.Equal("childGroup", Assert.Throws<ArgumentNullException>(() => new ServerBootstrap().Group(group, null)).ParamName);
            Assert.Equal("option", Assert.Throws<ArgumentNullException>(() => new Bootstrap().Option<object>(null, 1)).ParamName);
            Assert.Equal("childOption", Assert.Throws<ArgumentNullException>(() => new ServerBootstrap().ChildOption<object>(null, 1)).ParamName);
            Assert.Equal("childKey", Assert.Throws<ArgumentNullException>(() => new ServerBootstrap().ChildAttr<object>(null, 1)).ParamName);
        }
        finally
        {
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task AnUnknownOptionIsLoggedAndRegistrationStillSucceeds()
    {
        var group = new DefaultEventLoopGroup(1);
        try
        {
            // DefaultChannelConfig.setOption returns false for SO_BROADCAST on a local channel.
            IChannel channel = await new Bootstrap().Group(group).Channel<LocalChannel>()
                .Option(ChannelOption.SO_BROADCAST, true)
                .Handler(new ChannelInboundHandlerAdapter()).RegisterAsync();
            Assert.True(channel.IsRegistered);
            Assert.True(channel.IsOpen);
            await channel.CloseAsync();
        }
        finally
        {
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task ConnectWithOnlyARemoteAddressUsesTheConfiguredLocalAddress()
    {
        var group = new DefaultEventLoopGroup(1);
        try
        {
            var serverAddress = new LocalAddress(typeof(BootstrapInitContractTest));
            IChannel server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .ChildHandler(new ChannelInboundHandlerAdapter()).BindAsync(serverAddress);
            await server.EventLoop.SubmitAsync(() => { });
            Assert.Equal(new[] { "ServerBootstrap+ServerBootstrapAcceptor#0", "DefaultChannelPipeline$TailContext#0" },
                server.Pipeline.Names());
            var clientAddress = new LocalAddress(typeof(BootstrapInitContractTest));
            IChannel client = await new Bootstrap().Group(group).Channel<LocalChannel>()
                .LocalAddress(clientAddress).Handler(new ChannelInboundHandlerAdapter())
                .ConnectAsync(serverAddress);
            Assert.Equal(clientAddress, client.LocalAddress);
            await client.CloseAsync();
            await server.CloseAsync();
        }
        finally
        {
            await group.StopAsync();
        }
    }

    [Fact]
    public async Task ToStringWrapsTheConfig()
    {
        var group = new DefaultEventLoopGroup(1);
        try
        {
            var bootstrap = new Bootstrap().Group(group).Channel<LocalChannel>();
            // AbstractBootstrap.toString: simpleClassName(this) + '(' + config() + ')'.
            Assert.Equal("Bootstrap(" + bootstrap.Config + ")", bootstrap.ToString());
            var serverBootstrap = new ServerBootstrap().Group(group).Channel<LocalServerChannel>();
            Assert.Equal("ServerBootstrap(" + serverBootstrap.Config + ")", serverBootstrap.ToString());
        }
        finally
        {
            await group.StopAsync();
        }
    }
}
