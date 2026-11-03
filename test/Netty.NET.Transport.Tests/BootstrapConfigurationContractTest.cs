using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Resolver;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent group cleanup.
public class BootstrapConfigurationContractTest
{
    [Fact]
    public async Task FactoryFailureFaultsTheReturnedTaskWithoutFabricatingAChannel()
    {
        var exception = new InvalidOperationException("newChannel crash");
        var group = new DefaultEventLoopGroup(1);
        try
        {
            var bootstrap = new Bootstrap().Group(group).Handler(new ChannelInboundHandlerAdapter()).ChannelFactory(() => throw exception);
            Task<IChannel> future = null;
            Assert.Null(Record.Exception(() => { future = bootstrap.ConnectAsync(LocalAddress.ANY); }));
            Assert.NotNull(future);
            // Should fail with the RuntimeException.
            Assert.Same(exception, await Assert.ThrowsAsync<InvalidOperationException>(() => future.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)));
            Assert.True(future.IsFaulted);
            Assert.True(future.IsCompleted);
        }
        finally { await group.StopAsync(); }
    }
    [Fact]
    public async Task ClientConfigIsLiveButMapsAndClonesAreIndependentReadOnlySnapshots()
    {
        var group = new DefaultEventLoopGroup(1);
        var owner = group.Next();
        var key = AttributeKey<string>.ValueOf(Guid.NewGuid().ToString());
        var handler = new ChannelInboundHandlerAdapter();
        Func<IChannel> factory = () => new LocalChannel();
        var local = new LocalAddress("config-local");
        var remote = new LocalAddress("config-remote");
        var bootstrap = new Bootstrap().Group(group).ChannelFactory(factory).Handler(handler).LocalAddress(local).RemoteAddress(remote)
            .Option(ChannelOption.AUTO_READ, true).Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 100).Attr(key, "old");
        try
        {
            var config = bootstrap.Config;
            Assert.Same(config, bootstrap.Config);
            Assert.Same(group, config.Group); Assert.Same(factory, config.ChannelFactory); Assert.Same(handler, config.Handler);
            Assert.Same(local, config.LocalAddress); Assert.Same(remote, config.RemoteAddress);
            var options = config.Options; var attributes = config.Attributes;
            Assert.Throws<NotSupportedException>(() => ((IDictionary<ChannelOption, object>)options)[ChannelOption.AUTO_READ] = false);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<IAttributeKey, object>)attributes)[key] = "overwrite");
            var clone = bootstrap.Clone(owner);
            bootstrap.Option(ChannelOption.AUTO_READ, false).Attr(key, "new");
            Assert.Equal(true, options[ChannelOption.AUTO_READ]); Assert.Equal("old", attributes[key]);
            Assert.Equal(false, config.Options[ChannelOption.AUTO_READ]); Assert.Equal("new", config.Attributes[key]);
            Assert.Equal(true, clone.Config.Options[ChannelOption.AUTO_READ]); Assert.Equal("old", clone.Config.Attributes[key]);
            Assert.Same(owner, clone.Config.RegistrationEventLoop);
            Assert.Equal(new ChannelOption[] { ChannelOption.AUTO_READ, ChannelOption.CONNECT_TIMEOUT_MILLIS }, options.Keys);
            Assert.Contains("resolver:", config.ToString()); Assert.Contains("localAddress:", config.ToString());
            Assert.Contains("remoteAddress:", config.ToString()); Assert.Contains("options:", config.ToString()); Assert.Contains("attrs:", config.ToString());
            bootstrap.DisableResolver(); Assert.Null(config.Resolver); Assert.DoesNotContain("resolver:", config.ToString());
            bootstrap.Resolver<IPEndPoint>(null);
            Assert.Same(DefaultAddressResolverGroup.INSTANCE, config.Resolver);
            Assert.Contains("resolver:", config.ToString());
        }
        finally { await group.StopAsync(); }
    }
    [Fact]
    public async Task ServerConfigSeparatesParentChildAndCloneStateWithoutExposingMutableMaps()
    {
        var parent = new DefaultEventLoopGroup(1);
        var child = new DefaultEventLoopGroup(1);
        var key = AttributeKey<string>.ValueOf(Guid.NewGuid().ToString());
        var handler = new ChannelInboundHandlerAdapter();
        var childHandler = new ChannelInboundHandlerAdapter();
        var bootstrap = new ServerBootstrap().Group(parent, child).Channel<LocalServerChannel>().Handler(handler).ChildHandler(childHandler)
            .Option(ChannelOption.AUTO_READ, true).ChildOption(ChannelOption.AUTO_READ, false).Attr(key, "parent").ChildAttr(key, "child");
        try
        {
            var config = bootstrap.Config; var clone = bootstrap.Clone();
            Assert.Same(config, bootstrap.Config); Assert.Same(parent, config.Group); Assert.Same(child, config.ChildGroup);
            Assert.Same(handler, config.Handler); Assert.Same(childHandler, config.ChildHandler);
            var options = config.ChildOptions; var attributes = config.ChildAttributes;
            Assert.Throws<NotSupportedException>(() => ((IDictionary<ChannelOption, object>)options).Clear());
            Assert.Throws<NotSupportedException>(() => ((IDictionary<IAttributeKey, object>)attributes).Clear());
            bootstrap.ChildOption(ChannelOption.AUTO_READ, true).ChildAttr(key, null).Attr(key, "updated");
            Assert.Equal(false, options[ChannelOption.AUTO_READ]); Assert.Equal("child", attributes[key]);
            Assert.Equal(true, config.ChildOptions[ChannelOption.AUTO_READ]); Assert.Empty(config.ChildAttributes);
            Assert.Equal(false, clone.Config.ChildOptions[ChannelOption.AUTO_READ]); Assert.Equal("child", clone.Config.ChildAttributes[key]);
            Assert.Equal("parent", clone.Config.Attributes[key]); Assert.Equal("updated", config.Attributes[key]);
            Assert.Contains("childGroup:", config.ToString()); Assert.Contains("childOptions:", config.ToString());
            Assert.Contains("childHandler:", config.ToString());
            Assert.Equal("ServerBootstrapConfig()", new ServerBootstrap().Config.ToString());
        }
        finally { await parent.StopAsync(); await child.StopAsync(); }
    }
}
