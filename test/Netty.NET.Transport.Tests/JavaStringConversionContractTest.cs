using System;
using System.Net;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Groups;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Java string conversion (JLS 5.1.11) renders null as "null" and booleans as "true"/"false"; C# concatenation
// renders null as "" and bool as "True". Expected texts were printed by the pinned classes on JDK 21:
//   UnpooledByteBufAllocator(directByDefault: false)
//   DefaultAddressedEnvelope(<sender> => null, msg)
//   address null not supported
public class JavaStringConversionContractTest
{
    [Fact]
    public void AllocatorToStringRendersBooleanLikeTheSource()
    {
        Assert.Equal("UnpooledByteBufAllocator(directByDefault: false)", new UnpooledByteBufAllocator(false).ToString());
        Assert.Equal("UnpooledByteBufAllocator(directByDefault: true)", new UnpooledByteBufAllocator(true).ToString());
    }

    [Fact]
    public void EnvelopeToStringRendersMissingRecipientAsNull()
    {
        var sender = new IPEndPoint(IPAddress.Loopback, 1);
        var recipient = new IPEndPoint(IPAddress.Loopback, 2);
        Assert.Equal("DefaultAddressedEnvelope(" + sender + " => null, msg)",
            new DefaultAddressedEnvelope<string, IPEndPoint>("msg", null, sender).ToString());
        Assert.Equal("DefaultAddressedEnvelope(=> " + recipient + ", msg)",
            new DefaultAddressedEnvelope<string, IPEndPoint>("msg", recipient).ToString());
    }

    // toString() texts name classes with StringUtil.simpleClassName(this) (nested types as Outer$Inner, here
    // Outer+Inner), Class.getName() for handler types, and simpleClassName(ChannelHandlerContext.class) for contexts.
    // JDK 21: "UnpooledByteBufAllocator$UnpooledByteBufAllocatorMetric(usedHeapMemory: 0; usedDirectMemory: 0)",
    // "DefaultChannelGroup(name: g, size: 0)",
    // "ServerBootstrap(ServerBootstrapConfig(group: DefaultEventLoopGroup, childGroup: DefaultEventLoopGroup))",
    // "ChannelHandlerContext(ChannelInboundHandlerAdapter#0, [id: 0xembedded, L:embedded - R:embedded])" and
    // "...{(ChannelInboundHandlerAdapter#0 = io.netty.channel.ChannelInboundHandlerAdapter), (... #1 = ...)}".
    [Fact]
    public void ToStringUsesSourceClassNames()
    {
        Assert.Equal("UnpooledByteBufAllocator+UnpooledByteBufAllocatorMetric(usedHeapMemory: 0; usedDirectMemory: 0)",
            new UnpooledByteBufAllocator(false).Metric.ToString());
        Assert.StartsWith("PooledByteBufAllocatorMetric(usedHeapMemory: ", new PooledByteBufAllocator(false).Metric.ToString());
        var holder = new DefaultByteBufHolder(Unpooled.Buffer(4));
        Assert.StartsWith("DefaultByteBufHolder(", holder.ToString());
        holder.Release();
        Assert.Equal("DefaultChannelGroup(name: g, size: 0)", new DefaultChannelGroup("g").ToString());

        var group = new DefaultEventLoopGroup(1);
        try
        {
            Assert.Equal("ServerBootstrap(ServerBootstrapConfig(group: DefaultEventLoopGroup, childGroup: DefaultEventLoopGroup))",
                new ServerBootstrap().Group(group, group).ToString());
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void PipelineAndContextToStringFollowTheSource()
    {
        var channel = new EmbeddedChannel(new ChannelInboundHandlerAdapter(), new ChannelInboundHandlerAdapter());
        try
        {
            const string handler = "Netty.NET.Transport.Channels.ChannelInboundHandlerAdapter";
            // CLR: simpleClassName spells the nested EmbeddedChannelPipeline Outer+Inner where the JVM prints
            // EmbeddedChannel$EmbeddedChannelPipeline.
            Assert.Equal("EmbeddedChannel+EmbeddedChannelPipeline{(ChannelInboundHandlerAdapter#0 = " + handler
                         + "), (ChannelInboundHandlerAdapter#1 = " + handler + ")}", channel.Pipeline.ToString());
            Assert.Equal("ChannelHandlerContext(ChannelInboundHandlerAdapter#0, [id: 0xembedded, L:embedded - R:embedded])",
                channel.Pipeline.FirstContext.ToString());
            Assert.Equal("EmbeddedChannel+EmbeddedChannelPipeline{}", new EmbeddedChannel().Pipeline.ToString());
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void ProtocolFamilyOfNullNamesTheNullAddress()
    {
        Assert.Equal("address null not supported",
            Assert.Throws<ArgumentException>(() => InternetProtocolFamily.Of(null)).Message);
    }
}
