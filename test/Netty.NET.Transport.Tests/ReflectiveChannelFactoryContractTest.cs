using System;
using System.Reflection;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Internal;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

// Supplemental source-contract cases for ReflectiveChannelFactory, Bootstrap.channel(Class) and ChannelUtils;
// the pinned sources have no fixture for these classes.
public class ReflectiveChannelFactoryContractTest
{
    [Fact]
    public void NewChannelInvokesThePublicNoArgConstructorEachTime()
    {
        var factory = new ReflectiveChannelFactory<IChannel>(typeof(LocalChannel));
        IChannel first = factory.NewChannel();
        IChannel second = factory.NewChannel();
        Assert.IsType<LocalChannel>(first);
        Assert.NotSame(first, second);
        Assert.Equal("ReflectiveChannelFactory(LocalChannel.class)", factory.ToString());
    }

    [Fact]
    public void ConstructorRejectsClassesItCannotInstantiate()
    {
        Assert.Throws<ArgumentNullException>(() => new ReflectiveChannelFactory<IChannel>(null));
        var missing = Assert.Throws<ArgumentException>(() => new ReflectiveChannelFactory<IChannel>(typeof(NoDefaultChannel)));
        Assert.StartsWith("Class ReflectiveChannelFactoryContractTest+NoDefaultChannel does not have a public non-arg constructor",
            missing.Message);
        // The Class<? extends T> bound is a compile-time check in Java.
        Assert.Throws<ArgumentException>(() => new ReflectiveChannelFactory<IServerChannel>(typeof(LocalChannel)));
    }

    [Fact]
    public void ConstructorFailureBecomesChannelException()
    {
        var factory = new ReflectiveChannelFactory<IChannel>(typeof(ThrowingChannel));
        var error = Assert.Throws<ChannelException>(() => factory.NewChannel());
        Assert.Equal("Unable to create Channel from class " + typeof(ThrowingChannel), error.Message);
        var invocation = Assert.IsType<TargetInvocationException>(error.InnerException);
        Assert.Equal("boom", Assert.IsType<InvalidOperationException>(invocation.InnerException).Message);
    }

    [Fact]
    public void BootstrapChannelUsesTheReflectiveFactory()
    {
        Assert.IsType<LocalChannel>(new Bootstrap().Channel(typeof(LocalChannel)).Config.ChannelFactory());
        Assert.IsType<LocalServerChannel>(new ServerBootstrap().Channel<LocalServerChannel>().Config.ChannelFactory());
        Assert.Throws<ArgumentNullException>(() => new Bootstrap().Channel(null));
        // ServerBootstrap is AbstractBootstrap<ServerBootstrap, ServerChannel>.
        Assert.Throws<ArgumentException>(() => new ServerBootstrap().Channel(typeof(LocalChannel)));
        var failing = new Bootstrap().Channel<ThrowingChannel>().Config.ChannelFactory;
        Assert.Throws<ChannelException>(() => failing());
    }

    [Fact]
    public void ChannelUtilsKeepsTheSourceConstants()
    {
        Assert.Equal(4096, ChannelUtils.MAX_BYTES_PER_GATHERING_WRITE_ATTEMPTED_LOW_THRESHOLD);
        Assert.Equal(int.MaxValue, ChannelUtils.WRITE_STATUS_SNDBUF_FULL);
    }

    private sealed class NoDefaultChannel : LocalChannel
    {
        public NoDefaultChannel(int ignored) { }
    }

    private sealed class ThrowingChannel : LocalChannel
    {
        public ThrowingChannel() => throw new InvalidOperationException("boom");
    }
}
