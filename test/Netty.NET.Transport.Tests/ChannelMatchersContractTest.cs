using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Groups;

namespace Netty.NET.Transport.Tests;

// Supplemental source-contract cases for ChannelMatchers; the pinned sources have no ChannelMatchers fixture.
public class ChannelMatchersContractTest
{
    [Fact]
    public void AllMatchesEveryChannelThroughOneSharedMatcher()
    {
        var channel = new Mock<IChannel>().Object;
        Assert.True(ChannelMatchers.All()(channel));
        Assert.True(ChannelMatchers.All()(null));
        Assert.Same(ChannelMatchers.All(), ChannelMatchers.All());
    }

    [Fact]
    public void IsAndIsNotCompareChannelIdentity()
    {
        var a = new Mock<IChannel>().Object;
        var b = new Mock<IChannel>().Object;
        Assert.True(ChannelMatchers.Is(a)(a));
        Assert.False(ChannelMatchers.Is(a)(b));
        Assert.False(ChannelMatchers.IsNot(a)(a));
        Assert.True(ChannelMatchers.IsNot(a)(b));
        Assert.NotSame(ChannelMatchers.Is(a), ChannelMatchers.Is(a));
    }

    [Fact]
    public void InstanceOfMatchersUseTheRuntimeType()
    {
        var child = new Mock<IChannel>().Object;
        var server = new Mock<IServerChannel>().Object;
        Assert.True(ChannelMatchers.IsInstanceOf(typeof(IServerChannel))(server));
        Assert.False(ChannelMatchers.IsInstanceOf(typeof(IServerChannel))(child));
        Assert.False(ChannelMatchers.IsNotInstanceOf(typeof(IServerChannel))(server));
        Assert.True(ChannelMatchers.IsNotInstanceOf(typeof(IServerChannel))(child));
        Assert.True(ChannelMatchers.IsServerChannel()(server));
        Assert.False(ChannelMatchers.IsServerChannel()(child));
        Assert.False(ChannelMatchers.IsNonServerChannel()(server));
        Assert.True(ChannelMatchers.IsNonServerChannel()(child));
        Assert.Same(ChannelMatchers.IsServerChannel(), ChannelMatchers.IsServerChannel());
        Assert.Same(ChannelMatchers.IsNonServerChannel(), ChannelMatchers.IsNonServerChannel());
        // Class.isInstance(null) is false.
        Assert.False(ChannelMatchers.IsInstanceOf(typeof(IChannel))(null));
        // Without the Class<? extends Channel> bound, a non-channel type never matches.
        Assert.False(ChannelMatchers.IsInstanceOf(typeof(string))(child));
    }

    [Fact]
    public void InvertNegatesTheGivenMatcher()
    {
        var channel = new Mock<IChannel>().Object;
        Assert.False(ChannelMatchers.Invert(ChannelMatchers.All())(channel));
        Assert.True(ChannelMatchers.Invert(ChannelMatchers.Invert(ChannelMatchers.All()))(channel));
    }

    [Fact]
    public void ComposeRequiresEveryMatcherInOrder()
    {
        var channel = new Mock<IChannel>().Object;
        var error = Assert.Throws<ArgumentException>(() => ChannelMatchers.Compose());
        Assert.Equal("matchers must at least contain one element", error.Message);
        Func<IChannel, bool> single = ChannelMatchers.Is(channel);
        Assert.Same(single, ChannelMatchers.Compose(single));

        int laterCalls = 0;
        Func<IChannel, bool> later = _ =>
        {
            laterCalls++;
            return true;
        };
        Assert.True(ChannelMatchers.Compose(ChannelMatchers.All(), later)(channel));
        Assert.Equal(1, laterCalls);
        Assert.False(ChannelMatchers.Compose(ChannelMatchers.IsServerChannel(), later)(channel));
        Assert.Equal(1, laterCalls);
    }

    [Fact]
    public async Task GroupOperationsApplyTheMatcher()
    {
        var kept = Channel();
        var closed = Channel();
        var server = new Mock<IServerChannel>();
        server.SetupGet(value => value.Id).Returns(DefaultChannelId.NewInstance());
        server.SetupGet(value => value.CloseFuture).Returns(new TaskCompletionSource().Task);
        server.Setup(value => value.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var group = new DefaultChannelGroup();
        group.Add(kept.Object);
        group.Add(closed.Object);
        group.Add(server.Object);

        await group.CloseAsync(ChannelMatchers.IsNot(kept.Object));

        kept.Verify(value => value.CloseAsync(It.IsAny<CancellationToken>()), Times.Never());
        closed.Verify(value => value.CloseAsync(It.IsAny<CancellationToken>()), Times.Once());
        server.Verify(value => value.CloseAsync(It.IsAny<CancellationToken>()), Times.Once());

        await group.CloseAsync(ChannelMatchers.IsNonServerChannel());
        kept.Verify(value => value.CloseAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    private static Mock<IChannel> Channel()
    {
        var channel = new Mock<IChannel>();
        channel.SetupGet(value => value.Id).Returns(DefaultChannelId.NewInstance());
        channel.SetupGet(value => value.CloseFuture).Returns(new TaskCompletionSource().Task);
        channel.Setup(value => value.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return channel;
    }
}
