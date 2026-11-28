/*
 * Copyright 2026 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections.Generic;
using System.Net;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

// Supplemental CLR cases for code ported from the pinned Java sources during the 2026-11-16 comment/contract
// completion (each case names the Java member it pins). They are not mappings of original fixture methods.
public class JavaSourceAlignmentContractTest
{
    private class Animal
    {
    }

    private sealed class Dog : Animal
    {
    }

    // SimpleChannelInboundHandler(Class<? extends I> inboundMessageType, boolean autoRelease)
    private sealed class DogOnlyHandler : SimpleChannelInboundHandler<Animal>
    {
        public readonly List<Animal> Handled = new();

        public DogOnlyHandler() : base(typeof(Dog), false)
        {
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, Animal msg)
        {
            Handled.Add(msg);
        }
    }

    private sealed class WrongTypeHandler : SimpleChannelInboundHandler<Dog>
    {
        public WrongTypeHandler() : base(typeof(Animal))
        {
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, Dog msg)
        {
        }
    }

    [Fact]
    public void SimpleChannelInboundHandlerMatchesTheGivenMessageType()
    {
        var handler = new DogOnlyHandler();
        var channel = new EmbeddedChannel(handler);
        var dog = new Dog();
        var animal = new Animal();
        channel.WriteInbound(dog, animal);
        Assert.Equal(new Animal[] { dog }, handler.Handled);
        Assert.Same(animal, channel.ReadInbound<Animal>());
        Assert.False(channel.Finish());
        Assert.Throws<ArgumentException>(() => new WrongTypeHandler());
    }

    // SimpleUserEventChannelHandler(Class<? extends I> eventType, boolean autoRelease)
    private sealed class DogEventHandler : SimpleUserEventChannelHandler<Animal>
    {
        public readonly List<Animal> Received = new();

        public DogEventHandler() : base(typeof(Dog))
        {
        }

        protected override void EventReceived(IChannelHandlerContext ctx, Animal evt)
        {
            Received.Add(evt);
        }
    }

    private sealed class EventSink : ChannelInboundHandlerAdapter
    {
        public readonly List<object> Events = new();

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            Events.Add(evt);
        }
    }

    [Fact]
    public void SimpleUserEventChannelHandlerMatchesTheGivenEventType()
    {
        var handler = new DogEventHandler();
        var sink = new EventSink();
        var channel = new EmbeddedChannel(handler, sink);
        var dog = new Dog();
        var animal = new Animal();
        channel.Pipeline.FireUserEventTriggered(dog);
        channel.Pipeline.FireUserEventTriggered(animal);
        Assert.Equal(new Animal[] { dog }, handler.Received);
        Assert.Equal(new object[] { animal }, sink.Events);
        Assert.False(channel.Finish());
    }

    private sealed class NotSharableHandler : ChannelInboundHandlerAdapter
    {
        public void Check()
        {
            EnsureNotSharable();
        }
    }

    [Sharable]
    private sealed class SharableHandler : ChannelInboundHandlerAdapter
    {
        public void Check()
        {
            EnsureNotSharable();
        }
    }

    // ChannelHandlerAdapter.ensureNotSharable() and DefaultChannelPipeline.checkMultiplicity(ChannelHandler)
    [Fact]
    public void HandlerMultiplicityUsesTheSourceMessages()
    {
        var shared = new SharableHandler();
        Assert.True(shared.IsSharable);
        var e = Assert.Throws<InvalidOperationException>(shared.Check);
        Assert.Equal("ChannelHandler " + typeof(SharableHandler).FullName + " is not allowed to be shared", e.Message);

        var handler = new NotSharableHandler();
        Assert.False(handler.IsSharable);
        handler.Check();
        var first = new EmbeddedChannel(handler);
        var second = new EmbeddedChannel();
        var added = Assert.Throws<ChannelPipelineException>(() => second.Pipeline.AddLast(handler));
        Assert.Equal(typeof(NotSharableHandler).FullName +
            " is not a @Sharable handler, so can't be added or removed multiple times.", added.Message);
        Assert.False(first.Finish());
        Assert.False(second.Finish());
    }

    // ChannelInboundHandlerAdapter.exceptionCaught is @Skip like the other forwarding methods.
    [Fact]
    public void InboundAdapterExceptionCaughtIsSkippable()
    {
        Assert.Equal(0, ChannelHandlerMask.Mask(new ChannelInboundHandlerAdapter()) & ChannelHandlerMask.MaskExceptionCaught);
    }

    // ChannelException.newStatic(String, Class, String) and StacklessChannelException
    [Fact]
    public void NewStaticChannelExceptionKeepsMessageAndOrigin()
    {
        ChannelException e = ChannelException.NewStatic("too many channels (max: 1)", typeof(JavaSourceAlignmentContractTest), "test()");
        Assert.Equal("too many channels (max: 1)", e.Message);
        Assert.IsAssignableFrom<ChannelException>(e);
        Assert.Contains(nameof(JavaSourceAlignmentContractTest), e.StackTrace ?? string.Empty);
    }

    // LocalChannelRegistry.register(Channel, LocalAddress, SocketAddress)
    [Fact]
    public void LocalChannelRegistryUsesTheSourceMessages()
    {
        var address = new LocalAddress(typeof(JavaSourceAlignmentContractTest));
        var first = new EmbeddedChannel();
        var second = new EmbeddedChannel();
        Assert.Same(address, LocalChannelRegistry.Register(first, null, address));

        try
        {
            Assert.Equal("already bound", Assert.Throws<ChannelException>(() => LocalChannelRegistry.Register(first, address, address)).Message);
            Assert.Equal("address already in use by: " + first,
                Assert.Throws<ChannelException>(() => LocalChannelRegistry.Register(second, null, address)).Message);
            Assert.Equal("address already in use by: " + first,
                Assert.Throws<ChannelException>(() => LocalChannelRegistry.Register(first, null, address)).Message);
            Assert.Equal("unsupported address type: IPEndPoint",
                Assert.Throws<ChannelException>(() => LocalChannelRegistry.Register(second, null, new IPEndPoint(IPAddress.Loopback, 1))).Message);
            Assert.Null(LocalChannelRegistry.Get(new IPEndPoint(IPAddress.Loopback, 1)));
            Assert.Same(first, LocalChannelRegistry.Get(address));
        }
        finally
        {
            LocalChannelRegistry.Unregister(address);
        }

        Assert.Null(LocalChannelRegistry.Get(address));
    }

    // LocalAddress(Channel), LocalAddress(String) and compareTo(LocalAddress)
    [Fact]
    public void LocalAddressFollowsTheSourceFormat()
    {
        var channel = new EmbeddedChannel();
        var ephemeral = new LocalAddress(channel);
        Assert.Matches("^E:[0-9a-f]{8}$", ephemeral.Id);
        Assert.Equal("local:" + ephemeral.Id, ephemeral.ToString());
        Assert.Equal(((channel.GetHashCode() & 0xFFFFFFFFL) | 0x100000000L).ToString("x").Substring(1), ephemeral.Id.Substring(2));
        Assert.Equal("abc", new LocalAddress(" 	ABC").Id);
        Assert.Equal("Param 'id' must not be empty", Assert.Throws<ArgumentException>(() => new LocalAddress("  ")).Message);
        Assert.Throws<ArgumentNullException>(() => new LocalAddress("a").CompareTo(null));
        Assert.False(channel.Finish());
    }

    // EventLoop extends EventLoopGroup: an event loop is its own next() and can be a bootstrap group.
    [Fact]
    public void EventLoopIsAnEventLoopGroup()
    {
        IEventLoop loop = new EmbeddedEventLoop();
        IEventLoopGroup group = loop;
        Assert.Same(loop, group.Next());
    }

    // DatagramPacket extends DefaultAddressedEnvelope<ByteBuf, InetSocketAddress> implements ByteBufHolder
    [Fact]
    public void DatagramPacketIsAnAddressedEnvelope()
    {
        var recipient = new IPEndPoint(IPAddress.Loopback, 1);
        var sender = new IPEndPoint(IPAddress.Loopback, 2);
        var packet = new Channels.Sockets.DatagramPacket(Buffer.Unpooled.Buffer(1).WriteByte(1), recipient, sender);
        var same = new Channels.Sockets.DatagramPacket(Buffer.Unpooled.Buffer(1).WriteByte(1), recipient, sender);
        Assert.IsAssignableFrom<DefaultAddressedEnvelope<Buffer.ByteBuf, IPEndPoint>>(packet);
        Assert.StartsWith("DatagramPacket(" + sender + " => " + recipient + ", ", packet.ToString());
        Assert.NotEqual(packet, same);
        Channels.Sockets.DatagramPacket copy = packet.Copy();
        Assert.Equal(recipient, copy.Recipient);
        Assert.Equal(sender, copy.Sender);
        Assert.True(copy.Release());
        Assert.True(packet.Release());
        Assert.True(same.Release());
        Assert.Equal("recipient and sender",
            Assert.Throws<ArgumentNullException>(() => new Channels.Sockets.DatagramPacket(Buffer.Unpooled.Buffer(0), null, null)).ParamName);
    }

    // NioDatagramChannel(DatagramChannel) and filterOutboundMessage(Object)
    [Fact]
    public void NioDatagramChannelWrapsASocketAndFiltersMessagesAsTheSource()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        var channel = new Channels.Sockets.NioDatagramChannel(socket);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        Assert.Same(socket, typeof(Channels.Nio.AbstractNioChannel).GetMethod("JavaChannel", flags)!.Invoke(channel, null));
        Assert.Throws<ArgumentException>(() => new Channels.Sockets.NioDatagramChannel(
            new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp)));
        var filter = typeof(Channels.Sockets.NioDatagramChannel).GetMethod("FilterOutboundMessage", flags)!;
        var e = Assert.IsType<NotSupportedException>(Assert.Throws<System.Reflection.TargetInvocationException>(
            () => filter.Invoke(channel, new object[] { "x" })).InnerException);
        Assert.Equal("unsupported message type: String (expected: DatagramPacket, IAddressedEnvelope<ByteBuf, EndPoint>, ByteBuf)", e.Message);
    }

    // NioDatagramChannel(): DatagramChannel.open() uses INET6 when IPv6 is available and clears IPV6_V6ONLY.
    [Fact]
    public void ParameterlessNioDatagramChannelOpensTheDefaultDualStackFamily()
    {
        var channel = new Channels.Sockets.NioDatagramChannel();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var socket = (System.Net.Sockets.Socket)typeof(Channels.Nio.AbstractNioChannel).GetMethod("JavaChannel", flags)!.Invoke(channel, null);

        try
        {
            if (System.Net.Sockets.Socket.OSSupportsIPv6)
            {
                Assert.Equal(System.Net.Sockets.AddressFamily.InterNetworkV6, socket.AddressFamily);
                Assert.Equal(System.Net.Sockets.Socket.OSSupportsIPv4, socket.DualMode);
            }
            else
            {
                Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, socket.AddressFamily);
            }
        }
        finally
        {
            socket.Dispose();
        }
    }
}
