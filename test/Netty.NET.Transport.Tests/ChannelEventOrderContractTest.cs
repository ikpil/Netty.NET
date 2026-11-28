using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;
namespace Netty.NET.Transport.Tests;

/// <summary>
/// CLR supplementary contracts for event ordering that the pinned Netty 4.2.19.Final sources define in
/// DefaultChannelPipeline.HeadContext, AbstractChannel.AbstractUnsafe, ChannelOutboundBuffer and
/// ServerBootstrap.ServerBootstrapAcceptor. The expected orders were observed by running the unmodified pinned
/// sources on JDK 21 (see docs/transport-porting-manifest.json, validation.eventOrderEvidence).
/// </summary>
public class ChannelEventOrderContractTest
{
    [Fact]
    public void AutoReadIssuesPipelineReadAfterActiveAndReadComplete()
    {
        var events = new List<string>();
        var channel = new EmbeddedChannel(new ReadRecorder(events), new InboundRecorder(events, recordRegistration: false));
        // HeadContext.channelActive -> readIfIsAutoRead -> channel.read() through the pipeline.
        Assert.Equal(new[] { "active", "read" }, events);
        events.Clear();
        channel.WriteInbound("x");
        // HeadContext.channelReadComplete -> readIfIsAutoRead.
        Assert.Equal(new[] { "readComplete", "read" }, events);
        events.Clear();
        channel.Config.SetAutoRead(false);
        channel.WriteInbound("y");
        Assert.Equal(new[] { "readComplete" }, events);
        events.Clear();
        // DefaultChannelConfig.setAutoRead(true) -> channel.read().
        channel.Config.SetAutoRead(true);
        Assert.Equal(new[] { "read" }, events);
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void UserDefinedWritabilityNotifiesLater()
    {
        var events = new List<string>();
        var channel = new EmbeddedChannel(new InboundRecorder(events));
        events.Clear();
        channel.OutboundBuffer.SetUserDefinedWritability(1, false);
        Assert.Empty(events);
        channel.RunPendingTasks();
        Assert.Equal(new[] { "writability:False" }, events);
        channel.FinishAndReleaseAll();
    }

    [Fact(Timeout = 10000)]
    public async Task BindCompletesBeforeTheDeferredChannelActive()
    {
        var group = new DefaultEventLoopGroup(1);
        var events = new List<string>();
        var server = new LocalServerChannel();
        server.Pipeline.AddLast(new InboundRecorder(events));

        try
        {
            await group.RegisterAsync(server);
            Task bind = null;
            await server.EventLoop.SubmitAsync(() =>
            {
                bind = server.BindAsync(new LocalAddress(typeof(ChannelEventOrderContractTest)));
                Record(events, "bindReturned completed=" + bind.IsCompletedSuccessfully);
            });
            await bind;
            await WaitFor(server, events, "active");
            Assert.Equal(new[] { "registered", "bindReturned completed=True", "active" }, Snapshot(events));
        }
        finally
        {
            await server.CloseAsync();
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task CloseCompletesBeforeTheDeferredInactiveAndUnregistered()
    {
        var group = new DefaultEventLoopGroup(1);
        var events = new List<string>();
        var server = new LocalServerChannel();
        server.Pipeline.AddLast(new InboundRecorder(events));

        try
        {
            await group.RegisterAsync(server);
            await server.BindAsync(new LocalAddress(typeof(ChannelEventOrderContractTest)));
            await WaitFor(server, events, "active");
            Clear(events);
            Task close = null;
            await server.EventLoop.SubmitAsync(() =>
            {
                close = server.CloseAsync();
                Record(events, "closeReturned closeFuture=" + server.CloseFuture.IsCompleted +
                    " close=" + close.IsCompleted);
            });
            await close;
            await WaitFor(server, events, "unregistered");
            Assert.Equal(new[] { "closeReturned closeFuture=True close=True", "inactive", "unregistered" }, Snapshot(events));
        }
        finally
        {
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task DeregisterRunsLater()
    {
        var group = new DefaultEventLoopGroup(1);
        var events = new List<string>();
        var channel = new LocalChannel();
        channel.Pipeline.AddLast(new InboundRecorder(events));

        try
        {
            await group.RegisterAsync(channel);
            Clear(events);
            Task deregister = null;
            await channel.EventLoop.SubmitAsync(() =>
            {
                deregister = channel.DeregisterAsync();
                Record(events, "deregisterReturned registered=" + channel.IsRegistered +
                    " completed=" + deregister.IsCompleted);
            });
            await deregister;
            Assert.Equal(new[] { "deregisterReturned registered=True completed=False", "unregistered" }, Snapshot(events));
            Assert.False(channel.IsRegistered);
        }
        finally
        {
            await channel.CloseAsync();
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task AcceptorPausesAutoReadForOneSecondAfterAnException()
    {
        var group = new DefaultEventLoopGroup(1);
        var events = new List<string>();

        try
        {
            IChannel server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .Handler(new ExceptionRecorder(events))
                .ChildHandler(new ChannelInboundHandlerAdapter())
                .BindAsync(new LocalAddress(typeof(ChannelEventOrderContractTest)));
            // The acceptor is added by a task the initializer queues on the server's event loop.
            await server.EventLoop.SubmitAsync(() =>
            {
            });
            // generateName0: StringUtil.SimpleClassName + "#0" (the JVM spells it ServerBootstrap$ServerBootstrapAcceptor#0).
            Assert.Contains("ServerBootstrap+ServerBootstrapAcceptor#0", server.Pipeline.Names());
            var paused = Stopwatch.StartNew();
            await server.EventLoop.SubmitAsync(() => server.Pipeline.FireExceptionCaught(new Exception("boom")));
            Assert.False(server.Config.IsAutoRead());
            Assert.Equal(new[] { "parentException:boom" }, Snapshot(events));

            while (!server.Config.IsAutoRead())
            {
                Assert.True(paused.Elapsed < TimeSpan.FromSeconds(5), "Auto-read was not re-enabled.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(paused.Elapsed >= TimeSpan.FromMilliseconds(900), "Auto-read resumed after " + paused.Elapsed);
            await server.CloseAsync();
        }
        finally
        {
            await group.StopAsync();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task AFailedChildRegistrationIsForceClosedWithoutAParentException()
    {
        var group = new DefaultEventLoopGroup(1);
        var deadChildGroup = new DefaultEventLoopGroup(1);
        await deadChildGroup.StopAsync();
        var events = new List<string>();

        try
        {
            var address = new LocalAddress(typeof(ChannelEventOrderContractTest));
            IChannel server = await new ServerBootstrap().Group(group, deadChildGroup).Channel<LocalServerChannel>()
                .Handler(new ExceptionRecorder(events))
                .ChildHandler(new ChannelInboundHandlerAdapter())
                .BindAsync(address);
            Task<IChannel> connect = new Bootstrap().Group(group).Channel<LocalChannel>()
                .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(address);

            try
            {
                (await connect).CloseAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            } // The accepted peer was closed; the source reports a refused connection.

            await Task.Delay(300, TestContext.Current.CancellationToken);
            await server.EventLoop.SubmitAsync(() =>
            {
            });
            Assert.Empty(Snapshot(events));
            Assert.True(server.Config.IsAutoRead());
            await server.CloseAsync();
        }
        finally
        {
            await group.StopAsync();
        }
    }

    private static void Record(List<string> events, string value)
    {
        lock (events)
            events.Add(value);
    }

    private static void Clear(List<string> events)
    {
        lock (events)
            events.Clear();
    }

    private static string[] Snapshot(List<string> events)
    {
        lock (events)
            return events.ToArray();
    }

    private static async Task WaitFor(IChannel channel, List<string> events, string value)
    {
        var watch = Stopwatch.StartNew();

        while (Array.IndexOf(Snapshot(events), value) < 0)
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), value + " was not observed.");
            await channel.EventLoop.SubmitAsync(() =>
            {
            });
        }
    }

    private sealed class ReadRecorder(List<string> events) : ChannelOutboundHandlerAdapter
    {
        public override void Read(IChannelHandlerContext ctx)
        {
            Record(events, "read");
            ctx.Read();
        }
    }

    private sealed class InboundRecorder(List<string> events, bool recordRegistration = true) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRegistered(IChannelHandlerContext ctx)
        {
            if (recordRegistration)
            {
                Record(events, "registered");
            }

            ctx.FireChannelRegistered();
        }

        public override void ChannelUnregistered(IChannelHandlerContext ctx)
        {
            Record(events, "unregistered");
            ctx.FireChannelUnregistered();
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            Record(events, "active");
            ctx.FireChannelActive();
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            Record(events, "inactive");
            ctx.FireChannelInactive();
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            Record(events, "readComplete");
            ctx.FireChannelReadComplete();
        }

        public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
        {
            Record(events, "writability:" + ctx.Channel.IsWritable);
            ctx.FireChannelWritabilityChanged();
        }
    }

    private sealed class ExceptionRecorder(List<string> events) : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Record(events, "parentException:" + cause.Message);
            ctx.FireExceptionCaught(cause);
        }
    }
}
