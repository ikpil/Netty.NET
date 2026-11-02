using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

[CollectionDefinition("Initializer extensions", DisableParallelization = true)]
public class InitializerExtensionCollection { }

#pragma warning disable xUnit1051 // Independent group cleanup.
[Collection("Initializer extensions")]
public class ChannelInitializerExtensionContractTest
{
    [Fact]
    public void RegistrationsAreOptInScopedAndCanBeLogOnly()
    {
        Assert.Empty(ChannelInitializerExtensions.Snapshot());
        var extension = new Recording(0, "extension", []);
        using (ChannelInitializerExtension.Register(extension, invoke: false))
        {
            Assert.Empty(ChannelInitializerExtensions.Snapshot());
            Assert.Throws<ArgumentException>(() => ChannelInitializerExtension.Register(extension));
        }
        var registration = ChannelInitializerExtension.Register(extension);
        var snapshot = ChannelInitializerExtensions.Snapshot();
        Assert.Same(extension, Assert.Single(snapshot));
        registration.Dispose(); registration.Dispose();
        Assert.Empty(ChannelInitializerExtensions.Snapshot());
        Assert.Same(extension, Assert.Single(snapshot));
    }
    [Fact]
    public async Task PrioritiesFollowSourceDoubleOrderAndOneFailureDoesNotSuppressOtherExtensions()
    {
        var calls = new List<string>();
        var registrations = new List<IDisposable>();
        var group = new DefaultEventLoopGroup(1);
        IChannel channel = null;
        try
        {
            foreach (var extension in new[] { new Recording(double.NaN, "nan", calls), new Recording(0.0, "positive-zero", calls),
                new Recording(-0.0, "negative-zero", calls), new Recording(-1, "negative", calls), new Recording(1, "positive", calls),
                new Recording(-2, "throws", calls, throws: true) }) registrations.Add(ChannelInitializerExtension.Register(extension));
            channel = await new Bootstrap().Group(group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter()).RegisterAsync();
            Assert.Equal(new[] { "throws", "negative", "negative-zero", "positive-zero", "positive", "nan" }, calls);
        }
        finally
        {
            if (channel != null) await channel.CloseAsync();
            await group.StopAsync();
            foreach (var registration in registrations) registration.Dispose();
        }
    }
    [Fact]
    public async Task ExistingListenerRetainsItsSourceExtensionSnapshotForFutureChildren()
    {
        var group = new DefaultEventLoopGroup(1);
        int childCalls = 0;
        IChannel server = null, client = null;
        var extension = new ChildObserver(() => childCalls++);
        var registration = ChannelInitializerExtension.Register(extension);
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>().ChildHandler(new ChannelInboundHandlerAdapter())
                .BindAsync(new LocalAddress(Guid.NewGuid().ToString()));
            registration.Dispose();
            Assert.Empty(ChannelInitializerExtensions.Snapshot());
            client = await new Bootstrap().Group(group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(server.LocalAddress);
            Assert.Equal(1, childCalls);
        }
        finally
        {
            registration.Dispose();
            if (client != null) await client.CloseAsync();
            if (server != null) await server.CloseAsync();
            await group.StopAsync();
        }
    }
    [Fact]
    public async Task AttributeRemovalAndCloneSnapshotsKeepTypedNativeKeys()
    {
        var key = AttributeKey<string>.ValueOf(Guid.NewGuid().ToString());
        var original = new Bootstrap().Attr(key, "old");
        IChannel listener = null, client = null;
        var clone = original.Clone();
        original.Attr(key, null);
        Assert.Empty(original.NewAttributesArray());
        Assert.Equal("old", Assert.Single(clone.NewAttributesArray()).Value);
        var group = new DefaultEventLoopGroup(1);
        IChannel channel = null;
        try
        {
            channel = await original.Group(group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter()).RegisterAsync();
            Assert.False(channel.HasAttr(key));
            var server = new ServerBootstrap().Group(group).Channel<LocalServerChannel>().Attr(key, "remove").ChildAttr(key, "remove")
                .ChildHandler(new ActionChannelInitializer<IChannel>(child => Assert.False(child.HasAttr(key))));
            server.Attr(key, null).ChildAttr(key, null);
            listener = await server.Clone().BindAsync(new LocalAddress(Guid.NewGuid().ToString()));
            Assert.False(listener.HasAttr(key));
            client = await new Bootstrap().Group(group).Channel<LocalChannel>().Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(listener.LocalAddress);
        }
        finally
        {
            if (channel != null) await channel.CloseAsync();
            if (client != null) await client.CloseAsync();
            if (listener != null) await listener.CloseAsync();
            await group.StopAsync();
        }
    }
    [Fact]
    public async Task ConcurrentBuilderSnapshotsKeepOriginalOrderAndTypedAttributeState()
    {
        var key = AttributeKey<string>.ValueOf(Guid.NewGuid().ToString());
        var bootstrap = new Bootstrap().Option(ChannelOption.AUTO_READ, true).Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 1);
        var server = new ServerBootstrap().Option(ChannelOption.AUTO_READ, true).ChildOption(ChannelOption.AUTO_READ, true);
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 2048; i++)
            {
                bootstrap.Option(ChannelOption.AUTO_READ, (i & 1) == 0).Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, i).Attr(key, i % 2 == 0 ? "value" : null);
                server.Option(ChannelOption.AUTO_READ, (i & 1) == 0).ChildOption(ChannelOption.AUTO_READ, (i & 1) == 0).ChildAttr(key, i % 2 == 0 ? "value" : null);
            }
        }, TestContext.Current.CancellationToken);
        for (int i = 0; i < 2048; i++)
        {
            var snapshot = bootstrap.Clone().NewOptionsArray();
            Assert.Equal(2, snapshot.Length);
            Assert.Same(ChannelOption.AUTO_READ, snapshot[0].Key); Assert.IsType<bool>(snapshot[0].Value);
            Assert.Same(ChannelOption.CONNECT_TIMEOUT_MILLIS, snapshot[1].Key); Assert.IsType<int>(snapshot[1].Value);
            foreach (var attribute in bootstrap.NewAttributesArray())
            { Assert.Same(key, attribute.Key); Assert.Equal("value", attribute.Value); }
            server.Clone();
        }
        await writer;
    }
    private sealed class Recording(double priority, string name, List<string> calls, bool throws = false) : ChannelInitializerExtension
    {
        public override double Priority => priority;
        public override void PostInitializeClientChannel(IChannel channel)
        { Assert.False(channel.IsRegistered); calls.Add(name); if (throws) throw new InvalidOperationException("extension failure"); }
    }
    private sealed class ChildObserver(Action observed) : ChannelInitializerExtension
    {
        public override void PostInitializeServerChildChannel(IChannel channel)
        { Assert.False(channel.IsRegistered); observed(); }
    }
}
