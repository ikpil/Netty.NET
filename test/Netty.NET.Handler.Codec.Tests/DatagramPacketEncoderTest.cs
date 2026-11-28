/*
 * Copyright 2016 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.String;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: AddressedEnvelope is IAddressedEnvelope and InetSocketAddress is IPEndPoint;
// SocketUtils.socketAddress(host, port) is replaced by native socket operations, where a numeric host is an
// IPAddress in an IPEndPoint.
public class DatagramPacketEncoderTest : IDisposable
{
    private EmbeddedChannel channel;

    // CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setUp() runs from the
    // constructor and the @AfterEach tearDown() from Dispose() (https://xunit.net/docs/shared-context).
    public DatagramPacketEncoderTest()
    {
        SetUp();
    }

    public void Dispose()
    {
        TearDown();
    }

    // CLR: the type argument String becomes IEnumerable<char>, the message type of StringEncoder (the CLR form of
    // CharSequence), because C# has no '? super String' wildcard; a DefaultAddressedEnvelope<string, IPEndPoint> is an
    // IAddressedEnvelope<IEnumerable<char>, IPEndPoint> through the covariant IAddressedEnvelope.
    private void SetUp()
    {
        channel = new EmbeddedChannel(
                new DatagramPacketEncoder<IEnumerable<char>>(
                        new StringEncoder(Encoding.UTF8)));
    }

    private void TearDown()
    {
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestEncode()
    {
        TestEncode0(false);
    }

    [Fact]
    public void TestEncodeWithSenderIsNull()
    {
        TestEncode0(true);
    }

    // CLR: xUnit1024 forbids overloading the TestEncode test name.
    private void TestEncode0(bool senderIsNull)
    {
        IPEndPoint recipient = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 10000);
        IPEndPoint sender = senderIsNull ? null : new IPEndPoint(IPAddress.Parse("127.0.0.1"), 20000);
        Assert.True(channel.WriteOutbound(
                new DefaultAddressedEnvelope<string, IPEndPoint>("netty", recipient, sender)));
        DatagramPacket packet = channel.ReadOutbound<DatagramPacket>();

        try
        {
            Assert.Equal("netty", packet.Content.ToString(Encoding.UTF8));
            Assert.Equal(recipient, packet.Recipient);
            Assert.Equal(sender, packet.Sender);
        }
        finally
        {
            packet.Release();
        }
    }

    [Fact]
    public void TestUnmatchedMessageType()
    {
        IPEndPoint recipient = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 10000);
        IPEndPoint sender = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 20000);
        DefaultAddressedEnvelope<long, IPEndPoint> envelope =
                new DefaultAddressedEnvelope<long, IPEndPoint>(1L, recipient, sender);
        Assert.True(channel.WriteOutbound(envelope));
        DefaultAddressedEnvelope<long, IPEndPoint> output = channel.ReadOutbound<DefaultAddressedEnvelope<long, IPEndPoint>>();

        try
        {
            Assert.Same(envelope, output);
        }
        finally
        {
            output.Release();
        }
    }

    [Fact]
    public void TestUnmatchedType()
    {
        string netty = "netty";
        Assert.True(channel.WriteOutbound(netty));
        Assert.Same(netty, channel.ReadOutbound());
    }

    [Fact]
    public void TestIsNotSharable()
    {
        TestSharable(false);
    }

    [Fact]
    public void TestIsSharable()
    {
        TestSharable(true);
    }

    private static void TestSharable(bool sharable)
    {
        MessageToMessageEncoder<IAddressedEnvelope<ByteBuf, IPEndPoint>> wrapped =
                new TestMessageToMessageEncoder(sharable);

        DatagramPacketEncoder<IAddressedEnvelope<ByteBuf, IPEndPoint>> encoder =
                new DatagramPacketEncoder<IAddressedEnvelope<ByteBuf, IPEndPoint>>(wrapped);
        Assert.Equal(wrapped.IsSharable, encoder.IsSharable);
    }

    private sealed class TestMessageToMessageEncoder
            : MessageToMessageEncoder<IAddressedEnvelope<ByteBuf, IPEndPoint>>
    {
        private readonly bool sharable;

        internal TestMessageToMessageEncoder(bool sharable)
        {
            this.sharable = sharable;
        }

        protected override void Encode(
                IChannelHandlerContext ctx, IAddressedEnvelope<ByteBuf,
                IPEndPoint> msg, IList<object> @out)
        {
            // NOOP
        }

        public override bool IsSharable => sharable;
    }
}
